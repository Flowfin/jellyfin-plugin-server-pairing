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
# The refusing tier is for things that are near-certainly wrong: a body with no
# issue reference, a commit subject with no issue reference, and a manifest
# version change with no changelog entry. Each is cheap to check, cheap to fix,
# and loses information that cannot be recovered later.
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
# and the reference belongs in the body a maintainer can edit. Association is
# GitHub's own field, so a fork author cannot claim to be inside the repository
# by anything they write.
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

# Refuses: a manifest version change arrives with a changelog entry. A version
# published with no record of what it changed cannot be reconstructed later.
#
# The changelog is `CHANGELOG.md` where one exists and the manifest's own
# `changelog` field until it does. The field is read by taking the lines from
# `changelog:` to the next key at column zero, which is a heuristic over this
# manifest's flat shape rather than YAML parsing; a nested manifest would need
# a parser and would break this.
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
elif printf '%s\n' "$changed" | grep -qx 'CHANGELOG.md'; then
    echo "ok    the manifest version changed and CHANGELOG.md changed with it"
elif [ "$(changelog_block "$from")" != "$(changelog_block "$HEAD_SHA")" ]; then
    echo "ok    the manifest version changed and the manifest changelog changed with it"
else
    echo "FAIL  the manifest version changed with no changelog entry"
    fail=1
fi

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
