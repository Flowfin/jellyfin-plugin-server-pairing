# The user mapping table

Who on this server is who on the peer, why that is a decision an administrator
makes rather than one this plugin works out, and what happens to the table when
a pairing ends.

This is not [`docs/matching.md`](matching.md). That document is about matching an
item on one server to an item on the other, by provider identifiers. This one is
about people.

## Why nothing is inferred

Two servers have two sets of users, created independently, with no shared
identifier. Something has to say that the person called Anna here is the person
called Anna there, and the only thing that knows is a person.

Every earlier attempt at this problem matches usernames automatically, which
[`docs/prior-art.md`](prior-art.md) records. It works until two servers share a
household, where both sides have a `dad` and they are two different people. The
failure is not a wrong row in a table; it is one person's viewing history landing
on another person's account, on a server whose operator has no reason to look.

So this plugin offers no method that takes two sets of users and returns a
correspondence. That is refused rather than merely avoided:

    git grep -n 'public void NoPluginSourceFileMatchesUsersByName' -- Jellyfin.Plugin.ServerPairing.Tests/Mapping/

A dashboard may offer a name similarity as a suggestion, clearly marked as one,
and an administrator still confirms each row. What they confirm arrives at the
one method that writes a mapping, which takes the administrator as a required
argument, so no route into the table exists that does not name who decided.

## What a mapping holds

One local user identifier, one peer user identifier, the pairing it belongs to,
and a cached display name. Both identifiers are opaque to this plugin: it never
parses one, compares one to the other, or derives one from the other.

    git grep -n 'public string' -- Jellyfin.Plugin.ServerPairing/Mapping/UserMapping.cs

The display name is a cache and is never the truth about who the peer user is. It
exists so a dashboard can show a person something they recognise beside an opaque
identifier. It may be discarded at any moment, nothing decides anything from it,
and it is allowed to be empty, because a peer that sends no display name is not a
reason to refuse a row an administrator asked for.

It is also personal data sitting next to a table that deliberately holds none,
which is why it may not outlive the row it decorates. That is asserted rather
than intended, by both routes that remove a row.

## Where the table lives

With the pairing state, not in the plugin configuration, for the same reason the
key store is not there — [`docs/keystore.md`](keystore.md) argues that at length.
The short form is that the configuration is a file an operator edits by hand and
the host rewrites as plaintext XML, and a table deciding where one person's data
goes does not belong in it.

The store is an interface, so the model was provable before anything durable
existed:

    git grep -n 'interface IUserMappingStore' -- Jellyfin.Plugin.ServerPairing/Mapping/

**THIS SECTION SAID THE ONLY IMPLEMENTATION WAS THE ONE THE SUITE SUBSTITUTES.**
There is a file now, in the same directory as the key store and the pairing
records and under the same permissions:

    git grep -n 'public const string FileName' -- Jellyfin.Plugin.ServerPairing/Mapping/MappingStorePath.cs

It is a third file rather than a member of either of the other two because the
three refuse separately. A key store that refuses is not a reason an
administrator cannot be shown which users are mapped, and a mapping carries no
key material for the two to share a refusal over.

Every operation reads the file, changes what it holds and writes it back, so
nothing is cached to go stale against a file somebody replaced; every write goes
through the atomic write the key store uses, so a reader sees the table as it was
or as it is and never as it is halfway through a write; and the lock that
serialises the operations is per instance, which is why the server gets exactly
one of these and a second process is out of reach entirely.

**A file that is there and is not a mapping store is refused rather than answered
as an empty table**, which is the key store's answer read one file over. An empty
table is what a fresh installation has, so an administrator meeting one makes the
mappings again on top of rows that are still on the disk. A row this build could
not turn into a mapping — a blank pairing, a blank user on either side, a blank
actor, or no display name member at all — is damage of the same kind and is
refused with the rest of the document, because a table quietly one row shorter
than its file sends one person's data nowhere or to somebody else.

The file carries a format number, and there is no format 0 for the reason the
pairing record store gives: this store has never shipped without an envelope, so
a file carrying no number was not written by this plugin. A number higher than
this build reads is a rolled-back plugin rather than damage, and the two are
separate refusals because what an operator does about them is separate.

Every operation on it is keyed by pairing first, and there is deliberately no way
to ask it for every mapping it holds regardless of pairing. A caller that wants
them all walks the pairings, because a mapping outside a pairing is the one thing
this model does not allow.

## A mapping cannot exist without a pairing

Two halves, and they fail in opposite directions.

**On the way in.** A mapping is refused where the pairing is `Absent`, which is
what an identifier nothing is held for reads as, and refused where the pairing is
`Revoked`. Those are two different answers rather than one, because an
administrator told only that something failed goes looking in the wrong place.

**On the way out.** The mappings for a pairing go when it reaches `Revoked` or
`Absent`. That is driven by the transition rather than by the record being
deleted, and the difference is the whole of it: reaching `Absent` deletes the
record, and reaching `Revoked` keeps it on purpose so a later request naming that
identifier is refused rather than treated as new. A mapping table swept when a
record is deleted would survive every revocation.

    git grep -n 'PairingState.Absent or PairingState.Revoked' -- Jellyfin.Plugin.ServerPairing/Protocol/PairingStateMachine.cs

The `Absent` path is the one that is easy to forget, because nothing was revoked.
An enrolment window expiring takes a half-built pairing there, and an
administrator may have made mappings before both confirmations were in.

Only the state machine sweeps a table, and that is asserted too. A caller that
empties the table when it believes a pairing has ended makes the removal depend
on that caller being right, and the state machine is the one type that knows.

## One on each side, per pairing

Two rules and they are one rule, which is why they are written together.

**One local user maps to at most one peer user, and one peer user to at most one
local user.** Both directions are refused, and the second is the one that gets
left out. A table that guards only the local side accepts two local users both
pointing at one peer user, which is two people's history arriving on one account
- the failure this whole model exists against, reached from the side nobody
watched.

**Refused, never replaced.** A second mapping for either side is turned down and
the mapping already there is left exactly as it was, field for field. The two
answers are separate values rather than one, for the reason the section above
gives: `LocalUserAlreadyMapped` and `PeerUserAlreadyMapped` send an administrator
to different places.

    git grep -n 'AlreadyMapped' -- Jellyfin.Plugin.ServerPairing/Mapping/MappingOutcome.cs

**A refusal can say which mapping is in the way.** Both directions are readable:
the mapping held for a local user, and the mapping that claims a peer user. So
what an administrator is told is which correspondence stopped them, rather than
that something failed.

    git grep -n 'public UserMapping? From' -- Jellyfin.Plugin.ServerPairing/Mapping/UserMappings.cs

**Changing a mapping is removing it and making the new one**, and that is two
acts because it is two acts. A replacement reads as a repair and is not one:
everything that arrived under the old mapping stays on the user it arrived on,
and nothing here reaches it. That consequence is
[the data statement's](data.md), said in the words an operator will read in
`DestructiveWording.ChangeMapping`. An administrator who has to remove the old
mapping first has been shown that there was one.

**The rules are per pairing, and deliberately.** The same local user may map to
different peer users under two pairings, and the same peer user may be mapped
under two pairings, because those are different relationships between different
pairs of servers. A rule written over the whole table rather than over a pairing
would refuse both, and each of them has a case of its own so the scope cannot
quietly widen.

## What is not refused yet, and why

**A mapping to a local user who no longer exists.** Nothing here knows which
local users exist: this plugin holds no reference to the host's user manager and
takes no list of users on any call. Detecting it is a read of that set, and the
surface that would perform the read is the administration one. Issue #37 keeps
that rule.

**A mapping whose peer user no longer exists on the peer.** That is detected on
the next `exchange`, and `exchange` has no payload yet - the field table in
[the protocol specification](protocol.md) leaves it to M6.

**A local user being deleted.** The rule is that deleting one does not silently
delete the mapping, because a silent deletion loses the audit trail of what used
to be synced where. Nothing in this plugin is told when a user is deleted, so
there is no moment at which it could do either thing.

Each of the three is a rule with no code path to sit on rather than a rule that
was decided against, and none of them is asserted by a test, because a test over
a path that does not exist passes and goes on passing after the path is written.

## An unmapped user is not synced

Silently, and by default. Asking for the mapping of a user who has none returns
nothing rather than a guess, which is the fail-closed direction; the alternative
is deciding from a name which peer user somebody is, which is the failure at the
top of this document.

## The trail a change leaves

Every mapping made and every mapping removed writes one entry, and both go
through `UserMappings` and through nothing else, so the entry cannot be skipped
by a caller that forgot to write one. The row is
[`docs/logging.md`](logging.md)'s, and it carries three things: the pairing, the
administrator who made the change, and which way the mapping moved.

**Which way it moved is all the entry says about the change.** That is the answer
taken on issue #40 on 2026-08-31 rather than a shape chosen while building it.
The identities on either side of a mapping are the first item on the never-log
list, in any form, and the audit is the record an operator keeps longest and
pastes into a forum thread. So the log answers that a mapping moved, who moved it
and which way, and the mapping table answers which peer user a local user is
mapped to, live, to an operator entitled to ask.

What that costs is worth reading rather than implying: an operator whose user's
history went to the wrong account cannot reconstruct the old mapping from the
log. They can see that it changed, when, and who changed it, which is what lets
the change be noticed at all.

**A removal names who removed it.** `UserMappings.Unmap` takes the administrator
for the same reason `Map` does. It took none until this rule landed, so half of
every trail was a change nobody was named for.

**Removing a mapping that is not there writes nothing**, and neither does a
mapping this table refuses. An entry per call rather than per change would let
anything reaching this surface grow an operator's log without a mapping ever
moving.

**A pairing ending writes none of these.** The sweep is the relationship ending
rather than an administrator changing a mapping, and a revocation has its own row
at its own level. One revocation reported as many mapping changes would be
counted as many changes by whoever reads the log.

## What this document does not cover

**Anything writing through a surface.** The file above exists and nothing on a
server puts a row in it, because no endpoint and no page reaches the decision
surface. That is issue #40, behind the dashboard page in #49, and it is the
sentence below rather than a second one.

**The administration surface.** Nothing renders this table, nothing lists it, and
no endpoint reaches it. That is issue #40, behind the dashboard page in #49.

**The log line for a skipped user.** An unmapped user being skipped is a
behaviour of a sync path, and no sync path exists in this plugin. Nothing here is
in [`docs/logging.md`](logging.md)'s table yet, and the assertion that the skip is
visible is not written, because there is nothing to skip.

**Removing what is held about one person.** Reporting everything held for a user
is an action on the administrative plane, in [`docs/endpoints.md`](endpoints.md),
and it walks the pairing record store rather than the key store so that a mapping
under a pairing that has not finished enrolling is in the answer. Removing it is
the other half of issue #60 and is not built, for the reason
[`docs/data.md`](data.md) gives beside what it will cover.
