# The key store

Where a pairing's key material is kept, why it is not kept where a plugin's
settings are kept, and what protecting it does and does not mean.

## Why it is not the plugin configuration

The host writes a plugin's configuration object to disk with the XML serialiser
and serves it back to the dashboard through its own configuration endpoint. That
is what a configuration is for and there is nothing wrong with it; it is simply
not somewhere a key can go. A key on that object would be plaintext in a file,
in every backup of the server's settings, and readable by anyone who can open
the plugin's own settings page.

So the key store is a separate thing, with its own interface, its own file and
its own rules. The interface is what the suite drives, so it is defined
independently of the file format:

    git grep -n 'interface IPairingKeyStore' -- Jellyfin.Plugin.ServerPairing/KeyStore/

That nothing on the configuration can reach key material is refused rather than
stated. `ConfigurationKeyMaterialTests` walks the compiled type graph of
`PluginConfiguration`, declared and inherited members at any depth, and fails on
any member whose type is a run of bytes, comes from the cryptography namespace,
or is the key type itself.

## Where the file is

Under the server's data path, in this plugin's own directory, and never under
the directory the host writes plugin configurations into. The path is derived
from the host's own `IApplicationPaths` rather than written down here, so a
server whose data directory is somewhere unusual is served correctly with no
setting to get wrong:

    git grep -n 'DirectoryName\|FileName' -- Jellyfin.Plugin.ServerPairing/KeyStore/KeyStorePath.cs

`KeyStorePathTests.TheKeyFileIsNotUnderThePluginConfigurationDirectory` is the
assertion, and it compares against the two paths the host reports rather than
against a string written twice.

**THE DIRECTORY IS NO LONGER THIS STORE'S ALONE.** The pairing record store
writes a second file beside this one, under the same directory and the same
permissions, and issue #311 is where that landed. It holds what state each
pairing is in and how it got there and no key material of any kind, so nothing in
the rest of this document changes; what does change is that a reader listing that
directory will find two files rather than one, and that an operator moving this
plugin's state moves both. The two are separate files on purpose: a key store
that refuses is not a reason an operator cannot be told what state a pairing is
in.

    git grep -n 'public const string FileName' -- Jellyfin.Plugin.ServerPairing/Protocol/RecordStorePath.cs

## What the store holds

Three things per pairing: the current key, the key a rotation superseded, and
the instant that superseded key stops verifying. All three are persisted rather
than only the first, which is the answer taken on issue #30 on 2026-08-24. A
store that kept only the current key would lose the superseded one across a
restart, and a peer that had not yet caught up would stop being understood -
which is exactly the failure the rotation overlap exists to prevent.

Every read takes the instant it is judged at. A superseded key whose overlap has
run out is not a key any more, and a store that answered with one because nobody
had swept it would hand a caller something the rotation already ended. The
boundary is the instant itself: a key exactly at its own end is gone.

## The format number the file carries, and what an older file does

The file is an object with two members: `format`, which is a number, and
`pairings`, which is the map described above. The number is written from the
first write onwards, so no file this build produces is in an unnumbered shape.

The number exists so that a build meeting a file it does not understand says so
instead of reading what it recognises and dropping the rest. Two directions, and
they are answered differently.

**A file older than this build is carried up.** `StoreFormat` holds one
migration per format, and a read walks them in order from whatever the file
declares to the format this build writes. One rung at a time rather than a jump,
because a jump has to be rewritten every time a rung is added below it and a file
three formats old would then travel a path no fixture ever took. Every rung works
on the parsed document rather than on this plugin's own types, so a member a rung
does not name is carried across untouched.

**A file newer than this build is refused.** `StoreFormatRefusedException`, and
every operation on the store raises it, because every operation reads the file.
That is the downgrade case: an operator installs a newer plugin, pairs, rolls the
plugin back, and the file on disk was written by a build that knew more. Reading
it as far as it parses would drop whatever the newer format added, and the drop
would land on key material. So no pairing works and the message says which format
was found, which is the highest this build understands, and what to do about it.

**Format 0 is not a format anybody designed.** It is what this store wrote before
the number existed: the bare map, no envelope. It is named so a file already on
an operator's disk has a rung to start from, and
`Jellyfin.Plugin.ServerPairing.Tests/KeyStore/Fixtures/keys.format-0.json` is a
file in it, produced by running the store at the commit before the envelope
rather than typed out. That is the rule the migration cases are held to: a case
that builds the old shape out of the current types is a case about the current
types.

**Migrating writes two files and a read is where it happens.** The copy of what
was there goes beside the store, named for the format it is in, so an operator
can see it without opening it; then the migrated file replaces the store, through
the same atomic write as everything else. The copy is written first on purpose. A
migration that fails at the second write leaves the original store exactly as it
was, a copy of that same original beside it, and every operation still refusing -
rather than a store that has been half replaced. The next call reads the original
again and tries the same migration, which is what an operator who has just freed
some disk space wants.

**What the copy is, and what it is not.** It holds key material, so it is written
with the store's own permissions, and it is not a backup an operator should rely
on: it is the one file the last migration replaced, and a second migration away
from a different format writes a different name rather than rotating this one.
Nothing removes it, and nothing reads it.

**A migration is the one thing the store does that nobody asked it for, so it
says so.** One line at Information, naming the format the file was in, the format
it is now, and the copy left beside it. It is written after both writes rather
than before either: a line saying the store was carried up, written by a run that
then failed to carry it, names a file still to be migrated and reads as done. A
store needing no migration writes nothing, because this file is read on every call
and a line on every read is a line an operator learns to skip. The row is in
[what this plugin logs](logging.md).

**What this does not do**, stated so the section is not read as more than it is.
The migration preserves a member it does not recognise on the way up, and the next
write does not: a write serialises this build's own type and holds only what that
type holds. And the plugin configuration carries no format number, which is the
other half of issue #55. THIS SENTENCE SAID THAT HALF WAITED ON ISSUE #50 BECAUSE
THE CONFIGURATION TYPE HELD ONLY THE TEMPLATE'S EXAMPLE FIELDS. It holds settings
an operator sets now, and issue #50 is closed:

```
git grep -n 'public int EnrolmentWindowSeconds\|public string PeerAddress' origin/master -- Jellyfin.Plugin.ServerPairing/Configuration/PluginConfiguration.cs
origin/master:Jellyfin.Plugin.ServerPairing/Configuration/PluginConfiguration.cs:130:    public string PeerAddress { get; set; }
origin/master:Jellyfin.Plugin.ServerPairing/Configuration/PluginConfiguration.cs:158:    public int EnrolmentWindowSeconds { get; set; }
```

So what that half waits on is a decision about which number to stamp and when,
which is issue #55's, rather than a type with nothing on it worth versioning.

## A file that is there and is not a key store

A file that does not hold what a key store holds is refused. Every operation on
the store refuses with it, because every operation reads the file, and nothing
repairs, truncates or replaces the file on the way to refusing.

**The failure this exists against is not a crash.** It is the quiet answer that
used to stand in its place. A file that parsed as anything other than an object
was read as an empty store, and an empty store is what a fresh installation has.
So an operator whose store had been truncated, half-overwritten or replaced saw a
plugin with no pairings, concluded the pairings were gone, and did the one thing
that makes the loss permanent: paired again, over bytes that were still on the
disk in front of them.

**What counts as damaged**, which is every shape that is not one this plugin
writes:

- bytes that do not parse as JSON at all, which is what truncation and a partial
  overwrite actually look like: an empty file, a run of NUL bytes, a document cut
  off part way, anything that is not JSON
- JSON that parses and is not an object: an array, a number, a string, a boolean,
  and the literal `null`, which this store answered as empty before this rule and
  no longer does
- an object carrying the envelope whose `pairings` member is absent, or is
  anything other than an object: every write this store makes puts an object
  there, so a file without one was not written by this plugin
- pairings that are not pairings, which is where a file damaged inside the member
  the keys live in is caught rather than at the parse

**The refusal reads the file before it migrates it.** A file in an older format
is carried up on the first read, which writes two files; a file whose keys are
damaged is refused with nothing written, because the pairings are read out of the
document as it arrived rather than out of the migrated one. Without that order a
damaged store would be rewritten and copied on the way to a refusal that says
nothing has changed the file.

**What an operator does about it.** Move the file aside and keep it. It is
refused rather than read, so what is in it is exactly what was in it, and
whoever looks at it later has the whole of it. Pairing afresh before moving it
aside is the one action to avoid, because the first write of a new store replaces
the file. Both servers have to be paired again once the store is gone, since the
peer holds its own half and this side's key is not recoverable from it.

**What this does NOT see, and it is the larger half of issue #33.** A file that
is an intact key store and is nevertheless the wrong one is not damaged in any
way a reading of that file can find:

- a store restored from a backup taken before a rotation, so this side offers a
  key the peer has already retired
- a store restored from before an enrolment, so this side has no pairing the peer
  still believes in
- a store restored from before a revocation, so this side holds a key the
  operator deliberately destroyed
- a store copied to a second machine, so two servers hold the same pairing
  identity and both sign requests the peer accepts

Each of those parses, carries the envelope and holds well-formed keys. Telling
them apart from the store they were copied from needs something the peer can
see, and the decision taken on issue #33 is that no such field is added to the
wire: it would move the problem rather than solve it, because a peer that was
itself restored presents valid signatures over a state it legitimately holds, and
nothing on the wire separates a rewound peer from an attacker. So **a peer that
was restored behind this server's back is not detectable from here**, stated as
undetectable rather than covered by a mechanism that implies otherwise.

That sentence is held in place by `KeyStoreDocumentTests` in the test project,
which reads this section and refuses its absence, and refuses the loss of the
clause naming it undetectable rather than covered. An admission that something is
not seen from here is the one kind of sentence a later edit removes without
anybody noticing, and it is the one kind that has to survive.

What stands in one direction only is the peer's own state, and it is a property of
the specification rather than of anything running: `Revoked` is terminal in the
state machine, so a peer that holds a pairing revoked answers nothing under it and
a request from a copy restored to before that revocation is refused there. No
route on a server reaches `Revoked` yet, which is issue #24, so this is what the
transition table says rather than something anybody has watched happen.

## The type key material travels in

Key material has its own type rather than being an array of bytes. That is not
tidiness. The rules issue #32 exists for cannot be written against `byte[]`: an
array prints its type name into a log without complaint, serialises wherever a
serialiser meets it, and is indistinguishable from every other array in a
reflection walk.

What the type does today:

- its string conversion carries none of its bytes, so a careless interpolation
  into a log line or an exception message produces a placeholder
- neither serialiser this plugin's dependencies bring can write one at all. Both
  refuse the type, because its only accessor is a span and a span is a ref
  struct neither can represent
- comparison is `CryptographicOperations.FixedTimeEquals` and there is no
  equality operator to reach for by habit
- destroying it overwrites the bytes and makes it unusable, and asking a
  destroyed key for its bytes is an error rather than an empty answer

## How writes are made safe, and against what

**The mechanism is one lock per store instance and one atomic replace per
write.** Not a concurrent collection, not a type whose name sounds safe.

Every operation on a store instance is taken under that lock, so a rotation and
a read never interleave. Every write goes through `AtomicWrite.Replace`, which
writes a temporary file beside the destination and then moves it over, so a
reader sees the file as it was before the write or as it is after it, and never
as it is during one:

    git grep -n 'class AtomicWrite' -- Jellyfin.Plugin.ServerPairing/KeyStore/

The temporary file lives in the same directory as its destination. A move across
a filesystem boundary is a copy and a delete, which is the window this exists to
close, and a temporary directory is a different filesystem on more machines than
not. Nothing reads a file with the temporary suffix, so one left behind by a
process that died is inert and the next write overwrites it.

A write that fails anywhere leaves the destination as it was, and the failure
reaches the caller rather than being swallowed. A store that reported success on
a write that did not happen would leave a rotation the peer believes in and this
server has not made.

**What the lock does not cover.** It is per instance, so what makes it cover the
server is the singleton registration: two instances over one file would each
serialise their own callers and neither would see the other. A second process is
out of reach entirely - an operator editing the file by hand while the server
runs is serialised by nothing, and the last write wins.

**What the atomic replace does not cover.** The bytes are not forced to the
platter before the move. The move is ordered after the write by this code and
not by any promise the filesystem makes, so a machine that loses power between
them may come back with the move done and the contents not. Closing that needs a
flush the runtime does not expose portably.

## When it is created, and with what permissions

Nothing exists before the first pairing. The store's directory and its file are
brought into existence by the first write and by nothing else, so a server that
installs this plugin and never pairs has no key file to protect.
`StorePermissionsTests.NothingIsCreatedUntilSomethingIsWritten` drives the store
through every read it offers and asserts that neither appears;
`TheFirstWriteCreatesBoth` is the other half, so lazily created cannot quietly
become never created.

Where the platform expresses a Unix mode, both are created with theirs rather
than given them afterwards:

    git grep -n 'public const UnixFileMode' -- Jellyfin.Plugin.ServerPairing/KeyStore/StorePermissions.cs

The directory is `0700` and the file is `0600`. **The mode is a creation
argument in both cases, and that is the point rather than a detail.** A file
created with the platform's default and narrowed on the next line exists, for
that line, under whatever the umask gave it, and the key material is already in
it. The atomic replace is where this is easy to get wrong: a move preserves the
mode of the file being moved, so the file whose creation mode becomes the
store's is the temporary one, not the destination.
`StorePermissionsTests.TheTemporaryIsNarrowBeforeItBecomesTheStore` reads the
temporary's mode while it still exists rather than inferring it from the
destination afterwards.

A directory that is already there with permissions wider than `0700` is refused
and not narrowed. The refusal names the path. Narrowing it would be a change
made to an operator's server without saying so, and this plugin cannot tell a
directory somebody widened on purpose from one widened by accident; writing keys
into it either way is the worse of the two. `EveryPermissionPastTheStoreSOwnIsRefusedOnItsOwn`
walks every permission beyond the store's own one at a time, so a guard catching
world-readable and letting group-writable through fails here.

**On Windows none of this happens, and the residual risk is not smaller for the
rest of this section.** A Unix mode is not expressible there, so the directory
and the file are created with whatever the platform gives them and no check is
made. What protects the store on a Windows server is the access control the
operator has on the server's data directory, which this plugin neither reads nor
sets. Every case above that needs a mode is skipped there with that reason rather
than passed, so a green suite on Windows says nothing about the store's
permissions:

    git grep -n 'class UnixModeFactAttribute' -- Jellyfin.Plugin.ServerPairing.Tests/

**What the permissions do not do.** They stop a process running as another user
from reading the file. They stop nothing that runs as the server's own user, and
they stop nothing that reads the disk from underneath the running system - a
backup, a restored image, a stolen drive, or the operator themselves. The keys
are on disk in a form anybody who can read the file can use, because nothing here
encrypts them. What that leaves standing, adversary by adversary, is the two
sections below.

## What protects it at rest, and what does not

**Nothing in this plugin encrypts the file.** The mechanism at rest is the two
Unix modes in the section above, on a platform that has them, and the access
control the operator holds on the server's data directory everywhere. That is
the whole of it, and the rest of this section says what that buys and what it
does not.

There is no derivation, no passphrase, no wrapping key and no keyring, so there
is no input whose location this section could write down. That sentence is here
because its absence is the thing a reader goes looking for.

**The host has no key wrapping service to ask for.** Read in a checkout of
`jellyfin/jellyfin` at the two tags this repository supports:

```
git rev-parse v10.11.9 v12.0-rc3
e83a7e62f26443f7dd98f126d6955ac1af090125
fc43f151a2418cc112e116050a99dd6318917ab0

git grep -l 'IDataProtectionProvider\|AddDataProtection' v10.11.9 v12.0-rc3 -- '*.cs' ; echo "exit=$?"
exit=1
```

Neither line registers a data protection provider, so nothing in the container
would answer a request for one. Whatever protects this store, this plugin brings
or does without, and today it does without.

**What encrypting it with a key kept beside it would buy.** It protects against
somebody who takes one file and not the other, and against nothing else. A
process running as the server user reads both, and so does a filesystem backup
that copied the data directory. Naming it precisely matters because the phrase
"encrypted at rest" is read as the whole of a protection when it is a thin slice
of one, and a plugin that says its keys are encrypted and stops there has told an
operator something false by leaving the rest out.

**Why this file does not describe such a scheme today.** THIS PARAGRAPH SAID THE
STORE HAS NO ANSWER FOR A FILE THAT DOES NOT PARSE AND THAT THE CORRUPTION HALF
WAS WHAT WAS LEFT. It has one: a file that is there and is not a key store is
refused, which is the section above, and both halves this paragraph named are
therefore answered. The format version was the first of them - a wrapping layer
arriving later is a rung on the ladder above rather than a shape nothing can
migrate - and damage is the second, so a wrapping layer added now has a refusal
to be told apart from rather than a silence. Whether to add one at all, where the
wrapping key would live, and what a server does when the file is there and the
key is not, is issue #268 rather than this paragraph, and none of that is decided
by the corruption answer arriving.

## Residual risk, adversary by adversary

The list is the one in [the threat model](threat-model.md), and this section
answers for every adversary there that can read the filesystem. What each one
obtains is the same bytes; the difference between the rows is what stands
between them and the file.

**A5, somebody holding a stolen file, a backup or a log.** From the key store
they obtain every per pairing key it holds, current and superseded, in a form
they can use directly - the file is hex inside JSON and nothing has to be broken
to read it. What they do not obtain from it is a credential for the server: the
keys are per pairing, so each authorises one pairing and nothing else. The log is
not one of the places they get it from, which is [what this plugin
logs](logging.md).

**A6, somebody who can read the server filesystem, including a backup archive.**
The same bytes, and this is the row the permissions are actually about. The
`0700` directory and the `0600` file stop another user on the same host. They
stop nothing that already runs as the server's own user, nothing that reads the
disk from underneath the running system, and nothing that holds a copy of a
backup - a backup archive carries the file's contents whatever mode it had. On
Windows no mode is set at all and what stands in its place is the operator's
access control on the data directory. Against a copy already taken, the answer is
revocation after the fact rather than confidentiality of the file, because there
is no confidentiality of the file.

**A8, a compromised consumer plugin.** Its headline reach is memory rather than
the filesystem, and it belongs here anyway: it runs in this process, as the
server's user, so the file is readable to it as an ordinary file. The permissions
are not a boundary against it, and neither would encryption with a key kept
beside the store be, because it reads both.

**Who is not on this list, and why.** A4, a signed in user who is not an
administrator, reaches their own server's user API, and no path on it hands out a
file from the data directory. A1, A2 and A3 are on the network or are the peer,
and none of them reads this disk. A7 reaches endpoints. Their rows are in the
threat model and none of them is about this file.

**What no row of this section says.** None of them says the keys are protected
against an adversary who has the file. They are not. Every row above is about who
can reach the file, and the answer once somebody has is the same in all three.

## What zeroing does not achieve

`KeyMaterial.Destroy` calls `CryptographicOperations.ZeroMemory` on the array it
holds. **That narrows the window in which the key is in memory. It does not
close it.**

The runtime is free to move a managed array while a garbage collection compacts
the heap, and what it leaves behind at the old address is not zeroed by anything
this code can call. So a copy of the key may remain in memory the process no
longer considers live, until that memory is written over by something else. The
same applies to any copy the operating system made: a page written to swap, or a
crash dump, holds whatever was in it at the time.

Read that as: destroying a key stops this plugin from using it and shortens the
period in which it is trivially recoverable from the running process. It is not
a guarantee that the bytes are gone from the machine.

## What is NOT decided in this document, and where it is

Each of these is a real gap, open, and named so that reading this file is not
mistaken for reading a finished design.

- **Whether the file should be encrypted at all.** What protects it today, and
  what that does not reach, is the section above; issue #31 is answered by it.
  What is not decided is whether a wrapping layer with a key kept beside the
  store is worth adding, where that key would live, and what a server does when
  the file is there and the key is not. Issue #268 holds that question.
- **What a Windows server does about permissions.** The section above is what
  happens where a Unix mode exists. On Windows the directory and the file are
  created with whatever the platform gives them, nothing is checked, and this
  plugin sets no access control of its own. Issue #35 landed the Unix half and
  did not decide this one.
- **A restored or copied store.** THIS ENTRY SAID A FILE THAT DOES NOT PARSE
  THROWS RATHER THAN BEING ANSWERED FOR, AND THAT ALL THREE CASES WERE UNDECIDED.
  The corrupt one is decided and built: a file that is there and is not a key
  store is refused, which is the section above. What is left is the two that no
  reading of one file can see - a store restored from a backup and a store copied
  to a second server - and the wire carries nothing that would let a peer see them
  either, which is the decision recorded on issue #33 rather than an absence. That
  issue owns what is left. The format number above reaches none of it: it
  separates a file this build is too old to read from one it can, and says nothing
  about a file that is an older copy of this same store.
- **A format number on the plugin configuration.** Only the key store carries
  one. This entry said the configuration type held only the template's example
  settings, so that a number stamped on it would version fields no operator ever
  sets. That type carries settings an operator sets now, which is issue #50 closed
  rather than a reason that still holds, and the section above reads the two of
  them out of it. Issue #55 holds the number, and what it is waiting on is a
  decision about when to stamp one rather than a type with nothing worth
  versioning on it.
