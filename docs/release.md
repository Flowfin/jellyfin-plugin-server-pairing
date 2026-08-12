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
been walked against a real release. The last done condition of #79 is that the
first one is performed against this list and that whatever turns out to be
missing is added to it in the same change.

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

Both are runs somebody starts rather than gates that stop a merge, which is
exactly why they are on this list. A finding from either is triaged before the
release, not left pending, since a pending finding at release is a finding
nobody has decided about.

Both landed after this list was written, so this item is now read rather than
recorded as unmet:

    git ls-tree -r --name-only origin/master -- .github/workflows | grep -E 'stryker-mutation|fuzz'
    .github/workflows/fuzz.yml
    .github/workflows/stryker-mutation.yml

The dispatch is read from each workflow's own run history over the range since
the previous release tag, rather than from anyone's recollection:

    gh run list --workflow=stryker-mutation.yml --limit 5
    gh run list --workflow=fuzz.yml --limit 5

Neither has a `pull_request` trigger, so neither reports on a change on its way
in and a release is the moment somebody has to look. There is no previous
release tag to bound the range with, so for the first release the range is the
whole history of each workflow.

## 4. The changelog entry exists and marks any protocol or contract change

A change to the wire protocol or to the consumer contract is the thing an
operator on the other side of a pairing needs to see, so it is marked rather
than left to be inferred from a list of commits.

There is no changelog file in this repository:

    ls CHANGELOG.md
    ls: cannot access 'CHANGELOG.md': No such file or directory

`build.yaml` carries a `changelog` field, and it holds a placeholder rather
than an entry:

    grep -n -A2 'changelog' build.yaml

The versioning policy and the changelog are #76, and where the changelog lives
is settled there rather than here.

## 5. The manifest, the assembly version and the changelog agree

A manifest that disagrees with the build produces a plugin that installs and
does not load.

    grep -n '^version:' build.yaml
    grep -n '<Version>\|<AssemblyVersion>\|<FileVersion>' Directory.Build.props

Both currently read `0.0.0.0`, which is the unreleased value rather than an
agreement worth anything.

The comparison is no longer only those two greps. A script that reads every
manifest against the build it describes is in the tree:

    git ls-tree -r --name-only origin/master -- .github/manifest-check.sh
    .github/manifest-check.sh

and nothing in a workflow runs it, so it is a command a person runs before
cutting a release rather than a check that has already refused something on the
way in:

    git grep -l manifest-check origin/master -- .github/workflows ; echo "exit=$?"
    exit=1

    sh .github/manifest-check.sh

Putting it in front of a package is #71, which is open. Until that lands, this
item is the script above plus a reading of its output.

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

## What this list cannot decide

Items 2, 4 and 7 name work that has not landed: the cross-version test in #59,
the changelog in #76 and the operator guide in #75 are all open. A release cut
against this list today would read items 1, 3, 5 and 6, and would record those
three as not run, which is the honest state and not a reason to tick them.

Items 3 and 6 were in that unmet list when this document was written and are not
any more. Both moved because the thing they name landed, so the sentence that
described them as absent had to move with it rather than be left to be
discovered by whoever cuts the first release.

Items 6 and 7 stay readings after everything else lands. They are on the list
in that form deliberately, so that a person reads them before a release instead
of their having been assumed to have stayed true.
