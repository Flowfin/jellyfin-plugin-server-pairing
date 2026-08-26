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

The store is an interface, so the model is provable before anything durable
exists:

    git grep -n 'interface IUserMappingStore' -- Jellyfin.Plugin.ServerPairing/Mapping/

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

## An unmapped user is not synced

Silently, and by default. Asking for the mapping of a user who has none returns
nothing rather than a guess, which is the fail-closed direction; the alternative
is deciding from a name which peer user somebody is, which is the failure at the
top of this document.

## What this document does not cover

**Where the table is written.** The store is an interface and the only
implementation is the one the suite substitutes. A file on disk, with the atomic
write and the permissions the key store already has, is not built.

**The administration surface.** Nothing renders this table, nothing lists it, and
no endpoint reaches it. That is issue #40, behind the dashboard page in #49.

**The log line for a skipped user.** An unmapped user being skipped is a
behaviour of a sync path, and no sync path exists in this plugin. Nothing here is
in [`docs/logging.md`](logging.md)'s table yet, and the assertion that the skip is
visible is not written, because there is nothing to skip.

**Removing what is held about one person.** Reporting and removing everything
held for a user is issue #60, and it needs this table plus a surface to ask
through.
