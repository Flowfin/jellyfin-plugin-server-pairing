# The pairing protocol

This document fixes the wire before any of it is built, so that the tests are
derived from a specification rather than from an implementation, and so that
somebody can disagree with the design without reading code.

Part of what this document describes now exists in the tree, and the part that
does not is what a reader has to be told about first. The types that hold the
state machine, the canonical form, the field limits, the freshness window with
its nonce store, the key overlap, the peer address, the enrolment window and
the version negotiation are here:

```
git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.ServerPairing/Protocol | wc -l
34
```

Nothing reaches any of them from outside this server. There is no endpoint:

```
git grep -l "ControllerBase" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
exit=1
```

and there is no key store, so no request has ever been signed by this plugin
against a key it holds. Every sentence below about the wire is therefore still a
design position rather than a measured property of something that runs, and the
sentences about the state machine and the canonical form are the specification
the landed types are checked against rather than a reading of them. Where a
milestone owes a mechanism, the issue is named at the place the mechanism is
described. No later edit of this file turns any of it into a statement that
something has been checked.

It is written under the answers in issue #1, and the four that shape it most are
worth stating before the tables. Enrolment is static key pairs with a fingerprint
two operators compare, so no secret is ever transcribed. The pairing is
symmetric, so the two servers hold the same rights and there is no initiator role
on the wire. The transport is TLS with the peer certificate pinned, with an
opt-out that is a setting rather than a second design. There is no unattended
mode, so a pairing exists only where two people acted.

## What this document does not decide

The endpoint table and the authorization each endpoint requires is issue #27 and
lives in [`endpoints.md`](endpoints.md). What is here is the message layer that
those endpoints carry.

The interface a sync plugin codes against is an in-process .NET one, which is
issue #43, and it is not this wire. A consumer never sees a message described
below. The payloads of the `exchange` message are defined by that contract in M6,
and this document defines only the envelope they travel in.

Where this document names a digest, a curve or a length, it does so because a
wire cannot be described without them. [`crypto.md`](crypto.md) is where those
choices are argued and is the authority for every one of them. A value that
appears in both is a value this document has to move when that one does, and
there are three: the curve, the digest and the nonce length. Everything else
cryptographic is cited rather than restated.

## Vocabulary

A **pairing** is the relationship between two servers. It has an identifier, a
state on each side, and key material on each side.

A **peer** is the other server in a pairing. Both servers are peers of each
other; neither is a client.

A **pairing plane request** is a request one server sends to the other under this
document. It is never a request from a browser and never a request from a
consumer.

An **enrolment window** is the bounded interval during which a server will accept
a message from a party it has not yet authenticated. Its bounds and its
fail-closed edges are in the tree, in `EnrolmentWindow`, and the numbers are
argued at each constant rather than restated here.

## Identity, and where a pairing identifier comes from

Each server holds one long term key pair. The private half is generated on that
server, never leaves it in any encoding, and lives in the key store that M4 owns.
The public half is what a peer receives.

The key agreement is `ECDiffieHellman` over NIST P-256, both from the base class
library. The public half is exchanged in its DER `SubjectPublicKeyInfo` encoding,
which is what `ExportSubjectPublicKeyInfo` produces, so the bytes two servers
hash are the bytes the framework already agrees on.

Neither side chooses the pairing identifier. It is derived from the two public
keys, so two servers that hello each other at the same moment arrive at one
identifier rather than two:

```
material = the two SubjectPublicKeyInfo encodings, sorted as byte strings in
           ascending lexicographic order, concatenated with nothing between them

pairing id  = lowercase hex of the first 16 bytes of
              SHA-256("jellyfin-server-pairing/id" || 0x00 || material)

fingerprint = derived from
              SHA-256("jellyfin-server-pairing/fingerprint" || 0x00 || material)
```

The two labels are what stops one value from being usable in the place of the
other, and the zero byte after each label is what stops a label from running into
the material it prefixes. How much of the fingerprint digest an operator compares,
and why that length defeats an attacker grinding for a second preimage, is in
[`crypto.md`](crypto.md).

The identifier is a digest of two public keys, so it is not secret and it names
no person. That is why [`logging.md`](logging.md) allows it in a log line while
allowing almost nothing else.

## The states

Eight, per pairing, per side. A state is what this server believes; the peer
holds its own and the two are not assumed to agree.

| State | What is true |
| --- | --- |
| `Absent` | No pairing record with this identifier exists here. This is the state of every identifier that was never enrolled and, after the record is deleted, of one that was |
| `Offered` | An administrator here opened an enrolment window against a peer address. This server's key pair exists; no peer key has arrived |
| `Pending` | A peer key arrived inside the window and the fingerprint is on the dashboard. Neither operator has confirmed |
| `ConfirmedHere` | The administrator on this server compared and confirmed. The peer has not confirmed, or its confirmation has not arrived |
| `ConfirmedByPeer` | The peer's confirmation arrived and verified. The administrator on this server has not confirmed |
| `Active` | Both confirmations are in. An `exchange` is answered here and in `Rotating`, and nowhere else |
| `Rotating` | Active, and a key rotation is inside its overlap window, so two of this side's keys verify. The overlap is fixed below |
| `Revoked` | Terminal. Nothing moves out of it, and the record is kept rather than deleted so that a later request naming this identifier is refused rather than treated as new. Issue #24 owns revocation |

`Revoked` being terminal is the whole of it: there is no transition out, and
re-pairing two servers means new key material and therefore a different
identifier.

## The messages

Five request types. Every one is a `POST`, every one carries the authentication
headers below, and every one has exactly one response shape on success and the
refusal shape on failure.

| Type | Path | Body | Response body |
| --- | --- | --- | --- |
| `hello` | `/ServerPairing/hello` | offered public key, supported version range, peer address this server believes it is talking to | this server's public key, the version it selected, and the pairing identifier the two keys derive |
| `confirm` | `/ServerPairing/confirm` | the fingerprint digest this side's operator compared | empty |
| `rotate` | `/ServerPairing/rotate` | the replacement public key and the instant the old one stops verifying | the replacement public key of this side |
| `revoke` | `/ServerPairing/revoke` | nothing beyond the envelope | empty |
| `exchange` | `/ServerPairing/exchange` | opaque to this layer; the consumer contract in M6 defines it | opaque to this layer |

Paths are exact. A request whose path carries a trailing slash, a query string,
or any percent-encoded byte is refused rather than normalised, because two
implementations that disagree about normalisation interoperate right up until
they do not.

### Fields and their limits

Every field is checked against its limit before the value is used, and a
violation is a refusal rather than a truncation.

| Field | Type | Limit |
| --- | --- | --- |
| pairing identifier | 32 lowercase hex characters | exactly 32, and `[0-9a-f]` only. 32 zeros on a `hello` and nowhere else |
| protocol version | unsigned decimal integer, no leading zero | at most 4 digits |
| version range | two versions, low and high, low not above high | as above, each |
| timestamp | unsigned decimal integer, seconds since the Unix epoch | at most 20 digits |
| nonce | 32 lowercase hex characters | exactly 32 |
| signature | base64, standard alphabet, padded | at most 128 characters |
| public key | base64 of the DER `SubjectPublicKeyInfo` | at most 512 characters |
| fingerprint digest | lowercase hex | exactly 64 |
| peer address | absolute `https` URI, no query, no fragment, no userinfo | at most 255 characters |
| rotation instant | a timestamp, in a `rotate` body, saying when the superseded key stops verifying | after the instant the rotation starts, and no further ahead than the overlap the rotation section below fixes |
| body, `exchange` | bytes | 1 MiB |
| body, every other type | bytes | 8 KiB |

A body larger than its limit is refused without being read past the limit, and
without being parsed at all. That ordering is deliberate: a request that fails
verification never reaches the deserialiser, which is the limit the threat model
gives adversary A2 and which issue #20 owes.

The peer address in `hello` is the address the sending server believes it is
talking to. Comparing it against the address the local administrator entered is
what holds a peer to the approved address, and that is issue #22.

### The forms a peer address may take

A list rather than a pattern. Everything outside these four forms is refused,
including forms a permissive parser would accept, because the address decides
where this server sends an authenticated request and a pattern that passes the
examples somebody tried is not a decision anybody made.

| Form | Accepted | Refused, and why |
| --- | --- | --- |
| domain name | `https://peer.example.org` | `https://peer.example.org/pairing`, a path. The plane owns its paths and appends them |
| domain name and port | `https://peer.example.org:8920` | `https://peer.example.org:0`, not a port a peer listens on |
| IPv4 literal | `https://192.0.2.10` | `https://operator@192.0.2.10`, a credential in front of the host |
| bracketed IPv6 literal | `https://[2001:db8::10]:8920` | `https://2001:db8::10`, unbracketed, which no absolute URI parse reads as that address |

A domain name is ASCII letters, digits, hyphens and the dots between labels. A
name outside that is refused rather than converted, because two spellings that
render alike and resolve differently are the mistake an operator cannot see on
the page they approve it on.

Two spellings of one address are one address: the default port, a trailing
slash and the case of the host are all removed before two addresses are
compared, so an administrator who typed `https://peer.example.org:443/` approved
the same peer as one who typed `https://peer.example.org`.

Plaintext is refused with no way round it today. Decision 3 in #1 allows an
operator acknowledgement that would permit something else, and that
acknowledgement is a setting with a safe default, a range and a refusal, which
is issue #50 and does not exist yet. Until it does, `https` is the only scheme.

Nothing on this plane follows a redirect. A `3xx` answer is refused where it
arrives rather than followed, because following one sends an authenticated
request to an address no administrator approved. Every response is read against
the limit for its message type and the read stops one byte past it, so a peer
that answers endlessly costs this server that limit and no more.

### The body members

The message table fixes what each body carries and the limits table fixes what
each value is checked against. Neither names the member a value travels under,
so two implementations built from those two tables agree about every value and
disagree about every name. The names are fixed here, and the type that
serialises them follows this document rather than the other way round: choosing
them in a C# type would choose the wire in the one place the other server cannot
read.

Every body that is not empty is a single JSON object with no nesting. Members
are the exact byte sequences below, matched case-sensitively, so `Address` is
not `address`. Member order is not significant and nothing covers it, because
line 8 of the canonical form below covers the body bytes as they were sent
rather than as they would re-serialise.

| Type | Body | Member | Value, and the limit it is already checked against |
| --- | --- | --- | --- |
| `hello` | request | `key` | public key, the one this sender offers |
| `hello` | request | `versionLow` | protocol version, the lowest the sender speaks |
| `hello` | request | `versionHigh` | protocol version, the highest the sender speaks, not below `versionLow` |
| `hello` | request | `address` | peer address, the one this sender believes it is talking to |
| `hello` | response | `key` | public key, this server's |
| `hello` | response | `version` | protocol version, the one this server selected |
| `hello` | response | `pairingId` | pairing identifier, the one the two keys derive |
| `confirm` | request | `digest` | fingerprint digest |
| `confirm` | response | none | empty |
| `rotate` | request | `key` | public key, the replacement |
| `rotate` | request | `notAfter` | rotation instant, after which the superseded key no longer verifies |
| `rotate` | response | `key` | public key, this side's replacement |
| `revoke` | request | none | empty |
| `revoke` | response | none | empty |
| `exchange` | both | none named here | opaque to this layer; M6 fixes what is inside it and this document names none of it |
| every type | refusal | `code` | one of the codes in the error taxonomy below |

The version range is two members rather than one nested object, so each half is
checked against the protocol version limit by itself and a range whose halves
are the wrong way round is one comparison rather than a parse. `hello` is the
only body carrying a range; every later message carries the selected version in
`X-Pairing-Version` and nowhere else.

`key` is one name across the three bodies that carry a public key, because no
body carries two of them, and a name that changes per message buys a reader
nothing and costs an implementer a table.

**Empty means zero bytes.** Where the table says empty the body is nothing at
all, not `{}` and not whitespace, and line 8 of the canonical form takes the
digest of the empty string for it. A request or a response carrying a body where
this table says empty is refused, so a member cannot be smuggled into a body
this document says has none and then relied on.

**Every member in the table is required in the body it belongs to.** There is no
optional member and no default. A body missing a member that its declared
version requires is refused rather than completed, because a default is a value
neither side agreed on standing in for one they would have had to send. To a
caller whose signature verified that refusal is `malformed`; to one whose
signature did not, it is `refused` like everything else, which is the taxonomy
below rather than a rule of its own.

**A member this document does not name is refused rather than ignored**, in
those words. Ignoring an unknown member is what turns it into an undocumented
extension: two implementations begin relying on it and the version that was
supposed to announce it never moved. A member carrying `null`, and a body
carrying the same member twice, are refused the same way and for the same
reason, rather than being resolved by a rule about which copy wins.

**The members are part of the protocol version.** Adding a member, renaming one,
removing one, or changing what one holds is a change to the bytes under a
version, which [`versioning.md`](versioning.md) already calls a removal wearing
a smaller number: give it a new protocol version and keep the old one accepted,
or accept that it moves the first part of the plugin version. Each of those
carries a `[protocol]` line in [`../CHANGELOG.md`](../CHANGELOG.md).

## What is authenticated, and over exactly which bytes

Every pairing plane request carries five headers. They are custom headers rather
than `Authorization`, because the host reads `Authorization` for its own token
and a credential of this plugin's in that header would be handed to the server's
own authentication before this plugin saw it.

```
X-Pairing-Id
X-Pairing-Version
X-Pairing-Timestamp
X-Pairing-Nonce
X-Pairing-Signature
```

No credential of this plugin's appears in a query string, on either supported
server line. The reason is in [`endpoints.md`](endpoints.md): a query string is
written to access logs, proxy logs, browser history and referrer headers, and one
of the seven token routes the host accepts is a query string on both lines.

The signature covers a canonical byte string built here rather than the request
as it appears on the wire. Header case, header order, header folding and
whitespace are all things a proxy is allowed to change, so none of them is
covered; the values are covered, as written into the lines below.

The canonical byte string for a request is exactly eight lines. Every line is
US-ASCII, every line ends with one `0x0A`, including the last, and no line
contains `0x0D`:

```
1  jellyfin-server-pairing/request
2  <protocol version, the value in X-Pairing-Version>
3  <request method, uppercase>
4  <request path, exactly as sent, with no query string>
5  <pairing identifier>
6  <timestamp, the value in X-Pairing-Timestamp>
7  <nonce, the value in X-Pairing-Nonce>
8  <lowercase hex SHA-256 of the request body bytes>
```

Line 1 separates this signature from every other use of the same key, so a
signature produced here cannot be replayed into another construction. Line 2
binds the version, so a downgrade cannot happen silently underneath a valid
signature. Line 8 covers the body by digest rather than by inclusion, so the
signed material has a fixed length whatever the body is, and the digest of the
empty string is used where there is no body.

A response carries `X-Pairing-Timestamp`, `X-Pairing-Nonce` and
`X-Pairing-Signature`, over six lines:

```
1  jellyfin-server-pairing/response
2  <protocol version>
3  <pairing identifier>
4  <the nonce of the request being answered>
5  <timestamp, the value in X-Pairing-Timestamp on the response>
6  <lowercase hex SHA-256 of the response body bytes>
```

Line 4 is what binds a response to its request, so a captured response cannot be
replayed against a different one.

So which fields are authenticated has a short answer in both directions. Every
field of every body is, because the body is covered whole by its digest and a
single changed byte moves it. The five header values are, because each is written
into a line of its own. Nothing else is: not a header this document does not
name, not the query string, which is refused rather than covered, and not the
order or casing of anything.

The algorithm over these bytes, and which of the two per-direction keys signs
them, are pinned in [`crypto.md`](crypto.md). What this document fixes is the
bytes.

`hello` is the one message that cannot carry a pairing identifier, because the
identifier is derived from both public keys and the sender holds only one of
them. Its `X-Pairing-Id` is 32 `0` characters, which is what line 5 of its
canonical form holds, and the receiver returns the derived identifier in the
response body. Every later message carries the real one.

A `hello` is therefore matched to an enrolment window by the peer address the
local administrator entered, and by nothing else. It is signed with the private
half of the key it offers, over the same canonical form, so it proves possession
of that key and nothing else. That is the whole of what it is allowed to prove:
the comparison the two operators perform is what turns a key into an identity.

## Freshness

A request is fresh when its timestamp is within 300 seconds of this server's
clock in either direction, and its nonce has not been seen for this pairing
inside that window.

The nonce is 16 random bytes from `RandomNumberGenerator`, written as 32
lowercase hex characters. It is not a counter and carries no meaning; two
requests that differ in nothing else must differ here.

A nonce is remembered for 600 seconds, which is the window taken in both
directions, and is the widest gap there can be between the first arrival of a
request and the last instant a copy of it would still be inside the window. A
nonce older than that is dropped, so the store is
bounded by the request rate rather than by uptime. The store is per pairing and
is not persisted: a restart forgets it, and a request replayed across a restart
inside 300 seconds is accepted. That is a real gap, it is named rather than left
out, and issue #21 is where it is either closed or accepted with a reason.

The store is bounded by count as well as by age, and this document said only the
second until now. A pairing may hold 4096 remembered nonces at once, and a fresh
request arriving with no room left is refused rather than remembered, because
dropping a nonce that is still inside the window is the replay the store exists
to refuse. All three numbers are constants of the landed type, and the two this
paragraph adds are read out of it rather than asserted beside it:

```
git grep -n "const int RememberedSeconds\|const int NoncesPerPairing" -- Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs
Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs:43:    public const int RememberedSeconds = 600;
Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs:55:    public const int NoncesPerPairing = 4096;
```

What that refusal says on the wire is the taxonomy below. What the count should
be is issue #21's along with the restart question, and it is not settled here.

Both numbers are constants of the specification rather than secrets, so a caller
learns nothing by discovering them that reading this document would not have
told them. What makes them testable is already here: every judgement takes the
instant as an argument, so a case chooses the moment rather than waiting for it,
and nothing is injected. Issue #26 owns the skew policy the refusal below rests
on.

## The rotation overlap

A key that never changes is a key that is eventually copied, so a pairing can
replace its keys without being re-enrolled. `rotate` carries the replacement
public key and the instant the superseded key stops verifying, and between those
two instants both keys verify what arrives. That interval is the overlap.

It exists because the two servers are not online at the same moment. A side that
rotated cannot know whether the peer received the replacement, so refusing the
superseded key at the moment of the rotation would end the traffic exactly where
rotation is supposed to preserve it. During the overlap a request that verified
under the superseded key is accepted and recorded as such, which is the rotation
row of [`logging.md`](logging.md) and is how an operator sees a peer that has not
caught up.

The overlap is at most 86400 seconds. The case it exists for is a home server
switched off overnight that comes back the next morning; past a day the
superseded key has stopped being a grace period and has become a second live key
nobody is watching, which is the thing rotation removes. A `rotate` asking for
longer, for zero, or for an instant already past is refused and the pairing stays
on the key it was already using. It is refused rather than shortened, so a side
that asked for more finds out instead of being quietly given less.

The superseded key stops verifying at the earlier of two things. The overlap
running out is one. The other is the peer proving it holds the replacement, which
it does by sending a request that verifies under it, and at that point the
superseded key has nothing left to be for.

A rotation that does not complete fails the rotation and never the pairing. Every
refusal above leaves both keys where they were, and a rotation given up halfway
puts both sides back on the superseded key, which is the only key both are known
to hold. There is no path that removes a key without leaving one in place.

Two keys per pairing is the ceiling. A second `rotate` arriving while an overlap
is open is `state`, because accepting it would drop the key the offline peer is
still using and would spend the overlap on a peer that never heard about the
first replacement.

The side that rotates signs with the replacement from the moment it accepts it. A
side that went on signing with the superseded key would be unverifiable to a peer
that had already caught up, which is the same outage pointed the other way.

Rotation moves no pairing into a state it was not already in. A `rotate` is
answered in `Active` and nowhere else, so a key rotated on a pairing that answers
nothing goes on reaching nothing.

What this section does not fix. Whether a rotation is scheduled or started by an
operator, since the mechanism is the same either way. Where the replacement key
comes from and what holds it, which is the key store in M4. What is logged at
each end of the overlap, which is [`logging.md`](logging.md).

## The transition table

Rows are the state on the receiving side. Columns are the request that arrives.
Every cell is defined, and a cell reading `refused` is a refusal with the
undistinguished code, not undefined behaviour.

| State | `hello` | `confirm` | `rotate` | `revoke` | `exchange` |
| --- | --- | --- | --- | --- | --- |
| `Absent` | `refused` | `refused` | `refused` | `refused` | `refused` |
| `Offered` | record the peer key, answer with this side's key, go to `Pending` | `refused` | `refused` | `refused` | `refused` |
| `Pending` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | go to `ConfirmedByPeer` | `state` | go to `Revoked` | `state` |
| `ConfirmedHere` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | go to `Active` | `state` | go to `Revoked` | `state` |
| `ConfirmedByPeer` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | stay, answer as before | `state` | go to `Revoked` | `state` |
| `Active` | `refused` | stay, answer as before | accept the replacement key, go to `Rotating` | go to `Revoked` | answer it |
| `Rotating` | `refused` | stay, answer as before | `state` | go to `Revoked` | answer it |
| `Revoked` | `refused` | `refused` | `refused` | `refused` | `refused` |

Four things in that table are worth saying in words, because a reader can find
them in it but should not have to.

A second `hello` carrying a different key closes the window and destroys the
half-built pairing. That is the single-use half of issue #18, and it fails closed:
the operator starts again rather than choosing between two keys.

A repeated `confirm` and a repeated `hello` with the identical key are answered
as before rather than refused, because the network drops responses and a peer
that retries is not an attacker. A repeated `revoke` against `Revoked` is
`refused` rather than answered, because `Revoked` answers nothing at all.

`revoke` is accepted in every state where this side knows the peer's key,
including the half-built ones from `Pending` onwards. Revocation is unilateral,
which means it does not need the other side's cooperation and does not need the
pairing to have finished.

`Offered` is the exception and the table's row for it says so. No peer key has
arrived there, so an arriving `revoke` carries no signature this side could
verify, and accepting one would let anyone who can reach the endpoint end an
enrolment they know nothing about. An administrator on this side ending it is the
local event below and is not affected.

`exchange` is answered in `Active` and `Rotating` and nowhere else. In the
half-built states it is `state` rather than `refused`, because a caller that
reaches those states has already verified, and telling a verified peer that the
pairing is not finished tells it nothing it could not infer.

## Local events

The table above covers what arrives. Five things happen on this side and move a
pairing without any message arriving.

| Event | From | To |
| --- | --- | --- |
| An administrator opens a window against a peer address | `Absent` | `Offered` |
| An administrator confirms the fingerprint | `Pending` | `ConfirmedHere` |
| An administrator confirms the fingerprint | `ConfirmedByPeer` | `Active` |
| The enrolment window expires | `Offered`, `Pending`, `ConfirmedHere`, `ConfirmedByPeer` | `Absent` |
| An administrator revokes | `Offered`, `Pending`, `ConfirmedHere`, `ConfirmedByPeer`, `Active`, `Rotating` | `Revoked` |
| The rotation overlap closes | `Rotating` | `Active` |

An administrator confirming sends a `confirm` to the peer. A failure to deliver
it does not move this side back: the peer's own retry, or the operator's, is what
completes the pairing, and the window expiring is what ends it if neither does.

An administrator revoking sends a `revoke` to the peer and moves to `Revoked`
whether or not that delivery succeeds. Revocation that waits for the peer is
revocation an unreachable peer can refuse.

## The error taxonomy

Every refusal on the pairing plane is HTTP 403 with a body of exactly one JSON
object with exactly one member:

```
{"code":"refused"}
```

The shape never varies. Only the code varies, and it varies only for a caller
that has already proved it holds the key, or that is inside a window an
administrator opened deliberately.

| Code | What caused it | Who can ever see it | Distinguishable from its neighbours |
| --- | --- | --- | --- |
| `refused` | An unknown pairing identifier, a signature that does not verify, a body over its limit, a malformed header value, a request in a state that does not accept it from an unverified caller, a revoked pairing, a request when no pairing exists, a second `hello` with a different key, a fault on this side while serving the request | anyone | No, and deliberately so. Every one of those causes produces the same bytes |
| `clock` | The signature verified and the timestamp is outside the freshness window | only a caller holding a verifying key | Yes |
| `version` | No version in common | a caller inside an open enrolment window, or a caller holding a verifying key | Yes, to those callers only |
| `state` | The signature verified and the message is not accepted in this state | only a caller holding a verifying key | Yes |
| `malformed` | The signature verified and the body does not parse, or a field is outside its limit | only a caller holding a verifying key | Yes |
| `replay` | The signature verified, the timestamp is inside the window, and this nonce has already been seen for this pairing | only a caller holding a verifying key | Yes |
| `busy` | The signature verified, the request is fresh, and this pairing has no room left to remember another nonce | only a caller holding a verifying key | Yes |

The single `refused` code is what makes probing useless. A caller naming a
pairing that does not exist learns the same as one naming a pairing that does and
signing badly, which is what stops an unauthenticated caller from learning
whether this plugin is installed or whether it has ever been paired. That is the
general position issue #28 owns.

One distinction is kept on purpose and it is worth arguing rather than assuming.
`clock` is reported instead of being folded into `refused`, which hands a caller
one bit: whether their timestamp was inside this server's window. The bit is
worth giving away. The window is a constant of this document rather than a
secret, so the same bit is available by reading. And the alternative costs an
operator an evening debugging a signature error that is really a clock error, on
two home servers one of which has no time source. The bit is only ever handed to
a caller that already holds the key, because verification runs first.

`replay` and `busy` are the last two rows of the table and the newest, and they
were reached by the landed freshness window before this table had a code for
either. Both are only ever seen by a caller that already verified, for the same
reason `clock` is, and both hand over one bit this document states in the
freshness section above rather than keeps: how long a nonce is remembered, and
how many one pairing may hold at once.

`replay` says the nonce arrived twice. Folding it into `refused` is defensible,
because a caller replaying a request it captured learns nothing it did not
already have. It is not taken, because this is the refusal an operator most needs
told apart from a bad signature. A peer that resends has a retry loop or
something recording between the two servers, and neither of those reads as a key
problem to the person who has to find it.

`busy` is the refusal that is not about the request. Everything in it was right,
and this server refused it because a bound of its own was reached. Under one
undistinguished code the peer is told what a stranger is told and the operator is
told nothing, so a pairing that works stops working for a cause neither side can
see, which is the failure the whole list is written against. `busy` says the
bound was reached here. It is also the only code a caller can act on: the same
request is accepted once the remembered span rolls, and sending it again
immediately is not.

The per-pairing rate limit issue #28 owes is a separate mechanism from that
bound, and whether the two become one limit or two is that issue's to settle. Two
independent limits on one plane sharing one code between them is the state to
avoid, and naming the bound here is what makes the choice visible when the limit
is built.

`version` is the one code a caller can see without holding a key, and only
because an enrolment window is open, which is a door an administrator opened on
purpose and which closes on a timer.

The last cause in the `refused` row is the one that is not about the request at
all, and it is in that row for the reason the row exists. A fault on this side is
a bug, a full disk or a body stream that went away, and none of that is the
caller's business; more to the point, a caller that can make one path fault while
another path refuses has separated two paths, whatever the fault was. So a fault
is caught before it leaves the plugin and is answered with the same status, the
same media type and the same bytes as everything else in that row. What a
framework produces for an escaping exception is none of those: a different
status, usually a different media type, and on a server with the developer page
turned on, a stack trace naming this plugin's types.

The detail goes to the log instead, at Error, where an operator can read it and a
stranger cannot. [`logging.md`](logging.md) carries that entry and says why it is
the one entry in its table with no pairing identifier on it.

A caller that hung up is not a fault and is not answered at all. There is nobody
left to receive an answer, and an error line for every disconnect fills a log
with the network rather than with this plugin.

The timing of a refusal is one class. Every cause above is answered after the
same work, so a caller cannot separate them by measurement where the codes are
identical. Nothing in the tree enforces that today and issue #28 owes the test.
A fault is the one cause that plainly does not take the same time as the others,
and no reading of a tree fixes that: it is answered after however far the request
got before it failed. The one-shape refusal bounds what a caller reads, not how
long it waited for it.

Two refusals a landed type already produces have no code in the table above, and
saying so is what stops the table being read as the whole set. `KeyOverlap.Rotate`
returns five values, two of which are refusals this document has never named:

```
git grep -nE "^    [A-Za-z]+ = [0-9]+" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs:17:    Rotated = 0,
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs:24:    AlreadyRotating = 1,
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs:30:    OutsideTheMaximum = 2,
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs:35:    Malformed = 3,
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RotationOutcome.cs:41:    NotAReplacement = 4,
```

Three of the five are placed. `Rotated` is the answer, `AlreadyRotating` is the
`state` the rotation section above gives a second `rotate` inside an open overlap,
and `Malformed` is a replacement whose length is not the key length, which is a
field outside its limit and therefore `malformed`. The
other two are not. `OutsideTheMaximum` is an overlap of zero, a negative one, or
one longer than the bound; `NotAReplacement` is a `rotate` offering the key the
pairing is already on. Both are reached only after the signature verified, which
is the position every code other than `refused` exists for, and the rotation
section calls both of them refused in the ordinary sense of the word rather than
by giving either a code.

Which code each takes is not settled here, and folding them into `refused` is not
the default answer just because nothing else is written. Issue #28 owns the
taxonomy and is where `replay` and `busy` were separated from `refused` for the
same reason: a peer that is told what a stranger is told, about a cause the
operator cannot see either, is a pairing that stops working with nobody able to
say why.

## Versions

`X-Pairing-Version` carries one unsigned integer. Version 1 is this document.

`hello` carries a range: the lowest and highest versions the sender speaks. The
receiver selects the highest version inside both ranges and answers with it, and
that selected version is fixed for the life of the pairing. It appears on every
later request, and line 2 of the canonical form binds it, so neither side can
move it without the other noticing.

Where the ranges do not overlap, the answer is `version`, and to a caller that is
neither inside an open window nor holding a key it is `refused` like everything
else.

A request on an `Active` pairing carrying a version other than the selected one
is `state`. A pairing is not renegotiated; two servers that want a different
version rotate or re-pair.

A version this server does not know is not a version it guesses at. There is no
best-effort parse of an unknown version and no forward compatibility rule beyond
the range, because a message a server does not understand is one it cannot make a
security decision about.

## What is not decided here

The endpoint authorization table, issue #27.

The bounds of the enrolment window. This document names the state it puts the
pairing in and what closes it; how long it lasts, how many failures it takes and
the longest lifetime a caller may ask for are constants on `EnrolmentWindow`:

```
git grep -nE "public const int (LifetimeSeconds|MaximumLifetimeSeconds|FailuresAllowed)" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:47:    public const int LifetimeSeconds = 600;
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:58:    public const int MaximumLifetimeSeconds = 1800;
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:69:    public const int FailuresAllowed = 3;
```

Making the first of those a setting rather than a constant is issue #18.

The cryptographic parameters. [`crypto.md`](crypto.md) holds them, and the three
this document repeats are named at the top.

The `exchange` payloads, M6.

Whether the nonce store survives a restart, issue #21, which is named above as an
accepted gap rather than left silent.
