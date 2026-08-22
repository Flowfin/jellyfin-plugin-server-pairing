# What this plugin logs, and what it may never log

A Jellyfin log file is something an operator pastes into a public forum thread
when asking for help. Whatever this plugin writes there is readable by everyone
who reads that thread, months later, out of context. So the contents of the log
are part of the threat model rather than a detail of the implementation.

Two lists follow. The second one is the one that matters.

## What is logged

Every entry below carries the pairing identifier, so entries about one pairing
can be pulled out of a log holding several.

| Event | Level | Fields |
| --- | --- | --- |
| An enrolment was started | Information | pairing id, peer address, the administrator who started it |
| An enrolment completed | Information | pairing id, peer address, the administrator who confirmed it, the protocol version agreed |
| An enrolment expired or was abandoned | Information | pairing id, reason |
| A key was rotated | Information | pairing id, which side initiated, the overlap window that is now open |
| A rotation overlap closed | Information | pairing id |
| A pairing was revoked | Warning | pairing id, which side revoked, whether the peer was reachable at the time |
| A request was refused | Warning | pairing id where one is known, the refusal category, the peer address |
| A refusal rate crossed its threshold | Warning | pairing id, the count and the window it was counted over |
| A mapping was added, changed or removed | Information | pairing id, the administrator who did it, which direction |
| The key store could not be read or written | Error | the operation, the reason, no path contents |

Debug adds timing and state machine transitions for the same events. It adds no
field that is not in the table above.

## What may never be logged, at any level, including Debug

- key material of any kind, in any encoding, whole or partial
- the enrolment secret, before or after it is consumed
- the preimage of a fingerprint
- an authorization header, whole or partial
- a request signature
- a request or response body from the pairing plane
- the mapped user identities on the peer, in any form, including a username

A peer address is fine. A pairing identifier is fine. A refusal category is
fine, and it is deliberately a category rather than a detail, because the log
and the refusal path have the same oracle problem.

A truncated key fingerprint is fine only where this document says how many bits
it exposes and why that is acceptable for the thing it identifies.

One truncation is defined in the tree. [`crypto.md`](crypto.md) fixes the
leading 128 bits of a SHA-256 digest as the value two operators compare, and
argues that length in both directions. The number is cited rather than restated
here, because a second copy of a cryptographic parameter is the copy that goes
stale, and that document is the authority for every one of them.

What is missing is the second half of the rule rather than the first. This
document has not said whether that value may be logged, or what logging it would
expose for the pairing it identifies, so none may be logged. The prohibition
rests on the sentence above being unwritten rather than on there being no
truncation to prohibit, which is what it used to rest on.

## The audit trail these lists have to support

From the log alone, and with nothing else to hand, an operator can answer:

- when a pairing was created, and by which administrator
- which peer it was created against
- when a key was rotated, and which side started it
- when a pairing was revoked, and which side revoked it
- how many requests were refused, over what window, and in which category

Each of those is answerable from the table above. That is the reason the table
is shaped the way it is, rather than being a list of whatever happened to be
convenient at the call site.

## The rule that makes this hold

A promise in a document is not a guarantee about a log. What makes this hold is
a test that drives a full enrolment, a rotation and a revocation against a
capturing logger, at Debug, and asserts that the captured text contains none of
the secrets the run generated. The test generates the secrets, so it knows every
byte it is looking for, and it fails on any of them appearing anywhere in the
captured output.

That test does not exist. When this was written there was also no test project
to put it in; one landed afterwards, in `87cee9c`.

The reason given here after that was that there is no enrolment, no rotation and
no revocation to drive. That is no longer the reason. A rotation and a revocation
are both in the tree:

    git grep -n "public RotationOutcome Rotate" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs:146:    public RotationOutcome Rotate(ReadOnlySpan<byte> replacement, DateTimeOffset at, DateTimeOffset supersededStopsAt)

    git grep -n "AdministratorRevoked = " origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs:31:    AdministratorRevoked = 3,

What is missing is the logging itself. Nothing in the plugin takes a logger, so a
capturing logger would be handed a run that writes nothing and would pass on an
empty set, which is worse than having no test:

    git grep -nE "ILogger|_logger" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    exit=1

Until the test exists, both lists above are a design statement and nothing
refuses a call site that violates them. This paragraph is the whole of that
disclosure and no later edit of this file turns it into a statement that the
logging has been checked.
