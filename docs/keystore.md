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
encrypts them; that is issue #31 and it is unchanged by this section.

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

- **What protects the file at rest.** Nothing in this plugin encrypts it. The
  keys are on disk in a form anybody who can read the file can use. Issue #31 is
  where what protects it, and what does not, is answered.
- **What a Windows server does about permissions.** The section above is what
  happens where a Unix mode exists. On Windows the directory and the file are
  created with whatever the platform gives them, nothing is checked, and this
  plugin sets no access control of its own. Issue #35 landed the Unix half and
  did not decide this one.
- **A restored, copied or corrupt store.** A file that does not parse currently
  throws rather than being answered for, and a store restored from a backup or
  copied to a second server is not detected. Issue #33 owns all three.
