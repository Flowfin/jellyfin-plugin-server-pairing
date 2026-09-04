#!/bin/sh
# Checks the pull request itself rather than the code in it.
#
# One script rather than a block inside the workflow, so the command a person
# runs and the command CI runs are the same bytes:
#
#     PR_BODY="$(gh pr view 123 --json body --jq .body)" \
#     PR_AUTHOR_TYPE=User PR_AUTHOR_ASSOCIATION=OWNER \
#     BASE_SHA=$(git merge-base origin/master HEAD) HEAD_SHA=$(git rev-parse HEAD) \
#     sh .github/pr-hygiene.sh
#
# Everything it judges comes from git objects and from the pull request fields
# handed to it in the environment. Nothing is fetched.
#
# Two tiers, and the difference between them is deliberate.
#
# The refusing tier is for things that are near-certainly wrong: cheap to check,
# cheap to fix, and losing information that cannot be recovered later.
#
# Which rules are in it is not listed here. A list in a comment drifts against
# the code under it, and this one did: it named three while the script refused
# four, for as long as the protocol and contract markers have been in it. The
# run prints one line per rule, CONTRIBUTING.md states them under "What a pull
# request carries", and .github/pr-hygiene-test.sh watches each one refuse
# something.
#
# The annotating tier prints and never fails. A size cap that fails is a size
# cap that gets worked around, and a source change with no test change has
# exceptions that are real and frequent enough that failing on it teaches people
# to add an empty test.
#
# Fail closed on its own inputs. A run that could not walk the commit range, or
# that found no changed file at all, is a broken scanner rather than a clean
# pull request, and it exits non-zero saying so.

set -eu

# How many changed lines are worth a note. Not a limit; nothing refuses it.
LARGE_DIFF_LINES=400

PR_BODY=${PR_BODY:-}
PR_AUTHOR_TYPE=${PR_AUTHOR_TYPE:-User}
PR_AUTHOR_ASSOCIATION=${PR_AUTHOR_ASSOCIATION:-NONE}
BASE_SHA=${BASE_SHA:-}
HEAD_SHA=${HEAD_SHA:-}

if [ -z "$BASE_SHA" ] || [ -z "$HEAD_SHA" ]; then
    echo "::error::pr-hygiene needs BASE_SHA and HEAD_SHA. Refusing to report a clean pull request."
    exit 1
fi

# Everything is judged from the merge base rather than from the base revision
# itself. The base moves while a pull request is open, and comparing two moving
# tips reports the default branch's own commits as this pull request's work.
if ! from=$(git merge-base "$BASE_SHA" "$HEAD_SHA"); then
    echo "::error::Could not find the merge base of ${BASE_SHA} and ${HEAD_SHA}."
    exit 1
fi

# Guarded rather than used inside a `for ... in $(...)` word list, where a
# failing command substitution does not trip `set -e` and the walk would report
# nothing found as nothing wrong.
if ! commits=$(git rev-list --no-merges "$from".."$HEAD_SHA"); then
    echo "::error::Could not walk the commit range ${from}..${HEAD_SHA}."
    exit 1
fi

if ! changed=$(git diff --name-only "$from" "$HEAD_SHA"); then
    echo "::error::Could not diff ${from}..${HEAD_SHA}."
    exit 1
fi

if [ -z "$changed" ]; then
    echo "::error::pr-hygiene found no changed file between ${from} and ${HEAD_SHA}. Refusing to report a clean pull request."
    exit 1
fi

# Who the refusing tier does not apply to, and why. Two skips for two different
# reasons, written here because a skip nobody can see is a hole.
#
# A bot does not write a body; it fills a template from the update it is making,
# and the change it is asking for is named in the diff it carries. Refusing it
# for a missing issue reference asks a machine for a sentence no machine has.
#
# An author from outside this repository is meeting these rules for the first
# time at the moment the check reds, having had no way to read them before
# pushing. That is a wall in front of a contribution rather than a correction,
# and the reference belongs in the body I can edit. Association is GitHub's own
# field, so a fork author cannot claim to be inside the repository by anything
# they write.
#
# Both reasons are collected rather than the second overwriting the first. A bot
# is usually also outside the repository, and printing one reason for a pull
# request that has two says less than is known about why nothing was refused.
refusing=yes
skip_reasons=""

note_skip() {
    refusing=no
    if [ -n "$skip_reasons" ]; then
        skip_reasons="${skip_reasons}, and ${1}"
    else
        skip_reasons="$1"
    fi
}

if [ "$PR_AUTHOR_TYPE" = "Bot" ]; then
    note_skip "the author is a bot, which fills a template rather than writing a body"
fi
case "$PR_AUTHOR_ASSOCIATION" in
    OWNER | MEMBER | COLLABORATOR) ;;
    *) note_skip "the author is from outside this repository and has not been shown these rules" ;;
esac

fail=0

# Refuses: the body says which issue this belongs to. The linkage between a
# change and its issue is the cheapest thing to check and the first thing lost.
if printf '%s' "$PR_BODY" | grep -qE '#[0-9]+'; then
    echo "ok    body references an issue"
else
    echo "FAIL  the pull request body references no issue by number"
    fail=1
fi

# Refuses: every commit subject says which issue it belongs to. Merge commits
# are the server's and carry no author's sentence, so they are not walked.
subjects_without_issue=""
for sha in $commits; do
    subject=$(git show -s --format='%s' "$sha")
    if printf '%s' "$subject" | grep -qE '#[0-9]+'; then
        continue
    fi
    subjects_without_issue="${subjects_without_issue}${sha} ${subject}
"
done
if [ -n "$subjects_without_issue" ]; then
    printf 'FAIL  commit subjects reference no issue:\n%s' "$subjects_without_issue"
    fail=1
else
    echo "ok    every non-merge commit subject references an issue"
fi

# Refuses: a manifest version change arrives with a changelog entry, in both of
# the two places that hold one. A version published with no record of what it
# changed cannot be reconstructed later.
#
# THE TWO ARE NOT ALTERNATIVES, AND THIS RULE TREATED THEM AS ONE UNTIL #345.
# The comment here said the changelog was `CHANGELOG.md` where one exists and
# the manifest's own `changelog` field until it does. That sentence is older
# than `CHANGELOG.md`: once the file existed, the first arm passed on every
# release and the field was never looked at again. They have different readers.
# `CHANGELOG.md` is read by whoever works on this repository; the field is the
# only text an operator browsing a catalogue is shown, and it ships inside the
# package where it cannot be edited afterwards.
#
# What that cost is on the record. `0.1.1.0` published the paragraph written for
# `0.1.0.0`, so the catalogue entry describing the release that repairs the
# floor is the one that says nothing about it, in front of the operator on an
# old server who is the only reader it matters to.
#
# The field is read by taking the lines from `changelog:` to the next key at
# column zero, which is a heuristic over this manifest's flat shape rather than
# YAML parsing; a nested manifest would need a parser and would break this.
#
# WHAT IS COMPARED IS BYTES AND NOT MEANING. This asks whether the field moved
# with the version, never whether the words describe the version they ship
# under, so a bump that rewrites the field into a second wrong paragraph passes
# here. The entry in front of a release is still read by a person, which is what
# docs/release.md already says of the marker leg below and for the same reason.
#
# Only `build.yaml` is read, and the second manifest is covered by assertion
# rather than by hope: ManifestAgreementTests refuses any difference between the
# two files outside `targetAbi` and `framework`, so a field that moved in one
# and not the other reddens the suite instead of passing quietly here.
manifest=build.yaml

changelog_block() {
    git show "$1:$manifest" 2>/dev/null |
        awk '/^changelog:/ { inside = 1 } inside && /^[A-Za-z_-]+:/ && !/^changelog:/ { inside = 0 } inside { print }'
}

manifest_field() {
    git show "$1:$manifest" 2>/dev/null | grep -E "^$2:" || true
}

if [ "$(manifest_field "$from" version)" = "$(manifest_field "$HEAD_SHA" version)" ]; then
    echo "ok    the manifest version is unchanged"
else
    if printf '%s\n' "$changed" | grep -qx 'CHANGELOG.md'; then
        echo "ok    the manifest version changed and CHANGELOG.md changed with it"
    else
        echo "FAIL  the manifest version changed and CHANGELOG.md did not"
        fail=1
    fi

    if [ "$(changelog_block "$from")" != "$(changelog_block "$HEAD_SHA")" ]; then
        echo "ok    the manifest version changed and the manifest changelog field changed with it"
    else
        echo "FAIL  the manifest version changed and the manifest changelog field still describes the version before it"
        fail=1
    fi
fi

# Refuses: a change to the wire protocol or to the consumer contract arrives
# with a changelog line marked as one. The version bump rule above catches what
# a release changes; this catches it at the moment it is written, which is the
# only moment anybody remembers which protocol version it was about.
#
# The two audiences are why the marker exists rather than the entry alone. An
# operator on the far side of a pairing and an author of a plugin built on this
# one both scan for their own kind, and neither reads a list of commits.
# CHANGELOG.md states the markers and docs/versioning.md states what each kind
# does to the version.
#
# Only added lines count. A line already in the file, or one being deleted,
# says nothing about the change in front of it.
#
# The paths are declared here rather than derived, and two of the four do not
# exist in the tree yet: the consumer contract is M6 and this is the guard
# waiting for it. A contract that lands somewhere else walks past this, so the
# issue that creates it moves this list in the same change.
#
# THE PATHS ARE A PROXY FOR THE SUBJECT AND THEY OVER-REFUSE. The subject is a
# change to what the two servers say to each other, or to what a consumer
# compiles against. The proxy is a directory. A change inside that directory
# that alters nothing a peer or a consumer can observe - a loop rewritten to
# close a static-analysis alert, a comment, a rename of something private -
# earns no changelog line, because CHANGELOG.md's own first rule is that a
# change nobody outside this repository can see does not belong in it.
#
# Before the declaration below there were two ways past that, and both were
# worse than the rule. Write a [protocol] line the change does not carry, which
# puts a false claim in the file operators read on the far side of a pairing and
# is the one place a false claim costs most. Or leave the change unmade, which
# is what happened: issue #314's repair sat unmade because the alert it closes
# is in Protocol/ and the repair earns no line.
#
# So a pull request may declare instead, on a line of its own in the body:
#
#     No protocol change: <why the change inside those paths changes no wire>
#
# NOTHING HERE VERIFIES THAT DECLARATION. It is a claim a reader judges, exactly
# as a [protocol] line is a claim a reader judges - this check asks whether a
# marked line was added and never whether it says what the change did. What the
# declaration buys is not a stronger check. It is that the untrue version of it
# stays in the pull request, where it is read once and thrown away, instead of
# landing in CHANGELOG.md, where it is read for as long as the entry stands.
#
# The reason is required and the kind is not interchangeable: a declaration
# naming one kind does nothing for the other, so a change touching both
# declares both or carries the line for what it did change.
protocol_paths='^Jellyfin\.Plugin\.ServerPairing/Protocol/'
contract_paths='^docs/consumer-interface\.md$|^Jellyfin\.Plugin\.ServerPairing/Contract/'

marked_change() {
    kind=$1
    paths=$2

    if ! printf '%s\n' "$changed" | grep -qE "$paths"; then
        echo "ok    nothing in this pull request changes the ${kind}"
        return
    fi

    if ! added=$(git diff --unified=0 "$from" "$HEAD_SHA" -- CHANGELOG.md); then
        echo "::error::Could not read CHANGELOG.md between ${from} and ${HEAD_SHA}."
        exit 1
    fi

    if printf '%s\n' "$added" | grep -q "^+.*\[${kind}\]"; then
        echo "ok    the ${kind} changed and CHANGELOG.md gained a [${kind}] line"
        return
    fi

    if declared=$(printf '%s\n' "$PR_BODY" | grep -E "^No ${kind} change:[[:space:]]*[^[:space:]]"); then
        echo "ok    the ${kind} paths changed and the body declares no ${kind} change"
        printf '%s\n' "$declared" | sed 's/^/      /'
        return
    fi

    echo "FAIL  the ${kind} changed and CHANGELOG.md gained no [${kind}] line, and the body declares no \"No ${kind} change:\" with a reason"
    fail=1
}

marked_change protocol "$protocol_paths"
marked_change contract "$contract_paths"

# Annotates: a large diff. A legitimate change can be large.
if ! numstat=$(git diff --numstat "$from" "$HEAD_SHA"); then
    echo "::error::Could not measure the diff between ${from} and ${HEAD_SHA}."
    exit 1
fi
lines=$(printf '%s\n' "$numstat" | awk '$1 ~ /^[0-9]+$/ && $2 ~ /^[0-9]+$/ { total += $1 + $2 } END { print total + 0 }')
if [ "$lines" -gt "$LARGE_DIFF_LINES" ]; then
    echo "::notice::This pull request changes ${lines} lines, over the ${LARGE_DIFF_LINES} that are comfortable to read. Nothing refuses it."
else
    echo "ok    ${lines} changed lines"
fi

# Annotates: plugin source changed and the test project did not.
if printf '%s\n' "$changed" | grep -q '^Jellyfin\.Plugin\.ServerPairing/' &&
    ! printf '%s\n' "$changed" | grep -q '^Jellyfin\.Plugin\.ServerPairing\.Tests/'; then
    echo "::notice::The plugin source changed and the test project did not. Nothing refuses it; the exceptions are real."
else
    echo "ok    source and test changes are consistent"
fi

if [ "$refusing" = no ]; then
    echo "The refusing tier did not apply: ${skip_reasons}."
    echo "Anything printed as FAIL above is reported and not refused."
    exit 0
fi

if [ "$fail" -ne 0 ]; then
    echo "::error::This pull request fails the hygiene rules above. They are written out in CONTRIBUTING.md under 'What a pull request carries'."
    exit 1
fi

echo "This pull request carries what the refusing tier asks for."
