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
worth asserting on. That harness is #29 and it is in the tree:

```
git grep -n 'internal sealed class PairedInstances' origin/master -- Jellyfin.Plugin.ServerPairing.Tests/
origin/master:Jellyfin.Plugin.ServerPairing.Tests/Harness/PairedInstances.cs:48:internal sealed class PairedInstances : IDisposable
```

WHAT IT RUNS TODAY IS NOT THAT SENTENCE, and the paragraph above states the
target rather than the state. No enrolment, no rotation and no revocation runs
through it, for the reason the section below this one gives, and the harness
seeds the key an enrolment would have produced instead of producing one. What it
does carry is a signed message from one side reaching the other side's
controller and verifying there, and the four things a case may do to a message
on the way - drop it, delay it, duplicate it, corrupt it.

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

First run at `d070084`, over a test project of five files, and again on a clean
checkout of `a1ebd36`, which answered 27. Neither is what a reader has. Counted
at `c0532599b0dc8ce1debced4637eadb9bc04d85bb`, which reads the index rather than
a commit:

```
git ls-files -- '*Tests*' | wc -l
48
```

and each of the three greps returns nothing and exits 1 over those forty-eight.
That is what makes the empty results above worth reading: the pathspec matched a
project rather than nothing.

The count is re-run rather than carried over on purpose, AND IT HAD BEEN CARRIED
OVER AGAIN. What stood here first was the five-file listing from `d070084` beside
a present-tense sentence; the paragraph that replaced it said so and pasted 27,
and the tree left that behind in its turn, by twenty-one files. So the paragraph
warning against a stale count went stale, which is the argument for the count
being derived rather than written down: it stops being true on the next commit
that adds a file the pathspec matches, and nothing in this tree says when it has.

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

Of the three replacements above, two have been partly carried out and one has
not. Nothing generates a certificate in memory and hands it to pinning code:

```
git grep -nE "CertificateRequest|X509" -- '*Tests*' ; echo "exit=$?"
exit=1
```

The two-instance harness exists and #29 is open, which are two statements
rather than one. What the harness gives a case is two sides that share nothing,
a clock each, a key store each, and a message crossing between them that the
receiving side's own controller reads. THERE IS STILL NOTHING HERE THAT RUNS A
FULL ENROLMENT, A ROTATION AND A REVOCATION IN ONE TEST PROCESS, which is what
#29's first condition asks for, and that sentence is unchanged in what it
denies. The reason is not the harness: nothing in this plugin derives a key
pair, so there is no enrolment to drive, which is #18, and no route ends a
pairing, which is #24.

```
git grep -lni 'ECDiffieHellman' origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
exit=1
```

So a lifecycle driven through the harness starts at a key the harness put into
both stores. That is the state an enrolment would leave behind and it is not an
enrolment, so nothing driven through it says anything about how two servers come
to share a key.

The dashboard replacement has two halves and only the first of them exists. The
page is read as text by `ConfigurationPageTests`, which refuses a reference to
an external host in it, so that half is carried out. This paragraph said the controller-level
tests for every endpoint behind the page had no controller to be about, and
pasted a reading with no controller in it. There is one:

```
git grep -l "ControllerBase" origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs
origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs
exit=0
```

That command also read a working tree rather than the mainline, which is what
this document tells everybody else not to do, and it names `origin/master` now.

THIS PARAGRAPH SAID THERE IS NO CONTROLLER FOR THE ADMINISTRATIVE ENDPOINTS
BEHIND THAT PAGE, and there is one: the second file in the reading above is the
administrative plane, which is issue #289. `AdministrativePlaneControllerTests`
is the controller-level suite for it, beside `PeerPlaneControllerTests` for the
other plane.

What that does not do is make this replacement carried out. The plane holds one
action and it is a read; the endpoints the page is built around - opening a
window, confirming a ceremony, revoking, editing a mapping, rendering the states
- do not exist, so there is still no controller-level suite for what the page
would call, because the page would call almost nothing that is there. The page
itself is unchanged and is still the plugin template's.

Each of the rest arrives with the issue that builds the thing it replaces.
