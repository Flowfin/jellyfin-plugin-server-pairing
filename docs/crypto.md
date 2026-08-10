# The cryptographic building blocks, pinned once

Every cryptographic choice this plugin makes is here. A later issue does not get
to make one quietly, and where a number belongs to this document a second copy of
it somewhere else is a defect rather than a convenience.

Nothing described here is implemented. There is no key store, no enrolment and no
signed request in this tree, so every line below is a choice recorded before the
code rather than a reading of code that exists. One thing here is asserted by a
test today and it is named where it is.

## The rule this document exists to state

This plugin composes primitives that ship in the base class library. It does not
invent a primitive, a mode or a key derivation, and it does not reach for a
cryptographic package.

Where something is needed that is not a single call, this document names the
published construction it follows and links it, so that a reviewer checks an
implementation against a specification somebody else wrote rather than against a
paragraph written here.

That the primitives resolve from the base class library alone is checkable
against the plugin project rather than believed:

```
grep -n "PackageReference Include" Jellyfin.Plugin.ServerPairing/Jellyfin.Plugin.ServerPairing.csproj
23:    <PackageReference Include="Jellyfin.Controller" Version="$(JellyfinPackageVersion)" >
26:    <PackageReference Include="Jellyfin.Model" Version="$(JellyfinPackageVersion)">
32:    <PackageReference Include="SerilogAnalyzer" Version="0.15.0" PrivateAssets="All" />
33:    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556" PrivateAssets="All" />
34:    <PackageReference Include="SmartAnalyzers.MultithreadingAnalyzer" Version="1.1.31" PrivateAssets="All" />
```

Two server packages and three analyzers. No cryptographic package, and adding one
is a line in a diff that this document is the argument against.

## What was measured, and where

Every API named below was compiled and run against both supported target
frameworks before being written down, because an API that turns out not to exist
on one of the two lines is a design that has to be redone rather than a sentence
that has to be corrected. The programme and its output are in the pull request
that added this file. What it printed:

```
raw agreement bytes: 32
hkdf output bytes: 32
spki bytes: 91
fixed time equals: True
```

That is a measurement of the framework, not of this plugin. It says the calls
exist and return the shapes this document assumes. It says nothing about a
protocol nobody has written yet.

## The random source

`System.Security.Cryptography.RandomNumberGenerator`, and nothing else. Not
`System.Random`, not a seeded generator, and not a value derived from a clock.

| What | Bytes | Where it is used |
| --- | --- | --- |
| A request nonce | 16 | Every pairing plane request, [`protocol.md`](protocol.md) |
| An enrolment window token, where one is needed to name a window locally | 16 | Issue #18 |
| Any other value this plugin has to be unable to predict | 16, and never fewer | wherever it arises |

Sixteen bytes is 128 bits. The nonce has to be unique inside a window of a few
minutes on one pairing rather than unguessable for a lifetime, and 128 bits makes
a collision inside that window not a thing that happens. Nothing here needs more
and using more would be a number nobody could justify when asked.

There is no long-lived secret drawn from this generator, because there is no
transcribed secret in this design at all. That follows from the enrolment answer
in issue #1 and is the reason this table is as short as it is.

## The long term key pair

`ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)`.

The private half is generated on the server that holds it, never leaves it in any
encoding, and lives in the key store that M4 owns. The public half is exchanged as
its DER `SubjectPublicKeyInfo`, which is what `ExportSubjectPublicKeyInfo`
produces and what `ImportSubjectPublicKeyInfo` reads, so the bytes two servers
hash are the bytes the framework already agrees on rather than an encoding
invented here.

P-256 rather than a curve this document would have to argue for: it is in the
base class library on both supported server lines, it is the curve the platform
implements with hardware support, and nothing in this design needs more than the
128-bit security level it gives.

## The key derivation

The shared secret is the raw ECDH agreement,
`ECDiffieHellman.DeriveRawSecretAgreement`, 32 bytes on P-256.

That value is never used as a key. It goes through HKDF, which is
[RFC 5869](https://www.rfc-editor.org/rfc/rfc5869), in the base class library as
`System.Security.Cryptography.HKDF.DeriveKey`, with SHA-256:

```
HKDF.DeriveKey(
    HashAlgorithmName.SHA256,
    ikm:    the raw agreement,
    outputLength: 32,
    salt:   the pairing identifier, as its 16 raw bytes,
    info:   the context label of the key being derived)
```

The salt is the pairing identifier, which both sides derive from the two public
keys and which is therefore the same on both without being transmitted as key
material. The `info` is what separates one key from every other, and it is the
whole of why a key for one purpose cannot be used for another:

| Context label | The key it produces |
| --- | --- |
| `jellyfin-server-pairing/mac/a-to-b` | Authenticates requests from the server whose public key sorts first, to the other |
| `jellyfin-server-pairing/mac/b-to-a` | Authenticates requests in the other direction |

The sort is the same ascending byte order the pairing identifier uses in
[`protocol.md`](protocol.md), so both sides agree on which of them is A without
negotiating it and without either side choosing.

Two directions, two labels, two keys. A request signed with the a-to-b key does
not verify against the b-to-a key, so a captured request cannot be reflected back
at the server that sent it. That is the failure the separation exists to prevent,
and it is a real one: a single key for both directions makes reflection a matter
of resending the same bytes to the other endpoint.

A label is an exact ASCII string and a label is never assembled from a value that
could contain the separator. Adding a purpose means adding a label here, and a
purpose that reuses an existing label is the defect this table exists to make
visible.

## The message authentication

HMAC-SHA-256, `System.Security.Cryptography.HMACSHA256`, over the canonical byte
string.

The canonical bytes are not restated here. They are eight lines for a request and
six for a response, defined in [`protocol.md`](protocol.md), and that document is
the authority for them because they are a property of the wire rather than of the
cryptography. What this document pins is which algorithm consumes them, with
which key from the table above, and that the tag is the full 32 bytes rather than
a truncation.

The body is covered by its SHA-256 digest inside those lines rather than being
fed to the MAC directly, so the authenticated material has a fixed length
whatever the body is.

## The comparison used on anything secret

`CryptographicOperations.FixedTimeEquals`, everywhere, on every value where being
wrong in the first byte and being wrong in the last byte must cost the same.

A signature, a MAC tag, a derived key and a fingerprint compared with `==`, with
`Equals` or with `SequenceEqual` tell a caller who is allowed to keep asking how
many leading bytes were right. That is a working attack against a MAC and it is
the reason this is a rule rather than a preference.

This is the one line in this document a test asserts today.
`SecretComparisonTests` in the test project reads the plugin source and refuses a
comparison whose operand is named as secret material, and it proves it bites with
fixtures rather than by assertion: the same line written through
`CryptographicOperations.FixedTimeEquals` is not refused, and `signatureLength ==
64`, which is one word away from the line it refuses, is not refused either.

The assertion is about the call and not about the time it took. A timing
assertion on a shared build machine goes red on some later day for reasons that
have nothing to do with the code, and the first response to a flaky test is to
delete it, which is how a guard like this ends up not existing. Issue #16 asks
for the call to be asserted rather than measured, and that is what is done.

What the guard cannot do is read a type. It judges an identifier by its words, so
a secret held in a variable named `a` walks through it, and so does a comparison
split across two lines. It is a floor and the test file says so in the same
words.

## The fingerprint the two operators compare

SHA-256 over the same material the pairing identifier is derived from, with its
own context label so that neither value can stand in for the other, as written in
[`protocol.md`](protocol.md).

What an operator reads is the leading 128 bits of that digest, rendered as 32
lowercase hex characters in groups of four.

Why 128 bits and not fewer. The value an attacker wants is a second key pair
whose fingerprint matches the one the far operator is reading, which is a second
preimage rather than a collision: the honest party's key is already fixed, so the
birthday shortcut is not available. Second preimage on an n-bit truncation costs
about 2^n, so 128 bits is 2^128 and the length is not what fails first.

Why 128 bits and not more. The failure mode of a longer fingerprint is an
operator who compares the first group and the last group and calls it done, and
that failure is not detectable by anything. A value the length of a wireless
network key is one a person actually reads.

The grouping is part of the pinned construction rather than presentation, because
a fingerprint shown as an unbroken run of characters is one people compare
badly. What the page says around it is issue #54, and the comparison being
performed at all is the one mechanism in this design that is a person.

## What is deliberately absent

No encryption of the wire by this plugin. Confidentiality on the path is TLS with
the peer certificate pinned, which is settled in issue #1, and a second encryption
layer written here would be a construction nobody reviewed protecting bytes that
are already protected.

No padding scheme, because nothing here encrypts.

No custom curve, no custom mode, no custom key derivation, and no primitive
implemented in this repository. Every call named above is one the base class
library already ships, and a change that adds a cryptographic package reference is
a change against this document.

No password-authenticated key exchange. It would give a short transcribed code the
resistance to offline attack that a long one has, and there is none in the base
class library, so it means a cryptographic dependency or hand-rolling one. Neither
is on the table, and the enrolment answer removes the transcribed code that would
have made it worth arguing about.

No truncated key material anywhere. The fingerprint is a truncated digest of
public keys and is the only truncation in this document; a MAC tag is never
truncated and a derived key is never shortened.

## What this document does not do

It does not state the enrolment window's length, the timestamp window or the
nonce lifetime. The first is issue #18 and the other two are in
[`protocol.md`](protocol.md), which is where a reader implementing the wire will
be looking.

It does not describe how the key store holds any of this. That is M4, and issue
#31 is where what protects it at rest, and what does not, is answered.

Nothing here except the comparison rule is asserted by a test, because there is
nothing yet to assert it against.
