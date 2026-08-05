# What is logged and what may never be logged

Jellyfin writes its log to a file, and an operator asking for help will paste
that file into a forum thread without reading it first. That is not a misuse to
be scolded; it is the normal way a support request happens, and it means the log
is part of the threat model rather than an afterthought. Everything below is
written on the assumption that the log will be read by somebody who was never
meant to have it.

Two lists. The first is a feature and the second is a rule.

## What is logged

The audit trail exists so that an operator can answer six questions from the log
alone, with no database access and no support tooling.

When was a pairing created, and by which administrator, and against which peer.
Logged at Information when a pairing reaches its established state, with the
pairing identifier, the local administrator's user identifier, the peer address
the operator approved, and the peer's key fingerprint truncated as described
below.

When was a key rotated. Logged at Information at the start and at the completion
of a rotation, with the pairing identifier, the outgoing fingerprint and the
incoming fingerprint, both truncated. Two entries rather than one, because a
rotation that starts and never completes is the interesting case and a single
completion entry cannot show it.

When was a pairing revoked, and by which side. Logged at Warning, with the
pairing identifier and whether the revocation was raised locally or received
from the peer. Warning rather than Information because revocation is terminal
and an operator scanning at the default level has to see it.

How many requests were refused, and for which reason. Logged at Warning per
refusal, with the pairing identifier, the refusal reason as a fixed enumerated
value, and nothing derived from the rejected material. The enumerated value is
the same set the refusal path uses internally, so a log line and a response can
be lined up. The refusal path itself is designed not to be an oracle, which is
#28, and that constrains what the peer is told rather than what the log records:
the log may distinguish a bad signature from an expired timestamp even where the
response may not.

Enrolment attempts that never became a pairing are logged at Information with
the reason, because an operator debugging a failed pairing has nothing else to
look at, and because a run of them is what an attack on the enrolment window
looks like from this side.

At Debug, and only at Debug, the state machine's transitions with the pairing
identifier, the from-state and the to-state. Nothing else is added at Debug. The
rule below applies at Debug exactly as it applies everywhere.

Fields that are always safe to write, at any level: a pairing identifier, a peer
address, a state name, a refusal reason, a protocol version, a count, and a
truncated fingerprint.

## What may never be logged

At any level, including Debug, including a message that was only ever meant to be
seen during development.

Key material of any kind. A private key, a symmetric key, a key derivation
input, a key derivation output, and any encoding of any of them.

The enrolment secret, in any form: the generated value, what the operator
transcribed, and anything derived from it that is not a one-way function of it
with the full output length preserved.

A fingerprint preimage. The fingerprint may be logged, truncated. The bytes it
was computed over may not, because logging the preimage alongside the truncated
digest hands over exactly what the truncation was for.

An authorization header, a request signature, and a nonce. The header and the
signature are credentials for one request each, and a nonce logged is a nonce an
attacker knows has been spent.

The mapped user identities on the peer. Which local user corresponds to which
account on another server is personal data about two people, it is not needed to
debug the protocol, and the pairing identifier is enough to find the mapping in
the store where it belongs. What may be logged is that a mapping was applied and
how many entries the table holds.

The body of any peer-supplied request, and any peer-supplied string echoed back.
A peer-controlled string in a log file is a peer-controlled string in whatever
reads the log next, and #52 is the same rule for the dashboard.

## The truncated fingerprint

A key fingerprint may be written to the log truncated to its first 64 bits,
rendered as hexadecimal.

Why that is acceptable. The fingerprint is a digest of a public key, so the value
is not secret and the truncation is not protecting a secret. What truncation
protects is the human comparison: a support thread quoting a full fingerprint
invites an operator to accept a peer because a string matched something they read
online, and a value that is explicitly a support identifier rather than a
verification value is harder to misuse that way. The verification value the
operator actually compares is a separate rendering, and its length is fixed
where the rest of the cryptographic parameters are fixed, in #16.

Why 64 bits and not fewer. It has to distinguish the pairings on one server from
each other in a support conversation, and a birthday collision at 64 bits is not
reachable by the number of pairings a Jellyfin server will ever hold. Why not
more: past this point it stops being a support identifier and starts looking like
something to compare, which is the confusion this paragraph exists to prevent.

This document fixes the length used in the log. It does not fix the hash
function, the encoding used on the dashboard, or the length of the value the
operator compares. Those are #16.

## The rule that makes this hold

Everything above is prose, and prose is not a mechanism. What turns it into one
is a test: a full lifecycle, enrolment through rotation through revocation, run
against a capturing logger at the most verbose level the logging framework has,
asserting that every secret the test generated is absent from the captured
output.

The test generates the secrets rather than receiving them as constants, so it
cannot pass by asserting the absence of a value that never existed. It asserts
absence of each secret in every encoding the code could plausibly emit, at
minimum the raw bytes rendered as hexadecimal, the same bytes base64 encoded, and
the string form the type's own `ToString` produces. A secret that leaks through
an encoding nobody enumerated is the failure mode this list is trying to shrink,
and it is not claimed to be closed.

The same test answers the audit questions from the other end: the captured output
is asserted to contain an entry for the pairing's creation, its rotation and its
revocation, so the first list is verified as a feature rather than assumed.

## What is not yet true

That test does not exist. There is no pairing lifecycle to drive yet and no test
project to hold it, so this document is at present a specification and nothing
refuses a violation of it. The lifecycle is M3, the test project is #4, and this
paragraph is the disclosure that the rule above is currently prose.

Nothing in this document has been measured against a running plugin. Every
statement about what is logged is a statement about what the code must do, not a
report of what it does.
