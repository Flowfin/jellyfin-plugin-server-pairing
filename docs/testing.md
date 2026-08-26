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

Nothing writes outside a per-test temporary directory. The key store is what
that rule is written for and there is none in the tree yet, so it is what a
store test will be held to rather than something the suite does today: the path
is one the test chose, never a real application data directory.

Nothing reads the wall clock directly. Every expiry, every skew window and every
rotation overlap is judged at an instant its caller hands in, so a test that
depends on time is deterministic rather than slow and occasionally red. There is
no clock to inject: the source guard leaves a type that wants the time with
nowhere to get it except its own argument, which is the stronger of the two
shapes because a parameter cannot be bypassed by one careless call.

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

First run at `d070084`, over a test project of five files. Counted again on a
clean checkout of `a1ebd36`, which reads the index rather than a commit:

```
git ls-files -- '*Tests*' | wc -l
27
```

and each of the three greps returns nothing and exits 1 over those twenty-seven.
That is what makes the empty results above worth reading: the pathspec matched a
project rather than nothing.

The count is re-run rather than carried over on purpose. What stood here was the
five-file listing from `d070084` and the sentence that the project carries none
of the three refused things today, and those two do not go together: the reading
was of a tree twenty-two files smaller than the one a reader has, and a
present-tense sentence resting on it is the exact defect the paragraph above it
argues against.

It is a grep and not a gate: it reads the names in the source and never what the
code does, and nothing refuses a change that reintroduces one of them. #67 is
where a check over this would live.

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
this shows is that the nine tests in the tree at `d070084` ran and passed there.

The suite has grown a long way past nine since, so read the count as a
measurement of that run rather than of the tree. Counted at `34766d0`, which is
the commit this paragraph was last read at rather than whatever the reader has:

```
git ls-tree -r --name-only 34766d0 | grep -c Tests
25
```

That is files rather than tests, and it is not the same measurement. It also
moves whenever a test file lands, so a reader finding a different number has
found a later tree rather than an error, and the number that matters here is the
nine. What the job reports today is not re-read here, so the sentence above is a
statement about `d070084` and about nothing later.

None of that changes what the run shows about the runner, which is what this
section is for.

Of the three replacements above, one has been partly carried out and two have
not. Nothing generates a certificate in memory and hands it to pinning code:

```
git grep -nE "CertificateRequest|X509" -- '*Tests*' ; echo "exit=$?"
exit=1
```

The two-instance harness is #29, which is open, and there is nothing here that
runs a full enrolment, a rotation and a revocation in one test process.

The dashboard replacement has two halves and only the first of them exists. The
page is read as text by `ConfigurationPageTests`, which refuses a reference to
an external host in it, so that half is carried out. This paragraph said the controller-level
tests for every endpoint behind the page had no controller to be about, and
pasted a reading with no controller in it. There is one:

```
git grep -l "ControllerBase" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs
exit=0
```

That command also read a working tree rather than the mainline, which is what
this document tells everybody else not to do, and it names `origin/master` now.

The half that has not changed is the half the sentence was about. The controller
that exists serves the peer plane, which a peer reaches and the dashboard page
does not, and there is no controller for the administrative endpoints behind
that page. `PeerPlaneControllerTests` is the controller-level suite for the plane
and there is nothing of that shape for the page, because the endpoints it would
call do not exist.

Each of the rest arrives with the issue that builds the thing it replaces.
