# Testing policy

Every test in this repository runs on a plain CI runner: no display attached, no
elevation, and no write to the machine certificate store or any other
machine-wide trust state. That is a birth requirement rather than something to
retrofit. The alternative is a suite only its author can run, which stops running
the day a runner image changes and takes a while to be noticed, because a suite
that cannot start looks a lot like a suite that passed.

Three kinds of test would break the rule. Each is refused by name, and each
refusal carries the replacement that gets the same property a different way. A
refusal without a replacement is a gap, not a policy.

## Refused: installing a certificate authority into the machine trust store

The tempting shape is to exercise certificate pinning end to end by minting a
certificate authority, installing it into the machine trust store, serving TLS
from it, and watching the pinning code accept or refuse. It is refused. Writing
to the machine trust store needs elevation on Windows and root on Linux, it
changes state outside the test process that no test can be trusted to undo, and
a runner that has been mutated by one test is a runner that lies to the next one.

Replaced by generating the certificate in memory with `CertificateRequest`,
handing it to the pinning code directly, and driving the HTTP layer through an
injected `HttpMessageHandler`. No real handshake happens, no trust store is
consulted, and the thing under test is the pinning decision rather than the
platform's TLS stack. What this does not cover is stated plainly: it does not
prove that the platform's own chain building behaves as expected, only that the
pinning code reaches the verdict it should for a given certificate.

## Refused: booting two real Jellyfin servers and pairing them

The tempting shape is the most convincing one: two real servers, the real
plugin installed on both, a real pairing. It is refused. It needs two server
installations, two databases, two ports and a media library on a runner, it takes
minutes rather than seconds, and every one of those parts fails for reasons that
have nothing to do with the pairing protocol, which is the definition of a flaky
test.

Replaced by an in-process harness that constructs two sets of the plugin's own
services and connects them over an in-memory transport, so a full enrolment, key
rotation and revocation runs inside one test process with no network and no
server. That harness is #29, and it is the thing most of the protocol suite is
written against. What this does not cover: it does not prove the plugin loads
into a real Jellyfin server or that its endpoints are routed by the real
pipeline. That is a different question and it belongs to #70.

## Refused: driving the dashboard page in a browser

The tempting shape is a browser driver clicking through the configuration page.
It is refused. A browser driver is a large dependency with its own version
treadmill, it needs a display or a headless mode that is itself a moving target,
and the thing it usually proves is that a selector still matches.

Replaced by two things that together cover what the browser test was for.
The embedded page is asserted on as text, for the structure the endpoints depend
on, so a renamed element identifier that would break the page is a failing test
rather than a silent regression. Every endpoint behind the page has controller
level tests. What this does not cover is real rendering: a page that is
structurally correct and visually broken passes this suite, and nothing here
claims otherwise.

## Two more rules

Nothing writes outside a per-test temporary directory. The key store is
constructed against an injected path, never a discovered one, so a test cannot
reach the real store and a bug cannot either.

Nothing reads the wall clock directly. Every expiry, every skew check and every
rotation window is tested against an injected clock, which is #26. A test that
sleeps to make time pass is a slow test that still cannot reach the interesting
cases, because the interesting cases are a year of clock drift and a window that
closed one tick ago.

## The grep that checks the first two rules

The refusals above are prose. What a machine can refuse is the presence of the
things they forbid, and this is the command that reads it:

    git grep -n -i -E "Selenium|Playwright|WebDriver|ChromeDriver|Puppeteer|X509Store|StoreName\.|StoreLocation\.|CertMgr" -- '*Tests*/*.cs'

An empty result is the passing state. The pattern covers the browser drivers by
name and the certificate store by the API that opens it, which is `X509Store`
together with the `StoreName` and `StoreLocation` enumerations that address it.

Absolute paths are the third rule and the grep for them is a different shape,
because an absolute path has no fixed vocabulary:

    git grep -n -E '"([A-Za-z]:\\\\|/(etc|usr|var|home|opt|Users)/)' -- '*Tests*/*.cs'

An empty result is the passing state here too. The pattern catches a Windows
drive letter and the Unix directories a test has no business naming. It does not
catch every absolute path that could ever be written, and it is not claimed to:
what it catches is the paths somebody actually types when they are in a hurry.

Neither grep is a substitute for the rule. Both are cheap, both fail closed in
the sense that a new occurrence turns the result non-empty, and neither can tell
a test that touches machine state through an API nobody has thought of yet.

## What is not yet true

Both greps above return nothing today, and that is not evidence of compliance.
There is no test project in this repository yet, so the paths they read do not
exist and the empty result is empty for the wrong reason. The greps become
meaningful the moment #4 adds the test project, and putting them in the gate is
#67.

Likewise, "the suite passes on a Linux runner with no display" is a claim this
document cannot yet carry, because there is no suite. What the CI job proves
today is that a build and an empty test run complete, which is a much smaller
statement.
