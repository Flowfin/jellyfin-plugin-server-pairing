# Release checklist

A release is the point where everything that was checked separately has to be
true at once. This list is what stops that from depending on memory.

Each item says what decides it. Where a command decides it, the command is
written out and run against the commit being released rather than against a
working tree. Where no machine can decide it, the item says so and names the
document a person reads instead. Where the thing that would decide it has not
been built yet, the item names the issue that builds it, and until that lands
the item is unmet rather than passed. Nothing on this list is satisfied by
having been true at an earlier commit.

No release has been published from this repository yet, so nothing below has
been walked against a real release. That is read rather than remembered:

    gh api repos/Flowfin/jellyfin-plugin-server-pairing/releases --jq 'length'
    0
    git ls-remote --tags origin | wc -l
    0

The last done condition of #79 is that the first one is performed against this
list and that whatever turns out to be missing is added to it in the same
change.

This is where that fact is read from. A document that has a reason to say it
links here rather than carrying its own copy, because the day it stops being
true is a day every copy has to move and only the copies somebody remembers
will.

How a release is actually published, what the publish run refuses on its own and
what it produces, is [`RELEASING.md`](RELEASING.md). This list is what a person
decides before pushing the tag; that document is what happens after. Nothing the
publish run refuses by itself is restated here, because a rule written in two
places is a rule that only moves in one of them.

## 1. The gate is green on the commit being released

Not on an earlier one, and not on a branch head that has since moved. Take the
commit the release is cut from and read its check runs:

    RELEASE_SHA=$(git rev-parse HEAD)
    gh api repos/Flowfin/jellyfin-plugin-server-pairing/commits/$RELEASE_SHA/check-runs --jq '.check_runs[] | "\(.name) \(.status) \(.conclusion)"'

A conclusion of `skipped` is not a pass. It is a job that did not look, and it
reads on the pull request page as a name rather than as an absence, which is
the failure #101 was opened for.

The set that has to be green is the required set on the default branch, which
is a repository setting rather than a file in the tree, so it is read rather
than assumed:

    gh api repos/Flowfin/jellyfin-plugin-server-pairing/rulesets/20464076 --jq '{enforcement, bypass: .bypass_actors, required: [.rules[]|select(.type=="required_status_checks").parameters.required_status_checks[].context]}'

## 2. The cross-version test has been run against the previous protocol version

A release that changes the protocol has to be shown talking to the version
before it, because both sides of a pairing are upgraded by different operators
and never at the same moment.

Decided by the cross-version test in #59. That test does not exist, so for the
first release this item is recorded as not run rather than passed, and it
becomes a command here when #59 lands.

## 3. The mutation run and the fuzz run have been dispatched since the last release

Neither is a gate that stops a merge, which is exactly why they are on this list.
Each carries a weekly schedule and a manual dispatch and nothing else, so a run
can arrive without anybody having asked for one:

    git grep -E '^  (schedule|workflow_dispatch|pull_request|push):' origin/master -- .github/workflows/fuzz.yml .github/workflows/stryker-mutation.yml
    origin/master:.github/workflows/fuzz.yml:  schedule:
    origin/master:.github/workflows/fuzz.yml:  workflow_dispatch:
    origin/master:.github/workflows/stryker-mutation.yml:  schedule:
    origin/master:.github/workflows/stryker-mutation.yml:  workflow_dispatch:

That is why the item is worded as dispatched rather than as started by somebody,
and why it is read out of the run history below rather than out of a memory of
having launched one. A finding from either is triaged before the release, not
left pending, since a pending finding at release is a finding nobody has decided
about.

Both landed after this list was written, so this item is now read rather than
recorded as unmet:

    git ls-tree -r --name-only origin/master -- .github/workflows | grep -E 'stryker-mutation|fuzz'
    .github/workflows/fuzz.yml
    .github/workflows/stryker-mutation.yml

The dispatch is read from each workflow's own run history over the range since
the previous release tag, rather than from anyone's recollection:

    gh run list --workflow=stryker-mutation.yml --limit 5
    gh run list --workflow=fuzz.yml --limit 5

The trigger listing above is what makes this item necessary: with no
`pull_request` among them, neither reports on a change on its way in, and a
release is the moment somebody has to look. There is no previous release tag to
bound the range with, so for the first release the range is the whole history of
each workflow.

## 4. The changelog entry exists and marks any protocol or contract change

A change to the wire protocol or to the consumer contract is the thing an
operator on the other side of a pairing needs to see, so it is marked rather
than left to be inferred from a list of commits.

The changelog and the policy the version number follows are both in the tree:

    git ls-tree -r --name-only origin/master -- CHANGELOG.md docs/versioning.md
    CHANGELOG.md
    docs/versioning.md

So this item is a reading of a file rather than a note about a missing one. What
it asks: the version being released has a heading of its own, spelled the same
way as `version` in `build.yaml`, and every line that changes what the two
servers say to each other carries `[protocol]` while every line that changes the
interface a consumer compiles against carries `[contract]`.
[`../CHANGELOG.md`](../CHANGELOG.md) states that format at its own top and
[`versioning.md`](versioning.md) says what each kind of change does to the
number.

One leg of the hygiene check reads those two markers on the way in:

    git grep -n 'marked_change' origin/master -- .github/pr-hygiene.sh
    origin/master:.github/pr-hygiene.sh:225:marked_change() {
    origin/master:.github/pr-hygiene.sh:254:marked_change protocol "$protocol_paths"
    origin/master:.github/pr-hygiene.sh:255:marked_change contract "$contract_paths"

It refuses a pull request that touches the protocol or the contract and adds no
line carrying the marker for what it touched. That bound now stands in front of a
merge: `Pull request hygiene` is in the required set read by item 1 above, so the
leg stops one. This paragraph said it did not until that setting moved, which is
why item 1 reads the set rather than keeping a copy of it.

What the leg still cannot do is read the entry being released. It asks whether a
marked line was added, never whether that line says what the change did, so the
entry in front of a release is still read by a person.

A pull request may also declare in its body that a change inside those paths
moves nothing a peer or a consumer can see, and pass with no line at all. That
widens what reaches a release without an entry, and it does not widen what a
release owes: an entry is read for what the release changed rather than for what
the guard accepted, and a declaration is a sentence in a pull request that no
release note is assembled from.

`build.yaml` carries a `changelog` field and it holds the first release entry
rather than the placeholder it used to:

    git show origin/master:build.yaml | grep -n -A2 'changelog'
    28:changelog: >
    29-  The first release. A server that installs it answers on the five pairing paths
    30-  the specification fixes and refuses every request that reaches them. Nothing

`build.net10.0.yaml` carries the same field with the same words, and both take
the entry from the changelog rather than a second wording.
[`RELEASING.md`](RELEASING.md) fixes the order between writing the entry and
moving the number, and that order is not restated here.

## 5. The manifest, the assembly version and the changelog agree

A manifest that disagrees with the build produces a plugin that installs and
does not load.

    grep -n '^version:' build.yaml
    grep -n '<Version>\|<AssemblyVersion>\|<FileVersion>' Directory.Build.props

Both read `0.1.0.0`, which is the number the first release carries, written into
them ahead of the tag because that is the order [`RELEASING.md`](RELEASING.md)
fixes. No release has been published, so the agreement has never been tested by
one.

The comparison is no longer only those two greps. A script that reads every
manifest against the build it describes is in the tree, and a workflow runs it:

    git grep -l manifest-check origin/master -- .github/workflows
    origin/master:.github/workflows/gate.yml

It runs in the `Build and test` job, ahead of the restore, because every value it
reads comes out of a tracked file and needs no SDK. Its own cases run beside it
in the same job, so a change that quietly stops it refusing anything reds that
job rather than passing it.

Which pull requests that job sees is the workflow's own filter rather than a
reading of it, and it is narrower than all of them. The packaging job carries the
same filter, so both are read here and neither document holds a second copy:

    git grep -A2 '^  pull_request:' origin/master -- .github/workflows/gate.yml .github/workflows/package.yml
    origin/master:.github/workflows/gate.yml:  pull_request:
    origin/master:.github/workflows/gate.yml-    branches:
    origin/master:.github/workflows/gate.yml-      - master
    --
    origin/master:.github/workflows/package.yml:  pull_request:
    origin/master:.github/workflows/package.yml-    branches:
    origin/master:.github/workflows/package.yml-      - master

A pull request that targets any other branch runs neither, so a change arriving
at `master` through an intermediate branch is read by these two on its last step
and not before. Other guards in the same directory take every branch instead and
each says so in its own file, which is what makes this a filter somebody chose
rather than one nobody noticed.

That is a change of kind for this item and not only of wording. A manifest
naming a framework the project does not build, a version the assembly does not
carry, a floor above the package the build compiles at, or an identifier the
source does not hold, is now refused on the way in rather than caught by
somebody remembering to look. Whether the refusal stands in front of a merge is
the required set, read by the command in item 1 rather than assumed here.

Running it before a tag is still worth the second it costs, because the one
comparison the job cannot make is the one a release most needs:

    sh .github/manifest-check.sh
    ./build.net10.0.yaml: version 0.1.0.0, floor 12.0.0.0 on net10.0, 1 artefact(s)
    ./build.yaml: version 0.1.0.0, floor 10.11.0.0 on net9.0, 1 artefact(s)
    manifest-check: 10 comparison(s), 0 disagreement(s). The artefact list was not compared: no --output was given.

The last line is the bound and the script prints it on every run. The artefact
list is compared only against a build output, which that job does not produce,
so a manifest naming an assembly the package does not hold walks past it there.
Reading it here, against the output the release actually builds, is what covers
that.

Raising a `targetAbi` is the one change here that is not a reading. The floor is
a promise to load on a server that old, and the only thing standing behind it is
the `ABI floor build` job compiling this source against the package that carries
that floor. So a raised floor is released only once that job is green at the new
value, which means the mapping in `.github/abi-floor.sh` has been moved to the
new package in the same change:

    sh .github/abi-floor.sh

Lowering one is the same rule pointing the other way and is the harder case: it
claims support for a server nobody has compiled against yet, and the job is what
turns that claim into something that has been tried.

## 6. The threat model and the data statement still describe what the code does

A reading, not a check. Nothing in this repository can decide whether a
document still describes the code, and pretending otherwise would put a tick
next to the item on this list that most deserves attention.

The threat model is [`docs/threat-model.md`](threat-model.md) and it is read in
full against what the release actually does. The personal-data statement landed
under #14 and is read the same way:

    git ls-tree -r --name-only origin/master -- docs/data.md
    docs/data.md

Its own opening says nothing it describes has ever moved between two servers, so
the reading before the first release is whether that is still the truthful
sentence, rather than whether the field list matches an observed transfer.

[`docs/logging.md`](logging.md) is read the same way, because what a release
writes into a log is part of what it moves.

## 7. The operator guide's walkthrough still matches the page text

A reading, for the same reason. A guide that walks an operator through wording
the dashboard page no longer uses is worse than no guide, because it teaches
somebody to distrust the document while they are holding a credential.

The operator guide is #75 and does not exist yet.

## 8. The interoperability matrix is green on both server lines

This plugin has to work alone and to work with every other supported sibling
plugin installed at the same time. A clash over a route, a scheduled task name
or a configuration key is invisible to a suite that loads one plugin on its own,
and a release is where somebody else's installation meets it first.

Decided by the matrix in #81. That harness does not exist, so for the first
release this item is recorded as not run rather than passed, and it becomes a
command here when #81 lands.

What the item asks once it can be read: both runs come up without startup
errors on both server lines, answer their routes, and pass a collision scan
over routes, task names and configuration keys. A red matrix stops the release
until the collision is fixed or the incompatibility is written down as a known
limitation with its reason, because shipping over a known clash moves the
failure onto an operator who cannot see it coming.

## What this list cannot decide

Items 2, 7 and 8 name work that has not landed: the cross-version test in #59,
the operator guide in #75 and the interoperability matrix in #81 are all open. A
release cut against this list today would read items 1, 3, 4, 5 and 6, and would
record those three as not run, which is the honest state and not a reason to
tick them.

Items 3, 4 and 6 were in that unmet list when this document was written and are
not any more. Each moved because the thing it names landed, so the sentence that
described it as absent had to move with it rather than be left to be discovered
by whoever cuts the first release.

Items 6 and 7 stay readings after everything else lands. They are on the list
in that form deliberately, so that a person reads them before a release instead
of their having been assumed to have stayed true.
