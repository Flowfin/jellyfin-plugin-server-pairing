# The pairing protocol

This document fixes the wire before any of it is built, so that the tests are
derived from a specification rather than from an implementation, and so that
somebody can disagree with the design without reading code.

Part of what this document describes now exists in the tree, and the part that
does not is what a reader has to be told about first. The types that hold the
state machine, the canonical form, the field limits, the freshness window with
its nonce store, the key overlap, the peer address, the enrolment window, the
version negotiation, the store a pairing record is kept in and the revocation
that stops a pairing on this server are here:

```
git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.ServerPairing/Protocol | wc -l
44
```

**This paragraph said nothing reached any of them from outside this server, that
there was no endpoint, and that there was no key store. All three have stopped
being true and the sentence they supported has not.** Something reaches them:

```
git grep -l "ControllerBase" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs
origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs
exit=0
```

and there is a key store:

```
git ls-tree -r --name-only origin/master -- Jellyfin.Plugin.ServerPairing/KeyStore | wc -l
14
```

**A fourth has stopped being true since, and the sentence still stands.** This
paragraph said no request has ever been verified against a key this plugin holds
because the key source the plane is given holds none. The source reads the store
now, which is issue #287:

```
git grep -n 'new StoreBackedKeys' origin/master -- Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs
origin/master:Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:72:            new StoreBackedKeys(services.GetRequiredService<IPairingKeyStore>()));
```

What makes the sentence still right is one step further back. No route puts a key
into that store, because there is no enrolment and nothing generates the long-term
key pair one would need:

```
git grep -lni 'ECDiffieHellman' origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
exit=1
```

which is issue #19. So the six paths are served and every one of them refuses, for
want of a key rather than for want of a lookup, and nothing has ever
been signed by this plugin against a key it holds or accepted from a peer.

THIS SENTENCE NAMED ISSUE #18 AND THAT NUMBER IS THE ONE PART OF AN ENROLMENT
THAT HAS LANDED. An enrolment here is three things: the window an administrator
opens, the join that writes a record when it opens, and the ceremony that derives
a key pair, exchanges public keys and puts the fingerprint in front of two
operators. The first two are in the tree.

```
git grep -ln 'class EnrolmentWindow\|class Enrolment' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/Enrolment.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
```

The third is not, which the reading above says, and it is issue #19. Every other
place in this repository that gave #18 as the reason a server holds no key names
#19 now, for the same reason: a reader who follows a pointer to work that is
built concludes the absence is gone.
Everything below about what happens after a signature verifies is therefore still
a design position rather than a measured property of something that runs, and the
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

Six request types. Every one is a `POST`, every one carries the authentication
headers below, and every one has exactly one response shape on success and the
refusal shape on failure.

| Type | Path | Body | Response body |
| --- | --- | --- | --- |
| `hello` | `/ServerPairing/hello` | offered public key, supported version range, peer address this server believes it is talking to | this server's public key, the version it selected, and the pairing identifier the two keys derive |
| `confirm` | `/ServerPairing/confirm` | the fingerprint digest this side's operator compared | empty |
| `rotate` | `/ServerPairing/rotate` | the replacement public key and the instant the old one stops verifying | the replacement public key of this side |
| `revoke` | `/ServerPairing/revoke` | nothing beyond the envelope | empty |
| `unpair` | `/ServerPairing/unpair` | nothing beyond the envelope | empty |
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
gives adversary A2. THIS SENTENCE SAID ISSUE #20 OWED IT AND THAT ISSUE IS CLOSED:
the ordering is in the request authenticator it landed, which reads a body only
after a signature verified, and the plane refuses an oversized body before either:

    git grep -n 'public VerificationOutcome VerifyThenRead' -- Jellyfin.Plugin.ServerPairing/Protocol/RequestAuthenticator.cs
    git grep -n 'arrived.BodyExceededItsLimit' -- Jellyfin.Plugin.ServerPairing/Api/PeerPlane.cs

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

Plaintext is refused unless the operator has acknowledged what it costs. Decision
3 in #1 allows that acknowledgement, and it is a setting with a safe default, a
range and a refusal, which is `AcknowledgeCleartextTransport` in
[`docs/configuration.md`](configuration.md). Its safe value is off, and off is
also what a configuration file that never mentions it produces, so `https` is the
only scheme on a server nobody has changed. Where it is on, `http` is accepted as
well and nothing else is: the acknowledgement widens the scheme rule and leaves
every other rule above exactly where it was.

    git grep -n 'CleartextScheme' -- Jellyfin.Plugin.ServerPairing/Protocol/PeerAddress.cs

What an operator gives up by setting it is that request and response bodies, the
mapping table among them, are readable by anything on the path between the two
servers, and the statement that a passive listener gets nothing falls away with
no replacement. The plugin writes that sentence to the log at Warning on every
start where the setting is on.

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
| `unpair` | request | none | empty |
| `unpair` | response | none | empty |
| `exchange` | both | none named here | opaque to this layer; M6 fixes what is inside it and this document names none of it |
| every type | refusal | `code` | one of the codes in the error taxonomy below |
| every type | refusal, `version` only | `versionLow` | protocol version, the lowest this server speaks |
| every type | refusal, `version` only | `versionHigh` | protocol version, the highest this server speaks |

**EACH MEMBER'S JSON TYPE IS FIXED HERE TOO, AND THIS SECTION LEFT IT OPEN.** The
table above named the members and the limits table said what each value is, and
between them a reader could still build `versionLow` as `1` or as `"1"` and be
following both. Two implementations agreeing about every value and disagreeing
about its type interoperate exactly as badly as two disagreeing about a name, so
the type is written down rather than discovered:

| Member | JSON type |
| --- | --- |
| `versionLow`, `versionHigh` | number |
| `key`, `address`, `digest`, `code` | string |

A protocol version is a number and everything else is a string. The two version
members are numbers because the one refusal body that carries a range already
writes them as numbers, in the error taxonomy below, and that section says a peer
reads one spelling of a version range rather than two - which it would not, if a
request wrote the same two members as strings. Everything else is a string because
its limit is a length or an alphabet rather than a magnitude.

A member of the wrong type is refused rather than converted. There is no coercion
rule here, and asking for one is asking which of two implementations is right about
a value neither of them can check.

`notAfter` is the one member the table above names that this list does not, and it
is left open deliberately: it is a rotation instant whose bound is the rotation
section rather than the limits table, and no reader in this tree parses one. It is
fixed when a `rotate` body is read.

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

**WHAT EXECUTES THIS SECTION, AND WHICH TWO ROWS IT DOES NOT REACH.** Every rule
above is read by one type, which is handed the bytes of a request that has already
verified:

```
git grep -n 'public static ArrivingBody Read' -- Jellyfin.Plugin.ServerPairing/Protocol/ArrivingBody.cs
```

It reads a `hello` request, a `confirm` request, and the two rows that say empty. A
`rotate` request body has a row here and no reader, so nothing in this tree refuses
one that breaks these rules; an `exchange` body is opaque to this layer by the row
above and is not read here by design rather than by omission. No response row has a
reader at all: `PeerChannel` reads a peer's answer into bytes against its limit and
hands them on, and nothing turns those bytes into the members this table names for
a response.

The refusal a body earns is `malformed`, and only a caller whose signature verified
is ever told it, which is the error taxonomy below rather than a rule of this
section.

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

A request is fresh when its timestamp is within the tolerated skew of this
server's clock in either direction, and its nonce has not been seen for this
pairing inside that window.

The skew is the operator's, because two home servers disagree by seconds without
anything being wrong and by minutes when one of them has no time source. It is 300
seconds where nobody has chosen, at most 900, and it is
`TimestampWindowSeconds` in [`docs/configuration.md`](configuration.md). Neither
number is a secret: a caller learns nothing by discovering what a server accepts
that this paragraph has not already told them. What a second added to it buys is a
second in which a captured request is still worth sending.

The nonce is 16 random bytes from `RandomNumberGenerator`, written as 32
lowercase hex characters. It is not a counter and carries no meaning; two
requests that differ in nothing else must differ here.

A nonce is remembered for the window taken in both directions, which is the widest
gap there can be between the first arrival of a request and the last instant a
copy of it would still be inside the window. It is derived from the skew rather
than set beside it, so the two cannot be put into a state where a nonce ages out
while a request carrying it would still be accepted. A nonce older than that is
dropped, so the store is bounded by the request rate rather than by uptime. The
store is per pairing and is not persisted: a restart forgets it, and a request
replayed across a restart inside the skew is accepted.

**That gap is accepted rather than owed, and the reason is here rather than in a
tracker.** THIS PARAGRAPH SAID THE GAP WAS NAMED AND LEFT TO ISSUE #21 TO CLOSE
OR ACCEPT. It is accepted, on #21, and what a reader of the freshness rule needs
is the reason at the rule rather than a pointer away from it. Persisting a nonce
means a disk write on every request that verifies, at a rate the peer sets, so a
peer that can make this server write to disk as fast as it can send is a worse
property than the one being bought. Narrowing the window under the time a restart
takes is the other way out and it breaks the drift tolerance the window exists
for. So the window stays, the store stays in memory, and this is the whole of what
is lost: **a request captured and presented again within the freshness window,
across a restart of this server, is not detected as a replay.** It is a bounded
window rather than an open one, and the bound is the same number the paragraph
above gives.

What must not happen is this document saying the store is persisted while it is
not. The mismatch is the defect and the gap is not, which is why the sentence
above is written as plainly as it is and stays that way through every later edit.

The store is bounded by count as well as by age, and this document said only the
second until the count was added to it. A pairing may hold 4096 remembered nonces
at once, and a fresh request arriving with no room left is refused rather than
remembered, because dropping a nonce that is still inside the window is the replay
the store exists to refuse. The count is a constant of the landed type and the two
spans are not, so what is read out of the type is the count and the bound on the
skew:

```
git grep -n "const int NoncesPerPairing\|const int MaximumWindowSeconds" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs:61:    public const int MaximumWindowSeconds = 900;
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/FreshnessWindow.cs:73:    public const int NoncesPerPairing = 4096;
```

What that refusal says on the wire is the taxonomy below. THIS SENTENCE SAID THE
COUNT WAS ISSUE #21'S ALONG WITH THE RESTART QUESTION AND THAT NEITHER WAS
SETTLED HERE. The restart question is settled above. The count was never that
issue's: its done conditions ask that the store be bounded on entries and that
nothing inside the window be forgotten early, which 4096 satisfies whatever the
number is, and no condition anywhere asks what the number should be. So the count
is a constant of this specification, read out of the type above, and what is not
known about it is a measurement rather than a decision: no run on any machine has
said what request rate 4096 covers before a fresh request meets a full store, and
nothing here claims one.

Both numbers are constants of the specification rather than secrets, so a caller
learns nothing by discovering them that reading this document would not have
told them. What makes them testable is already here: every judgement takes the
instant as an argument, so a case chooses the moment rather than waiting for it,
and nothing is injected.

THIS PARAGRAPH ENDED BY SAYING ISSUE #26 OWNS THE SKEW POLICY THE REFUSAL BELOW
RESTS ON. It owns it and has taken it. The plane consults a window: a request is
judged for freshness once its signature has verified, and the three answers below
are what a caller holding the key is told. The order is the security property and
is stated where the taxonomy is, not here.

## The arrival limit

A request that survives the shape checks costs this server a signature
computation. That is the work a stranger can ask for by sending anything at all,
so the plane bounds how much of it one claimed identifier may ask for, and the
bound is consulted before the work rather than after it.

Sixty arrivals per identifier inside a window of sixty seconds; six for the
enrolment identifier; four thousand and ninety-six identifiers counted at once.
Each is a constant of the landed type with its reason argued at the constant,
read out of it rather than restated here:

    git grep -nE 'public const (int|string) (WindowSeconds|ArrivalsPerPairing|ArrivalsPerEnrolment|PairingsCounted|EnrolmentPairingId)' -- Jellyfin.Plugin.ServerPairing/Api/ArrivalLimit.cs

**It counts the identifier the request claims, which is not the same as the
identifier it turns out to hold.** Before verification there is no other
identifier to count: the header is what arrived. So a stranger who knows a
pairing's identifier can spend that pairing's allowance, and that is not repaired
here. The alternative is worse rather than better. One allowance for the whole
plane lets any flood starve every pairing at once, where this confines it to the
identifier the flood claims, and the identifier is derived from both public keys,
so a stranger cannot produce one for a pairing they do not already know.

**Every `hello` carries the same identifier, so every enrolment shares one
allowance, and that allowance is the harder one.** It is the only thing on this
plane a stranger reaches knowing nothing, and an enrolment is a handful of
requests between two operators sitting at two screens. The cost is real and is
stated rather than hidden: a stranger spending that allowance refuses a genuine
`hello` arriving in the same window. Anything this protocol cannot read an
identifier out of at all is counted with them, under one shared count, because
none of it can ever verify and counting each spelling apart would hand a
stranger a fresh allowance per spelling.

**A window is fixed rather than sliding.** It starts at the first arrival counted
into it, so twice the allowance can arrive inside one span of sixty seconds by
falling either side of a boundary. A sliding window removes that and costs one
remembered instant per arrival instead of one counter per identifier, which is
the memory this bound exists to hold down.

**Nothing is dropped to make room except a window that has run out.** A flood
claiming a fresh identifier per request fills the table and is then refused for
want of room; it does not displace a pairing that is being counted, because the
allowance handed back by displacing one would be somebody else's. Room comes back
when the windows in the table run out, so a filled table is a refusal for one
window rather than for the life of the process.

**What this does not buy is availability.** No limit does. A flood large enough
is refused and the refusals are still answered, and a peer sharing the flooded
identifier is refused with it. What it buys is that the flood stops at a counter
instead of reaching a signature computation per request.

A refused arrival is answered with `refused`, like every other cause in that row,
so a caller learns from it exactly what it learns from any other refusal. One
thing it can measure is in the timing class below: an arrival past the limit is
answered without the signature computation the others pay for, so it is faster.
What that hands over is that the identifier the caller itself claimed has used
its allowance, which is a fact about the caller's own sending rather than about
this server's pairings, and the limit counts an identifier this server has never
heard of exactly as it counts one it holds.

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

| State | `hello` | `confirm` | `rotate` | `revoke` | `unpair` | `exchange` |
| --- | --- | --- | --- | --- | --- | --- |
| `Absent` | `refused` | `refused` | `refused` | `refused` | `refused` | `refused` |
| `Offered` | record the peer key, answer with this side's key, go to `Pending` | `refused` | `refused` | `refused` | `refused` | `refused` |
| `Pending` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | go to `ConfirmedByPeer` | `state` | go to `Revoked` | go to `Revoked` | `state` |
| `ConfirmedHere` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | go to `Active` | `state` | go to `Revoked` | go to `Revoked` | `state` |
| `ConfirmedByPeer` | identical key: answer as before, stay. Different key: close the window, go to `Absent`, `refused` | stay, answer as before | `state` | go to `Revoked` | go to `Revoked` | `state` |
| `Active` | `refused` | stay, answer as before | accept the replacement key, go to `Rotating` | go to `Revoked` | go to `Revoked` | answer it |
| `Rotating` | `refused` | stay, answer as before | `state` | go to `Revoked` | go to `Revoked` | answer it |
| `Revoked` | `refused` | `refused` | `refused` | `refused` | `refused` | `refused` |

Five things in that table are worth saying in words, because a reader can find
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

`unpair` takes the same cells as `revoke`, and it is a second message rather than
a second meaning of the first because of what happens on the sending side and in
the record, neither of which this table holds. Revoking is the security action:
it asks the peer for nothing and works against a peer that is hostile or gone.
Unpairing is the ordinary one: two operators are finished, so the sending side
asks the peer to end its side first and then ends its own, and it records whether
the peer acknowledged. A receiver handed one envelope for both could not tell them
apart, would record the wrong event, and could never say whether an
acknowledgement was owed, which are the two things issue #56 exists to define. So
a verified `unpair` completes the receiving side without that side's operator
being asked, because the pairing is already gone from the other end and a half
pairing is worse than acting, and the record it writes names `unpair` as the
cause where a revocation names `revoke`. `Offered` refuses it for the reason it
refuses `revoke`: no peer key has arrived there, so nothing could have signed it.
What the sending side does, the audit entry, and the wording an operator is shown
are issue #56's and are not decided by this table.

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

### What holds a pairing that is not yet identified

The first row of that table has a problem the other five do not. This section
carried both answers and took neither for as long as the row existed; it takes
one now, so the row is written against a decision rather than against whichever
change reaches it first.

A pairing is held by its identifier, and the identifier is derived from both
public keys. A window opens before any peer key has arrived, so at the moment
this table says a pairing moves from `Absent` to `Offered` there is no identifier
to hold it under. The wire already says as much about the request that arrives in
that state:

    git grep -n "^them. Its .X-Pairing-Id. is 32" origin/master -- docs/protocol.md
    origin/master:docs/protocol.md:436:them. Its `X-Pairing-Id` is 32 `0` characters, which is what line 5 of its

**`OFFERED` IS WRITTEN, UNDER A PROVISIONAL IDENTIFIER.** Opening a window mints
one and writes the record under it. The record moves to the derived identifier at
the transition that first has one, which is `Offered` to `Pending`, and the
provisional identifier is retired there and never reused.

The other answer was that `Offered` is a state no record is written for, and what
that costs is why it is not taken. The reader that asks what state a pairing is in
would answer `Absent` for one this table says is `Offered`, so both tables above
would describe a state nothing can report. The fifth row of the local events table
- an administrator revoking from `Offered` - would name a pairing nothing could
find, and revocation reaching a half-built pairing is a property rather than a
convenience. And an open window would be visible only inside `EnrolmentWindow`,
which holds an address and no state, so a dashboard would have to read one surface
for a pairing an operator has started and another for every pairing that finished.

**WHAT THE ANSWER TAKEN COSTS IS THAT AN IDENTIFIER MOVES, ONCE.** The guarantee
it looks like it breaks is worth stating exactly rather than waving at. `Revoked`
records are kept so that a later request naming an identifier is refused rather
than treated as new, and that is a guarantee about DERIVED identifiers: a derived
identifier is written once, never moves, and is never reused. What moves here is a
handle this side made for itself and no peer has ever seen.

**A PROVISIONAL IDENTIFIER CANNOT BE A WIRE IDENTIFIER, AND THAT IS A SHAPE RATHER
THAN A PROMISE.** A pairing identifier on the wire is exactly 32 lowercase hex
characters:

    git grep -n 'public const int HexFieldLength' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/FieldShape.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/FieldShape.cs:19:    public const int HexFieldLength = 32;

A provisional one is a word, a hyphen and 32 hex characters, so it is longer than
a wire identifier and carries a byte no hex field may hold:

    git grep -n 'public const string Prefix' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/ProvisionalPairingId.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/ProvisionalPairingId.cs:46:    public const string Prefix = "offered-";

An arriving request naming one is therefore refused by the field shape before any
store is read, so no peer can reach a record held under one, and the two
namespaces are disjoint by construction rather than by anybody remembering to keep
them apart. Nothing about the value is secret: it is random so that two windows
opened in the same second are two records, and it is unreachable because of its
shape rather than because of its entropy.

**THE MOVE DELETES BEFORE IT WRITES, SO AN INTERRUPTION FAILS CLOSED.** Each of
the store's two operations is all or nothing and the pair of them is not, so a
process that dies between them leaves one outcome or the other. Deleting first
leaves no record at all, which is `Absent`, which is where an enrolment that was
interrupted belongs and is what a second `hello` with a different key already
produces. Writing first would leave one pairing under two identifiers, and the one
nothing will ever name again would sit in the file until somebody looked.

WHICH TYPE MINTS THE IDENTIFIER IS DECIDED AND BUILT, AND THIS PASSAGE SAID IT WAS
NOT. It said no pairing had ever been in `Offered` on a running server and that
nothing in this plugin had ever minted one of these. The join between the window
and the state machine is what mints one and writes the record, and it is a type of
its own rather than a method on either of the two it joins:

    git grep -n 'ProvisionalPairingId.Mint()' origin/master -- Jellyfin.Plugin.ServerPairing/
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/Enrolment.cs:113:        var pairingId = ProvisionalPairingId.Mint();

The window itself is unmoved and still holds itself against the peer address,
writes no record and calls no state machine, which is why the join is a third type:

    git grep -n 'OpenAddresses' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:264:    public IReadOnlyList<string> OpenAddresses(DateTimeOffset at)

THIS PARAGRAPH SAID WHAT THE ADMINISTRATIVE ACTION LOOKS LIKE WAS NOT DECIDED,
THAT NOTHING ON A SERVER CALLED THE JOIN, AND NAMED ISSUE #53 FOR THE ENDPOINT
THAT WOULD. The action is `AdministrativePlaneController.OpenEnrolmentWindow`,
issue #357, and [`endpoints.md`](endpoints.md) carries its row and its shape: it
opens the window against the peer address the configuration holds, answers the
identifier the record was written under, and refuses under a word each what the
window refuses. #53 is about how a dashboard request is authenticated and never
asked for an action. What has not happened is a run: no window has been opened
on a running server, and no pairing has been in `Offered` on one.

The record it writes carries the peer address, which is the field the reader of
the open windows and the refusal against re-pairing an existing peer both need,
and which no message on the wire supplies:

    git grep -n 'public string? PeerAddress' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/PairingRecord.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PairingRecord.cs:107:    public string? PeerAddress { get; }

## The error taxonomy

Every refusal on the pairing plane is HTTP 403 with a body of exactly one JSON
object with exactly one member:

```
{"code":"refused"}
```

The shape never varies. Only the code varies, and it varies only for a caller
that has already proved it holds the key, or that is inside a window an
administrator opened deliberately.

**`version` IS THE ONE EXCEPTION TO THAT SHAPE AND IT IS NAMED HERE RATHER THAN
DISCOVERED.** THIS SECTION SAID EVERY BODY CARRIES EXACTLY ONE MEMBER WITH NO
EXCEPTION. That refusal carries two more, which are the range this server speaks:

```
{"code":"version","versionLow":1,"versionHigh":1}
```

The member names are the ones a `hello` request uses for the same two numbers, so
a peer reads one spelling of a version range rather than two, and the numbers are
read out of the same list the negotiation selects against rather than written
beside it.

Why the exception is worth its cost. A refusal that says only "no version in
common" leaves the operator on the far side with nothing to act on, and every
mismatch between two servers becomes a support conversation instead of a sentence
on a page. What it discloses is nothing: the range is what a `hello` response
advertises anyway and this document states it, so a probe learns from the refusal
exactly what it learns from reading this file. Putting it in the `hello` response
instead fails the one case that matters, because a peer outside the overlap never
gets a `hello` response.

Why it is an exception rather than a rule. Every other code is seen by a caller
that has already proved it holds the key or is inside a window an administrator
opened, and none of them has anything to add that the caller cannot already work
out. Making refusal bodies variable-length in general would give every future code
a place to leak into; making this one carry two named numbers does not. Issue #25
is where that was decided.

| Code | What caused it | Who can ever see it | Distinguishable from its neighbours |
| --- | --- | --- | --- |
| `refused` | An unknown pairing identifier, a signature that does not verify, a body over its limit, an arrival past the limit for the identifier it claims, a malformed header value, a request in a state that does not accept it from an unverified caller, a revoked pairing, a request when no pairing exists, a second `hello` with a different key, a fault on this side while serving the request | anyone | No, and deliberately so. Every one of those causes produces the same bytes |
| `clock` | The signature verified and the timestamp is outside the freshness window | only a caller holding a verifying key | Yes |
| `version` | No version in common, which is a `hello` whose range does not overlap this server's, or a request whose signature verified declaring a version this server does not speak | a caller inside an open enrolment window, or a caller holding a verifying key | Yes, to those callers only |
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

The arrival limit above is a second mechanism beside that bound rather than the
same one, and that choice is taken rather than still open. They are reached at
different moments and they cannot do each other's work: the nonce bound is per
pairing, is reached only by a request that has already verified, and stops a
replay being forgotten while it is still inside the freshness window; the arrival
limit is reached by anything that survives the shape checks, verified or not, and
stops a stranger buying a signature computation per request. Two limits sharing
one code between them is the state that was to be avoided, and they do not share
one: a full nonce store is `busy`, which only a caller holding a verifying key
ever sees, and an arrival past the limit is `refused`, because the caller it
usually answers has authenticated nothing.

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
An arrival past the limit is the second cause that does not, and it is answered
before the signature computation rather than after it, which is what the section
on that limit says it hands over.
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

`unpair` is a message of version 1 rather than the first message of a version 2,
and the reason is worth stating because [`versioning.md`](versioning.md) reads
the other way at first sight. That document says a change to what a version
already means is a removal wearing a smaller number, and the peer it protects is
one that speaks the old version and was never upgraded. There is no such peer:
no version of this plugin has been published, so no server anywhere speaks the
version 1 that lacked `unpair`. That was read rather than recalled on the day the
method was added, 2026-09-03, with `gh api
repos/Flowfin/jellyfin-plugin-server-pairing/releases --jq length` answering `0`
and the same call against `tags` answering `0`. The moment a release exists, that
argument stops being available, and a method added after it is a version 2.

A version this server does not know is not a version it guesses at. There is no
best-effort parse of an unknown version and no forward compatibility rule beyond
the range, because a message a server does not understand is one it cannot make a
security decision about.

WHERE THAT IS JUDGED ON AN ARRIVING REQUEST IS AFTER ITS SIGNATURE HAS VERIFIED,
and this paragraph said no route judged it at all. The plane reads the declared
version against the set this build speaks and answers `version`, with the range,
to a caller that has proved it holds the pairing's key. A caller that has not is
answered `refused` and learns nothing, which is the audience the taxonomy fixes
for that code, so the position of the check is the property rather than a
convenience: judging the version first would hand the range to anybody who asked.

```
git grep -ln 'SupportedVersions.Speaks' origin/master -- Jellyfin.Plugin.ServerPairing/
origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlane.cs
```

One site asks it, which is the plane, and the set it asks about is declared in one
place:

```
git grep -n 'public static bool Speaks' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/SupportedVersions.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/SupportedVersions.cs:66:    public static bool Speaks(string? version) =>
```

THAT IS NOT THE `state` RULE ABOVE, AND THE TWO ARE WORTH KEEPING APART. A version
this server does not speak at all is `version`. A version it does speak that is
not the one this pairing settled on is `state`, and that one needs the selected
version to have been remembered, which is issue #316. Nothing remembers one, so
only the first of the two is answered today.

THIS PARAGRAPH SAID NO SITE ANSWERS THE OTHER CALLER THE TAXONOMY LETS SEE
`version`, BECAUSE THE TWO RANGES ARE BODY MEMBERS AND NOTHING TURNED A BODY INTO
FIELDS. One does. A `hello` that verified is read against the member table above,
and a range that does not overlap this server's is answered `version` carrying the
range this build speaks:

```
git grep -n 'RefusalCause.NoVersionInCommon' -- Jellyfin.Plugin.ServerPairing/Api/PeerPlane.cs
```

WHICH OF THE TWO CALLERS REACHES IT TODAY IS THE HALF TO READ CAREFULLY. The
taxonomy names two, and the one that has a route is the caller holding a verifying
key. The other is a caller inside an open enrolment window, and that route does not
exist: a `hello` carries 32 zero characters where the identifier goes and is signed
with the private half of the key it offers, and no such verification is built, so
nothing on this plane admits a caller that holds no pairing key. A peer outside the
overlap that has never paired is therefore still refused for its signature. What is
answered `version` is a peer whose range moved out from under a pairing that
already has one.

## What is not decided here

The endpoint authorization table, issue #27.

The bounds of the enrolment window. This document names the state it puts the
pairing in and what closes it; how long it lasts, how many failures it takes and
the longest lifetime a caller may ask for are constants on `EnrolmentWindow`:

```
git grep -nE "public const int (LifetimeSeconds|MaximumLifetimeSeconds|FailuresAllowed)" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:47:    public const int LifetimeSeconds = 600;
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:60:    public const int MaximumLifetimeSeconds = 1800;
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:72:    public const int FailuresAllowed = 3;
```

The first of those is the default behind a setting now rather than the only value
a server runs on, which is the half of issue #18 that has landed. A value above
the maximum is refused as the configuration is read rather than shortened to it,
and the range is in [`configuration.md`](configuration.md):

```
git grep -n '`EnrolmentWindowSeconds` | ' origin/master -- docs/configuration.md
origin/master:docs/configuration.md:25:| `EnrolmentWindowSeconds` | `int` | `600` | 1 to 1800 |
```

The cryptographic parameters. [`crypto.md`](crypto.md) holds them, and the three
this document repeats are named at the top.

The `exchange` payloads, M6.

THIS LIST CARRIED WHETHER THE NONCE STORE SURVIVES A RESTART AND IT IS DECIDED.
It does not survive one, the reason is written where the freshness rule is rather
than here, and what is lost is stated there as plainly as it can be. It is off
this list because a decided question left on a list of undecided ones is read as
open by everybody who does not open the section it points at.

THIS LIST ALSO CARRIED THAT NO ROUTE ON THIS PLANE CONSULTS A FRESHNESS WINDOW AT
ALL, so that nothing in this section was refused by anything. One does. A request
that has verified is judged against the window and the nonce store, and a stale
one, a replayed one and one arriving with no room left to remember its nonce are
each answered with their own code out of the taxonomy above. It is off this list
for the same reason the restart question is: it was the fourth done condition of
issue #26 rather than a question this document left open, and it has been met.

What is NOT claimed by that, and is the half to read carefully: no request has
ever reached this on a running server. Nothing puts a key into a key store, so
nothing verifies there, and a request that does not verify is refused before its
timestamp is considered. What is proved is proved against the types and against
the two-instance harness, and what a server does is unchanged until a ceremony
derives a key pair, which is issue #19.

THIS LIST CARRIED WHAT IDENTIFIER HOLDS A PAIRING IN `Offered` AND IT IS DECIDED.
A record is written, under a provisional identifier that no peer can name and that
is retired when the derived one arrives. The answer, what the other one would have
cost, and what the decision does not settle are written under the local events
table above rather than here, because the row it is about is there. It is off this
list because a decided question left on a list of undecided ones is read as open
by everybody who does not open the section it points at.

**Where this side remembers the version a pairing settled on.** Every message
after `hello` carries the selected version in `X-Pairing-Version` and nowhere
else, which this document states above, so a side that sends one has to have kept
it. Nothing in the tree keeps it:

    git grep -nE 'Version' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/PairingRecord.cs Jellyfin.Plugin.ServerPairing/Protocol/IPairingRecordStore.cs Jellyfin.Plugin.ServerPairing/KeyStore/PairingKeys.cs Jellyfin.Plugin.ServerPairing/KeyStore/IPairingKeyStore.cs ; echo "exit=$?"
    exit=1

Empty output, exit one, over the record, the record store, the key store and the
keys a pairing holds. THIS COMMAND ASKED FOR THE PEER ADDRESS AS WELL AND THIS
PARAGRAPH SAID THAT FIELD WAS MISSING TOO. The record carries one now, so a
command asking for both would answer with hits and prove nothing about the
version, which is why it asks for the version alone and the address is read on its
own line:

    git grep -n 'public string? PeerAddress' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/PairingRecord.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PairingRecord.cs:107:    public string? PeerAddress { get; }

So what is left unbuilt here is the version. It is issue #316, which was opened
because this paragraph said the field was claimed by no issue at all and that was
still true when it was read. The address was issue #350, which is the sub-issue
#18 was split into and which built the field and the producer that writes one. The
version has not bitten yet because nothing in this plugin sends a message: the
only mentions of the channel's send are its own declaration and its own call into
the HTTP client.

    git grep -n 'SendAsync' origin/master -- Jellyfin.Plugin.ServerPairing
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PeerChannel.cs:131:    public async Task<PeerReply> SendAsync(
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PeerChannel.cs:167:                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)

The first outbound message pays for both.
