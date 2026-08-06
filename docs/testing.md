# Testing policy

Every test in this repository runs on a plain CI runner with no display
attached, with no elevation, and without touching the machine certificate store
or any other machine-wide trust state.

This is a condition of a test existing here rather than something to retrofit.
The alternative is a suite only its author can run, which is a suite that
quietly stops running the day a runner image changes and that nobody notices has
stopped, because a suite that does not run looks exactly like a suite that
passed.

## Three kinds of test that are refused, each with its replacement

### A test that installs a certificate authority into the machine trust store

Refused. It needs elevation, it changes state outside the checkout, and it
leaves that state behind on a developer machine.

Replaced by generating a certificate in memory with `CertificateRequest`,
handing it to the pinning code directly, and driving the HTTP layer through an
injected `HttpMessageHandler`. No real handshake happens and no trust store is
consulted, so the thing under test is the pinning decision rather than the
platform's TLS stack.

### A test that boots two real Jellyfin servers and pairs them

Refused. It needs two server installations, a network, and a fixture that ages
out with every server release.

Replaced by an in-process harness that constructs two sets of this plugin's own
services and connects them over an in-memory transport. A full enrolment, a
rotation and a revocation run inside one test process, which is the behaviour
worth asserting on. What that harness cannot see is anything the real server
does around the plugin, and that limit is stated wherever a test leans on it.

### A test that drives the dashboard page in a browser

Refused. It needs a display or a headless browser binary, and it is the slowest
and least stable thing in any suite that has one.

Replaced by asserting on the embedded page as text, for the structure the
endpoints depend on, together with controller level tests for every endpoint
behind that page. What this does not cover is whether the page renders, and that
is a reading rather than a test.

## Two rules that apply to every test

Nothing writes outside a per-test temporary directory. The key store is
therefore tested against an injected path and never against a real application
data directory.

Nothing reads the wall clock directly. Every expiry, every skew window and every
rotation overlap is tested against an injected clock, so a test that depends on
time is deterministic rather than slow and occasionally red.

## The greps that check this

Run from the repository root, over the test project only:

```
git grep -n -i -E "Selenium|Playwright|Puppeteer|WebDriver|ChromeDriver" -- '*Tests*'
git grep -n -E "X509Store|StoreName\.|StoreLocation\.|dev-certs" -- '*Tests*'
git grep -n -E "\"[A-Za-z]:\\\\|\"/(etc|usr|var|home|opt)/" -- '*Tests*'
```

Each is expected to return nothing. The first refuses a browser driver, the
second refuses any machine trust store API, and the third refuses an absolute
path outside a temporary directory, on either platform.

These three greps currently match nothing because there is no test project in
the tree for them to read. That is an empty result from an empty input and not a
passing check, and this document does not claim otherwise. They become evidence
on the day the test project exists, and the issue that adds it is the place
their first real run belongs.

## Where the suite runs

On a Linux CI runner with no display. That the suite passes there is not
something this document can assert yet, for the same reason: there is no suite.
