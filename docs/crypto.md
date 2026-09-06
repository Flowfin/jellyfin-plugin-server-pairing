# The cryptographic building blocks, pinned once

Every cryptographic choice this plugin makes is here. A later issue does not get
to make one quietly, and where a number belongs to this document a second copy of
it somewhere else is a defect rather than a convenience.

Some of the choices below are implemented and the rest are not, which is a
different sentence from the one this paragraph carried while none of them were.
They are named rather than counted, because a count in a document drifts against
the tree it describes and this one has drifted twice. The message authentication
and the fixed-time comparison are in the tree:

```
git grep -ln "HMACSHA256" origin/master -- Jellyfin.Plugin.ServerPairing
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/RequestAuthenticator.cs
```

The fingerprint the two operators compare is in the tree as well, with the pairing
identifier it shares its material with. The long term key pair, the agreement and
the key derivation are not:

```
git grep -lE "HKDF|ECDiffieHellman|SubjectPublicKeyInfo" origin/master -- Jellyfin.Plugin.ServerPairing Jellyfin.Plugin.ServerPairing.Tests
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Api/PeerPlaneTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/KeyStore/KeyMaterialTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/ArrivingBodyTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/HelloAuthenticationTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/PairingIdentityTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Wording/CeremonyWordingTests.cs
origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/KeyMaterial.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PairingIdentity.cs
```

THIS PARAGRAPH NAMED TWO FILES, THEN FOUR, THEN SIX, THEN SEVEN, AND WHAT MOVED
THIS TIME IS THE SENTENCE ABOVE THE COMMAND RATHER THAN THE COUNT UNDER IT. Every
time before this one, the answer was that a name had been cited somewhere new and
nothing had been called. That is still true of seven of the nine.
`HelloAuthenticationTests.cs` names the key pair's type in a fixture line the guard
there has to leave alone, which is the near miss for the signature primitive it
refuses, so it is a name held as test data and nothing more. `CeremonyWordingTests.cs`
is a list of words the ceremony wording may not use, where two of these names are
there to be kept off an operator's screen. `KeyOverlap.cs`, `KeyMaterial.cs` and
`KeyMaterialTests.cs` name the derivation in a comment, beside the length it
fixes. `PeerPlaneTests.cs` and `ArrivingBodyTests.cs` name the encoding in a
comment beside a public key member built out of bytes that are not a key, which
is the length this document measured used as test data and nothing more.

**THE TWO NEW ONES ARE DIFFERENT AND THAT IS WHY THIS SENTENCE CHANGED.**
`PairingIdentity.cs` is the construction under [`protocol.md`](protocol.md) that
derives the pairing identifier and the fingerprint from two public key encodings,
landed under issue #19. It names the encoding and calls neither the key pair nor
the agreement, because it digests the bytes it is handed and reads no key. Its
cases DO call `ECDiffieHellman.Create` and `ExportSubjectPublicKeyInfo`: the
material the construction is over is that encoding, and a case over arbitrary
bytes would not meet the lengths and the byte patterns a real key has. So a key
pair is created in the test project and in no other place.

WHAT THAT MOVES AND WHAT IT DOES NOT. No key described here has ever been derived,
held or destroyed by this PLUGIN, which is narrower than the sentence that stood
here and is still true: a pair a case creates and drops inside one method is not a
key this plugin holds, nothing writes one to a store, and no private half outlives
the test that made it. The agreement and the HKDF derivation are called by nothing
at all, in either project. What holds the rest of the claim up is unchanged:
nothing calls the one routine that would produce a pairing key, and nothing on a
request path reaches the store that would hold it:

```
git grep -n 'KeyMaterial.Fresh()' origin/master -- Jellyfin.Plugin.ServerPairing/
origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/KeyMaterial.cs:81:    public static KeyMaterial Fresh() => new KeyMaterial(RandomNumberGenerator.GetBytes(Length));
```

One hit, which is the declaration itself. There is still no enrolment, and every
line below about a key's life is a choice recorded before the code rather than a
reading of code that exists.

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
| An enrolment window token, if one is ever needed to name a window locally | 16 | Nothing. The landed window is named by the peer address an administrator entered and by nothing else |
| Any other value this plugin has to be unable to predict | 16, and never fewer | wherever it arises |

Sixteen bytes is 128 bits. The nonce has to be unique inside a window of a few
minutes on one pairing rather than unguessable for a lifetime, and 128 bits makes
a collision inside that window not a thing that happens. Nothing here needs more
and using more would be a number nobody could justify when asked.

There is no long-lived secret drawn from this generator, because there is no
transcribed secret in this design at all. That follows from the enrolment answer
in issue #1 and is the reason this table is as short as it is.

The middle row is a length held for a value nothing draws. The window that landed
holds no random bytes at all, because the identifier it would otherwise be named
by does not exist while it is open:

```
git grep -n "held against the address an administrator entered" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/EnrolmentWindow.cs:16:/// A window is held against the address an administrator entered and against nothing else,
```

The row stays because the length is what a token would have to be if one is ever
wanted, and it says what it is rather than reading as a value in use.

## The long term key pair

`ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256)`.

The private half is generated on the server that holds it, never leaves it in any
encoding, and lives in the key store that M4 owns. The public half is exchanged as
its DER `SubjectPublicKeyInfo`, which is what `ExportSubjectPublicKeyInfo`
produces and what `ImportSubjectPublicKeyInfo` reads, so the bytes two servers
hash are the bytes the framework already agrees on rather than an encoding
invented here.

P-256 rather than a curve this document would have to argue for: it is in the
base class library on both supported server lines, and nothing in this design
needs more than the 128-bit security level it gives.

Both of those are checkable from a checkout, which is why they are the whole of
the argument. Whether the processor underneath accelerates the curve is not.
Which curves are offloaded is decided by the operating system's cryptographic
provider and by the machine a server runs on, nothing in this tree can read
either, and no measurement of it was taken here.

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

## What authenticates a `hello`

Nothing authenticates a `hello`. It carries no signature, this document pins no
primitive that could produce one, and none is owed.

THIS DOCUMENT PINNED NO ANSWER AT ALL UNTIL NOW, AND
[`protocol.md`](protocol.md) PINNED ONE IT COULD NOT SUPPLY. That document said a
`hello` is signed with the private half of the key it offers. The long-term key
pair above is an `ECDiffieHellman` key and produces no signature, and the one
authentication primitive here is HMAC-SHA-256 over a key derived from an
agreement, which neither side can compute while a `hello` is in flight: the
receiver holds no peer public key yet, so there is nothing to derive from. The
first message of every pairing therefore rested on a construction nobody had
chosen. Issue #364 is where that was found and this section is the answer.

WHY NO PRIMITIVE IS ADDED RATHER THAN ONE BEING CHOSEN. A signature on a `hello`
can only be made with a key the receiver has no prior knowledge of, so it proves
that the sender holds the key in the message and nothing about who the sender is.
To prove more, the signing key would have to be one the two operators compare,
which puts it into the fingerprint - and then the fingerprint covers two keys and
the ceremony grows in order to buy a proof the ceremony already gives. A signing
key left outside the fingerprint is a key nobody ever checks, and a signature made
with it is a ceremony of its own rather than evidence.

What it would cost is also more than a call. `ECDsa` on the same curve is in the
base class library, and using the long-term key pair for it means taking a private
scalar out of one algorithm and into another - a construction this document would
have to argue for, and one key serving two purposes, which is the thing the
context labels above exist to prevent. A second key pair is the alternative, and
it is the paragraph before this one.

What carries the weight instead is three things, none of them a primitive, and
they are written out once in [`protocol.md`](protocol.md) under the `hello`
message rather than twice: the enrolment window admits the message at all, the
comparison the two operators perform turns the offered key into an identity, and
the first message after `hello` proves possession because it is signed with a key
derived from an agreement only the holder of the private half can compute. Key
confirmation rather than a proof of possession up front.

WHAT IT COSTS. A `hello` is unauthenticated, so anyone who can reach the endpoint
while an enrolment window is open can put a public key of their own choosing in
front of an operator, and this plugin will show its fingerprint faithfully.
Nothing in the cryptography refuses that. The operator comparison is the whole of
what stands between it and a pairing, which is the same thing this document says
below about the fingerprint - the comparison being performed at all is the one
mechanism in this design that is a person - and a signed `hello` would not have
improved that sentence.

## The comparison used on anything secret

`CryptographicOperations.FixedTimeEquals`, everywhere, on every value where being
wrong in the first byte and being wrong in the last byte must cost the same.

A signature, a MAC tag, a derived key and a fingerprint compared with `==`, with
`Equals` or with `SequenceEqual` tell a caller who is allowed to keep asking how
many leading bytes were right. That is a working attack against a MAC and it is
the reason this is a rule rather than a preference.

This is the one rule in this document a guard over the plugin source refuses.
What else here a test asserts, and what none of those tests reads, is at the end
of this document.
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
badly. The sentences said around it are in the tree, in `CeremonyWording`, and
the page that would show them is issue #49. The comparison being performed at all
is the one mechanism in this design that is a person.

**THE DIGEST AND THE TRUNCATION ARE IN THE TREE AND THE GROUPING IS NOT, AND THE
REASON IS THAT NOTHING PINS IT.** `PairingIdentity` computes the digest and hands
back the leading 128 bits, and it refuses a value that is not a whole digest
rather than shortening whatever it is given:

```
git grep -n 'public static string ShownToAnOperator' origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/PairingIdentity.cs
origin/master:Jellyfin.Plugin.ServerPairing/Protocol/PairingIdentity.cs:111:    public static string ShownToAnOperator(string fingerprintDigest)
```

What separates one group of four from the next is written nowhere - not here, not
in [`protocol.md`](protocol.md), not in `CeremonyWording` - so a type choosing a
separator would be taking a decision this document owns and has not taken. That is
the same shape as the question issue #369 answered one document over, where two
readings of a sentence produced two different signed byte strings, and it is
recorded on issue #19 rather than settled in passing by whoever writes the page.
Until it is taken, the sentence above claims a pinned construction that is pinned
except for one byte.

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

No signature. Nothing this plugin produces is signed with an asymmetric key, and
the `hello` that used to be is the section above.

No password-authenticated key exchange. It would give a short transcribed code the
resistance to offline attack that a long one has, and there is none in the base
class library, so it means a cryptographic dependency or hand-rolling one. Neither
is on the table, and the enrolment answer removes the transcribed code that would
have made it worth arguing about.

No truncated key material anywhere. A MAC tag is never truncated and a derived
key is never shortened. Two truncated digests do appear here and neither is key
material: the fingerprint above, and the pairing identifier the HKDF salt is
taken from, which [`protocol.md`](protocol.md) fixes as the first 16 bytes of a
digest over the same public keys. Both are public values, so shortening either
removes nothing an attacker holding those keys could not compute in full.

## What this document does not do

It does not state the enrolment window's length, the timestamp window or the
nonce lifetime. All three are constants in the protocol code, and
[`protocol.md`](protocol.md) is where a reader implementing the wire will be
looking for them.

It does not describe how the key store holds any of this. THIS SENTENCE SAID THAT
WAS STILL OWED, AND IT IS WRITTEN: [`keystore.md`](keystore.md) carries what
protects the file at rest and what does not, under a heading of its own.

    git grep -n '^## What protects it at rest' -- docs/keystore.md

Five things here are asserted by a test, and two of the five assertions read this
file. THIS PARAGRAPH SAID FOUR; the fifth arrived with the `hello` answer above.
The comparison rule is refused by `SecretComparisonTests`, which opens this
document and requires it to go on naming the call it pins. The tag length is
asserted by `PairingCredentialTests` against what the pairing plane accepts as a
credential. The grouping is asserted by `CeremonyWordingTests` against the
sentence an operator reads, and that test also refuses the names in this document
appearing in that sentence. The derived key's length is asserted by
`KeyMaterialTests` against the length the rotation overlap already refuses
anything else against, rather than against a second number beside it. The `hello`
answer is asserted by `HelloAuthenticationTests`, which opens this document and
[`protocol.md`](protocol.md) and requires both to carry the same answer and the
same statement of what it costs, and which refuses in the plugin source the
signature primitives that answer rules out.

The second and third hold their own copy of the value, with this document named
in a comment beside it. That is a citation rather than a reading, and the
difference is the whole of what changing a number here would do. One test opens
this file by a path written out in it:

```
git grep -E 'File\.ReadAllText\(.*"crypto' origin/master -- Jellyfin.Plugin.ServerPairing.Tests
origin/master:Jellyfin.Plugin.ServerPairing.Tests/SecretComparisonTests.cs:        var document = File.ReadAllText(Path.Join(RepositoryRoot(), "docs", "crypto.md"));
```

THE SECOND READER IS NOT IN THAT OUTPUT AND IS NOT MISSING FROM THE COUNT.
`HelloAuthenticationTests` opens this document through a file name it is handed
rather than one written into the call, so a grep for the literal cannot see it,
and a reader who takes that one line for the whole set of readers is wrong in the
direction that matters. The command below is the wider one and it does return it.

Seven name it, and a grep for the name cannot tell a citation from a reading,
which is why both commands are here rather than the second alone:

```
git grep -l "crypto.md" origin/master -- Jellyfin.Plugin.ServerPairing.Tests
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Api/PeerPlaneTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/KeyStore/KeyMaterialTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/ArrivingBodyTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/HelloAuthenticationTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Protocol/PairingCredentialTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/SecretComparisonTests.cs
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Wording/CeremonyWordingTests.cs
```

THAT COUNT HAS READ FOUR AND SIX BEFORE READING SEVEN, AND TWO OF THE SEVEN ARE
WEAKER THAN THE REST. The body reader's cases name this file for the public key
length it measured, which is a length used as test data rather than a value either
of them asserts, so neither adds an assertion to the five the paragraphs above
count. The number moved and what is asserted did not.

So changing the tag length or the grouping here reddens nothing. The value would
have to be changed in the test as well, and nothing says so at the moment
somebody changes one.

The rest is not asserted anywhere, because there is nothing yet to assert it
against.
