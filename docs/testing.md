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
worth asserting on. That harness is #29.

What it cannot see is anything the real server does around the plugin. The real
HTTP stack, the real serialiser and the real routing are all absent from it, so
a message the harness accepts is one this plugin's own types accepted rather
than one that survived a round trip through the host. Nothing in this policy
closes that gap. #70 is where it is closed, against a packaged plugin over real
HTTP, and until that lands the gap is open and the limit is stated wherever a
test leans on it.

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

An empty result from these three is worth nothing on its own. A grep whose
pathspec matches no file prints nothing and exits 1, which is byte for byte what
a clean test project prints, so the input is counted before the result is read:

```
git ls-files -- '*Tests*'
```

Run at `d070084`, that names five files:

```
Jellyfin.Plugin.ServerPairing.Tests/Jellyfin.Plugin.ServerPairing.Tests.csproj
Jellyfin.Plugin.ServerPairing.Tests/PluginIdentityTests.cs
Jellyfin.Plugin.ServerPairing.Tests/ServiceRegistrationTests.cs
Jellyfin.Plugin.ServerPairing.Tests/StaticStateTests.cs
Jellyfin.Plugin.ServerPairing.Tests/packages.lock.json
```

and each of the three greps returns nothing and exits 1 over them. That is the
first run of these commands against a test project rather than against an empty
set. It says the project carries none of the three refused things today. It is a
grep and not a gate: it reads the names in the source and never what the code
does, and nothing refuses a change that reintroduces one of them. #67 is where a
check over this would live.

The pathspec is the fragile part. It matches on the segment `Tests`, so a test
project named without it would take these three greps back to reading an empty
set, silently and permanently green. Renaming the project means rewriting the
pathspec in the same change.

## Where the suite runs

On a Linux CI runner with no display, measured rather than assumed. The
`call / test` job of run 31107829142, at `d070084`:

```
Image: ubuntu-24.04
```

```
Total tests: 9
     Passed: 9
```

No display is attached to that image and no step in the job attaches one. What
this shows is that the nine tests in the tree today run and pass there. It is
not a statement about tests that do not exist yet: nothing in the suite yet
exercises certificate pinning, the two-instance harness or the dashboard page,
so none of the three replacements above has been carried out in code. Each
arrives with the issue that builds the thing it replaces.
