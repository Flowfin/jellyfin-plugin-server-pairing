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

Decided by the mutation run in #68 and the fuzz run in #69. Neither exists, so
this item is unmet for the first release. When they land, the dispatch is read
from the workflow's own run history for the range since the previous release
tag rather than from anyone's recollection.

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
agreement worth anything. The check that refuses a disagreement is #71 and does
not exist, so until it lands this item is two commands read by a person.

## 6. The threat model and the data statement still describe what the code does

A reading, not a check. Nothing in this repository can decide whether a
document still describes the code, and pretending otherwise would put a tick
next to the item on this list that most deserves attention.

The threat model is [`docs/threat-model.md`](threat-model.md) and it is read in
full against what the release actually does. The personal-data statement is #14
and does not exist yet, so for the first release that half is read against the
`description` field in `build.yaml` and recorded as the weaker thing it is.

[`docs/logging.md`](logging.md) is read the same way, because what a release
writes into a log is part of what it moves.

## 7. The operator guide's walkthrough still matches the page text

A reading, for the same reason. A guide that walks an operator through wording
the dashboard page no longer uses is worse than no guide, because it teaches
somebody to distrust the document while they are holding a credential.

The operator guide is #75 and does not exist yet.

## What this list cannot decide

Items 2, 3, 4 and 7 name work that has not landed, and item 6 names one
document that has not been written. A release cut against this list today would
meet items 1 and 5 and would record the rest as not run, which is the honest
state and not a reason to tick them.

Items 6 and 7 stay readings after everything else lands. They are on the list
in that form deliberately, so that a person reads them before a release instead
of their having been assumed to have stayed true.
