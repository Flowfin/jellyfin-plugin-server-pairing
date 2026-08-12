# What pairing moves, where it comes to rest, and how to remove it

Pairing moves user mappings between two servers, and a user mapping is personal
data. An operator should not have to work that out from the source, so it is
written here.

This is a statement about the design in [`protocol.md`](protocol.md), not about
something running. There is no pairing, no key store and no endpoint in this
tree, so nothing below has ever moved between two servers. Every field named here
is a field the specification defines, and the milestone that owes the code is
named where it matters. No later edit of this file turns any of it into a
statement that a transfer was observed.

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
| `X-Pairing-Nonce` | 16 random bytes, hex | No | In the nonce store for 600 seconds, in memory, per pairing |
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
| replacement public key | As the offered public key above | No | As the offered public key above. The superseded key is dropped when the overlap closes, which is issue #23 |
| the instant the old key stops verifying | A timestamp | No | In the pairing record until the overlap closes |

### In `revoke`

Nothing beyond the envelope crosses. That is the point of it.

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
either server as truth. Issue #41 owes the record's shape and issue #36 owes the
table being an administrator's decision.

A display name may cross so that an administrator can read the mapping page, and
it is a cache. It may be discarded at any moment, nothing is decided from it, and
it is never the identity a request is authorised against.

Until `exchange` has its fields, this document describes the envelope of a
transfer and not its contents. That is a real gap in a personal-data statement,
it is named here rather than papered over, and it closes when M6 lands.

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

`Active` is the only state in which an `exchange` is answered. Its two entries
are from `ConfirmedHere` on a peer's `confirm`, and from `ConfirmedByPeer` on the
local administrator confirming. So reaching `Active` requires the local
administrator's confirmation in both cases, and by symmetry it requires the far
administrator's on the other side. There is no unattended mode, which is settled
in issue #1, so there is no second path with a weaker root.

Three mechanisms hold the edges of that.

An enrolment window is opened by an administrator against an address that
administrator typed, and a `hello` is matched to a window by that address and by
nothing else. Holding the peer to the approved address is issue #22.

The credential on the pairing plane is this plugin's own and is accepted by
nothing else, so nothing that holds a Jellyfin credential can create a pairing.
That is issue #11, and what a Jellyfin API key would otherwise reach is measured
in [`threat-model.md`](threat-model.md).

A pairing that both administrators built can be ended by either of them alone.
Revocation is unilateral, immediate and terminal, which is issue #24.

None of those five is enforced by anything in this tree today. The mechanism is a
specification and the issues that owe the code, and this paragraph is the whole of
that disclosure.

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

What disable, uninstall and reinstall leave behind is issue #58, and reporting
and removing what is held about one user is issue #60. Both are M8, and neither
is answered by this document.

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
mapping for either side is refused rather than replacing the first, are issue
#37. Saying this consequence at the moment of remapping, on the page rather than
in this document, is issue #40 for the surface and issue #54 for the wording.
Neither exists, and until they do an operator meets this sentence only if they
read this file.

## What this document does not do

It does not list the fields of an `exchange`, for the reason given above.

It does not state a retention period in days for anything. Nothing here expires
on a clock except the nonce store and the rotation overlap, both of which carry
their number in [`protocol.md`](protocol.md); everything else lives for the life
of the pairing and goes when the pairing does.

It is not a legal document and does not tell an operator what their obligations
are. It tells them what the software moves, which is the part only this
repository can answer.
