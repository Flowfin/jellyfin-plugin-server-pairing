# What pairing moves, where it comes to rest, and how to remove it

Pairing moves user mappings between two servers, and a user mapping is personal
data. An operator should not have to work that out from the source, so it is
written here.

This is a statement about the design in [`protocol.md`](protocol.md), not about
something running. Nothing below has ever moved between two servers, and THE
REASON GIVEN HERE HAS STOPPED BEING THE REASON. This sentence said there is no
endpoint for a request to arrive at and no key store for one to be signed
against; both exist. What holds the claim up instead is that no key has ever been
put in that store by anything on a request path, so every request the endpoint
receives is refused before a body is read. What the tree does hold is in
[what exists today](threat-model.md#what-exists-today) with the command for each
half rather than counted a second time here. Every field named here is a field
the specification defines, and the milestone that owes the code is named where it
matters. No later edit of this file turns any of it into a statement that a
transfer was observed.

## The list of fields that cross the wire

Complete against [`protocol.md`](protocol.md) as it stands: every field that
document defines as crossing between the two servers appears below, and nothing
appears below that it does not define. The two are meant to be read side by side,
and a field added there without a row here is a defect in this file.

### On every request

| Field | What it is | Personal data | Where it comes to rest |
| --- | --- | --- | --- |
| `X-Pairing-Id` | The pairing identifier, a digest of the two servers' public keys | No. It is a digest of two public keys and names no person | In the pairing record on both sides, for the life of the pairing, and in the log, where [`logging.md`](logging.md) allows it by name |
| `X-Pairing-Version` | The protocol version selected at enrolment | No | In the pairing record on both sides, for the life of the pairing |
| `X-Pairing-Timestamp` | Seconds since the Unix epoch, for freshness | No | Nowhere. It is checked and dropped |
| `X-Pairing-Nonce` | 16 random bytes, hex | No | In the nonce store, in memory, per pairing, for the span [`protocol.md`](protocol.md) fixes and reads out of the constant that decides it |
| `X-Pairing-Signature` | The signature over the canonical form | No | Nowhere. It is verified and dropped, and [`logging.md`](logging.md) forbids it in a log at any level |

### On every response

| Field | What it is | Personal data | Where it comes to rest |
| --- | --- | --- | --- |
| `X-Pairing-Timestamp` | As above | No | Nowhere |
| `X-Pairing-Nonce` | As above | No | Nowhere. A response nonce is not stored; the request nonce it answers is what is remembered |
| `X-Pairing-Signature` | As above | No | Nowhere |

### In `hello`, both directions

| Field | What it is | Personal data | Where it comes to rest |
| --- | --- | --- | --- |
| offered public key | The DER `SubjectPublicKeyInfo` of the sender's long term key | No. It is a public key, and the private half never leaves the server that made it | In the pairing record on the receiving side, for the life of the pairing. In the key store, which is M4 |
| supported version range | Two integers | No | Not stored. The selected version is what is kept |
| peer address | The `https` address the sender believes it is talking to, entered by an administrator | No, unless an operator put a person's name in a hostname, which this plugin cannot detect and does not try to | In the pairing record on both sides, and in the log, where [`logging.md`](logging.md) allows a peer address by name |
| selected version | One integer | No | In the pairing record on both sides |
| pairing identifier | As `X-Pairing-Id` | No | As `X-Pairing-Id` |

### In `confirm`

| Field | What it is | Personal data | Where it comes to rest |
| --- | --- | --- | --- |
| fingerprint digest | The value the administrator on the sending side compared | No. It is a digest of two public keys | Not stored. It is compared against the value computed here and dropped, and [`logging.md`](logging.md) forbids the preimage of a fingerprint in a log |

### In `rotate`, both directions

| Field | What it is | Personal data | Where it comes to rest |
| --- | --- | --- | --- |
| replacement public key | As the offered public key above | No | As the offered public key above. The superseded key is dropped when the overlap closes, which `KeyOverlap.CloseIfElapsed` does and issue #23 built |
| the instant the old key stops verifying | A timestamp | No | In the pairing record until the overlap closes |

### In `revoke`

Nothing beyond the envelope crosses. That is the point of it.

### In `unpair`

Nothing beyond the envelope crosses, as in `revoke`. What separates the two is the
order on the sending side and the cause written on the record, which
[`protocol.md`](protocol.md) states at the transition table, and neither of those
is a byte on the wire.

### In `exchange`

This is where the personal data is, and this document cannot yet list its fields.
[`protocol.md`](protocol.md) defines `exchange` as an envelope whose payload is
opaque to the wire layer, and the payload is defined by the consumer contract in
M6, issue #43. Until that contract exists there is nothing to list, and a list
written here first would be this document inventing the fields that the protocol
is later checked against, which is the wrong direction and is exactly what the
second condition on issue #14 exists to prevent.

Two constraints on that payload are already settled and are recorded here so that
whoever writes the contract meets them rather than discovers them:

The mapping table holds opaque identifiers, and no peer username is at rest on
either server as truth. What the record holds, field by field, is the section
below. That the table is an administrator's decision rather than an inference is
issue #36 and is argued in [the mapping document](mapping.md).

A display name may cross so that an administrator can read the mapping page, and
it is a cache. It may be discarded at any moment, nothing is decided from it, and
it is never the identity a request is authorised against.

Until `exchange` has its fields, this document describes the envelope of a
transfer and not its contents. That is a real gap in a personal-data statement,
it is named here rather than papered over, and it closes when M6 lands.

## What the mapping table holds, field by field

One row per field of the record this plugin keeps for one mapped user. Nothing
else about that user is at rest here.

| Field | What it is | Why it is held | When it goes |
| --- | --- | --- | --- |
| `PairingId` | The pairing this mapping belongs to | A mapping outside a pairing is the one thing this model does not allow, and every read is keyed by it | With the pairing, whether it was revoked or removed |
| `LocalUserId` | The user on this server, as the server identifies them | The mapping answers who on this server a transfer is for, and nothing else identifies that user | With the mapping |
| `PeerUserId` | The user on the peer, as the peer identifies them | The other half of the correspondence, opaque here and never parsed, compared or derived from | With the mapping |
| `PeerDisplayName` | The peer's readable name for that user | A cache, so an administrator sees something they recognise beside an opaque identifier; nothing is decided from it and it may be discarded at any moment | With the mapping, and it may go sooner |
| `Actor` | The administrator who decided this mapping | A mapping is a decision somebody made, and a decision with no author cannot be audited | With the mapping |
| `At` | When they decided it | The other half of the audit trail: an administrator reading the table needs to know when this correspondence was asserted | With the mapping |

**Every row's answer to the last column is at the latest the end of the
pairing.** That is not a promise made here and checked nowhere: reaching
`Revoked` or `Absent` sweeps the pairing's mappings, which is a property of the
state machine rather than of a caller remembering to call something, and it is
asserted per field rather than by the row count going to zero.

**What is not on this list is the point of the list.** No email address, no
password material, no permission set, and no copy of the peer's user list. None
of those is needed to resolve a mapping or to tell an administrator which mapping
is which, and every one of them is worth stealing. A table that is read or taken
away carries two opaque identifiers, a readable name that is admitted to be a
cache, and who decided it when.

`PeerDisplayName` is the one field in the table that names a person, and it is
held as a cache rather than as truth for exactly that reason. A report of what is
held about somebody that lists the opaque identifier and leaves out the readable
name beside it has left out the only field that names them, which is why the
section on reporting below counts it.

The list is checkable rather than trusted. `MappingRecordDocumentTests` reads the
table above and the record's own members and fails on a field with no row, on a
row naming no field, and on a row that leaves a cell empty:

```
git grep -n 'public void EveryStoredFieldHasARow' -- Jellyfin.Plugin.ServerPairing.Tests/Mapping/MappingRecordDocumentTests.cs
```

## What is never sent anywhere else

This plugin sends nothing to any address other than the one an administrator
entered as the peer. There is no relay, no third party in the protocol, no
directory service and no update ping on the pairing plane.

There is no telemetry of any kind. Nothing is collected and nothing is sent, and
there is no opt-in that turns it on.

## The constraint, and what makes it true rather than intended

The constraint is that a transfer happens only between two servers that the same
operator has paired.

What makes it true is that there is no path in the protocol from no pairing to a
working pairing that does not pass through an administrator acting on each of the
two dashboards. That is readable from the transition table in
[`protocol.md`](protocol.md) rather than asserted here:

An `exchange` is answered in `Active` and in `Rotating` and nowhere else, and
`Rotating` is entered only from `Active`, so both rest on reaching `Active`. Its
two entries are from `ConfirmedHere` on a peer's `confirm`, and from
`ConfirmedByPeer` on the local administrator confirming. So reaching `Active`
requires the local administrator's confirmation in both cases, and by symmetry it
requires the far administrator's on the other side. There is no unattended mode, which is settled
in issue #1, so there is no second path with a weaker root.

Three mechanisms hold the edges of that.

An enrolment window is opened by an administrator against an address that
administrator typed, and a `hello` is matched to a window by that address and by
nothing else. Holding the peer to the approved address was issue #22, and the two
types that decide it are in the tree:

    git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/PeerAddress.cs Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
    Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
    Jellyfin.Plugin.ServerPairing/Protocol/PeerAddress.cs

The credential on the pairing plane is this plugin's own and is accepted by
nothing else, so nothing that holds a Jellyfin credential can create a pairing.
That was issue #11, what a Jellyfin API key would otherwise reach is measured in
[`threat-model.md`](threat-model.md), and a host credential offered as the
signing key is refused by a test rather than by intention:

    git grep -n "public void AHostCredentialUsedAsTheSigningKeyIsRefused" origin/master -- Jellyfin.Plugin.ServerPairing.Tests
    origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/PairingCredentialTests.cs:128:    public void AHostCredentialUsedAsTheSigningKeyIsRefused(string encoding)

A pairing that both administrators built can be ended by either of them alone.
Revocation is unilateral, immediate and terminal, which is issue #24 and is the
one of the three still open. The transition into `Revoked` is in the state
machine, and nothing destroys a key. THIS SENTENCE SAID THAT WAS BECAUSE THERE IS
NO KEY STORE TO DESTROY ONE FROM AND NAMED ISSUE #30 FOR IT. That store is in the
tree and that issue is closed. What is true instead is that nothing on any path
calls the destruction the store offers, and nothing puts a key there for it to
destroy:

    git grep -nE '\.Put\(|\.Destroy\(' -- Jellyfin.Plugin.ServerPairing/Protocol/ Jellyfin.Plugin.ServerPairing/Api/ ; echo "exit=$?"
    exit=1

Empty output, exit one, over the protocol types and the plane. Composing the
revocation with that store is issue #24.

None of the three reaches a request, and that is what this disclosure is about
rather than which types exist. This paragraph used to say that all three were a
specification and the issues that owed the code. Two of those issues have closed
with types the suite exercises, so the attribution moved and the position did
not. This paragraph said nothing in this plugin answered a peer, and that has stopped
being true:

    git grep -l "ControllerBase" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs
    exit=0

What has not changed is what the sentence was for, and the reason it holds is
narrower than it was. THIS PARAGRAPH SAID EVERY ANSWER IS THE SAME REFUSAL BECAUSE
THE KEY SOURCE THE PLANE IS GIVEN HOLDS NO KEYS. The source reads the key store
now, which is issue #287:

    git grep -n 'new StoreBackedKeys' origin/master -- Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs
    origin/master:Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:68:            new StoreBackedKeys(services.GetRequiredService<IPairingKeyStore>()));

What no route does is put a key in that store. There is no enrolment, so nothing
generates a long-term key pair for one:

    git grep -lni 'ECDiffieHellman' origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    exit=1

which is issue #18. A server's store is therefore empty, and every arriving request
is refused for want of a key rather than for want of a lookup. So no window is
consulted, no credential is checked and no revocation is applied on any path a
request takes. The reading of the transition table above them is a
reading of a specification for the same reason, and the types named here are
exercised by the suite rather than by a server.

## Removing what moved

Revocation deletes what came from the peer rather than stopping the transfer and
leaving it in place. That is the answer settled in issue #1, and it is why the
consumer contract requires every synced row to carry its provenance, which is
issue #57.

The deletion happens in the sync plugin that stored the rows, not here. This
plugin holds the pairing record, the key material and the mapping table; a sync
plugin holds whatever it wrote from a transfer. So an operator removing what moved
does two things, and the second one is not this plugin's to do:

Revoke the pairing on either server. That is unilateral and immediate, issue #24,
and it ends the pairing on both sides.

Let the consumer remove what it stored under that pairing, which the contract
requires it to be able to do, issue #57.

What no revocation reaches is what this server already sent to the peer. Those
bytes are on the peer's disk and nothing this plugin can send gets them back.
That is stated in [`threat-model.md`](threat-model.md) in the same words and it is
not softened here.

What disable, uninstall and reinstall leave behind is
[`lifecycle.md`](lifecycle.md), which names the file that survives an uninstall
and what deleting it costs. It is not answered by this document.

## Reporting and removing what is held about one user

The section above removes what moved through a pairing. An operator asked by one
person in a household what is held about them, and to remove it, is asking a
narrower question that crosses every pairing at once. Issue #60 owes the two
administrative operations that answer it. The report exists, on the
administrative plane in [`endpoints.md`](endpoints.md), and was built against
this section; the removal does not exist. This section states what both cover so
that the code obeys it rather than the scope being read back off the code
afterwards.

Nothing in this plugin holds a person's name. What the report covers, for one
local user, on every pairing that user is mapped on, is what the sections above
say is at rest here:

- the mapping, which is one local user identifier and one opaque peer user
  identifier
- the cached peer display name beside it, said in the output to be a cache rather
  than the peer user's name, because it is a copy that may be discarded at any
  moment, nothing is decided from it, and it is never the identity a request is
  authorised against
- the pairing each mapping belongs to, by its identifier

Counting the cache as data held about that user is the part that is easy to
leave out. A report that lists the opaque identifier and omits the readable name
beside it has left out the only field in the table that names a person.

What the removal covers, once per pairing the user is mapped on:

- the mapping and its display cache, both removed rather than marked
- the consumer event that tells a sync plugin to delete what it stored under that
  pairing for that user, which is the same direction revocation takes above and
  is settled in issue #1
- an audit entry, for the report as well as for the removal, because a report of
  what is held about a person is itself an act worth being able to find later

There is no operator choice at removal time and no confirmation offering to stop
the transfer and leave the rows in place, for the reason the section above gives.

NONE OF THE REMOVAL IS BUILT, and what holds it is the second bullet. There is no
consumer contract for the event to be raised through, which is issue #43, and a
removal that took the mapping and its cache and told no consumer would read to
the operator as total while leaving in place the rows it exists to remove. So the
removal is absent rather than half-built, and the report is the whole of what an
operator can do here today.

What only the peer operator can do is the half this plugin cannot reach, and it
is the same asymmetry as revocation, one level down from a pairing to a person. A
mapping names a user on each side. Removing it here removes this server's half
and asks the consumers on this server to delete what they wrote; what the peer
server holds about that user, including whatever this server already sent under
the mapping, stays on the peer's disk. Nothing in the specification asks a peer
to delete on behalf of a person, and no operation this issue adds gets those
bytes back. An operator answering the person in front of them is told that half
plainly rather than being left to read a completed removal as a total one.

## Changing a mapping does not move what already moved

Remapping is not revocation and does not behave like it. An administrator who
maps a local user to the wrong peer user, notices, and corrects the mapping has
changed where the next transfer goes. Everything that already arrived under the
old mapping is still on the local user it arrived on, and nothing about the
correction touches it.

That is the same asymmetry as the section above, one level down. Revocation ends
a relationship and takes this side's copy of what came through it. Changing a
mapping ends nothing, so there is no event under which a consumer could be told
to remove anything, and there is no record of the old mapping for it to remove
things by unless somebody kept one.

The consequence for an operator is the part worth stating plainly, because it is
the case where a correction reads like a repair and is not one. Data that landed
on the wrong local user stays on that user until somebody removes it by hand,
using whatever the sync plugin that wrote it offers. This plugin cannot do it and
will not pretend to: it holds the mapping and the pairing, and the rows are
somebody else's.

The rules that decide when a mapping may change at all, including that a second
mapping for either side is refused rather than replacing the first, are in
[the mapping document](mapping.md). Saying this consequence at the moment of remapping, on the page rather than
in this document, is issue #40 for the surface and issue #54 for the wording.
The wording is written and is in the tree, saying what this section says:

```
git grep -n "public const string ChangeMapping" origin/master -- Jellyfin.Plugin.ServerPairing/Wording/DestructiveWording.cs
origin/master:Jellyfin.Plugin.ServerPairing/Wording/DestructiveWording.cs:51:    public const string ChangeMapping =
```

The surface is not, and nothing renders that constant, so an operator still
meets this sentence only if they read this file.

## What this document does not do

It does not list the fields of an `exchange`, for the reason given above.

It does not state a retention period in days for anything. Nothing here expires
on a clock except the nonce store and the rotation overlap, both of which carry
their number in [`protocol.md`](protocol.md); everything else lives for the life
of the pairing and goes when the pairing does.

It is not a legal document and does not tell an operator what their obligations
are. It tells them what the software moves, which is the part only this
repository can answer.
