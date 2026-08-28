#!/bin/sh
# Proves .github/reading-check.sh refuses what it says it refuses.
#
#     sh .github/reading-check-test.sh
#
# A check nobody has watched refuse something is a check nobody knows the state
# of, so every rule in that script has a case here that goes red without it and
# a case that passes with it.
#
# The fixtures are throwaway repositories built in a temporary directory rather
# than this one. A case run against this tree would prove the state of the tree
# on the day it ran, which is the opposite of proving the guard: the documents
# here reproduce today, so the refusing case could never fire from them. It also
# lets the same document be shown failing and then passing once the stale line
# is corrected, which is what the issue asks for and what this tree cannot
# stage.
#
# THE FIXTURE COMMITS ARE UNSIGNED AND THAT IS NOT THIS REPOSITORY'S SIGNING
# RULE BEING BYPASSED. A fixture repository lives inside `mktemp -d`, is never a
# remote of anything and is deleted at the end of the run; a runner has no key
# to sign one with, so requiring a signature here would make the test unrunnable
# in the place it has to run. Nothing this script creates reaches a branch.

set -eu

script=$(cd "$(dirname "$0")" && pwd)/reading-check.sh
if [ ! -f "$script" ]; then
    echo "cannot find reading-check.sh next to this test"
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

failures=0
case_number=0

# Builds a repository holding one source file and whatever documents the caller
# writes into it afterwards, and leaves its path in $repo. It sets $repo rather
# than printing it, because a command substitution runs the body in a subshell
# and the case counter would not survive it: every case would then be handed the
# same repository, and the ones that expect an empty tree would be handed a full
# one.
new_repo() {
    case_number=$((case_number + 1))
    repo="$work/repo$case_number"
    mkdir -p "$repo/src"
    git init -q -b main "$repo"
    git -C "$repo" config user.email "reading-check-test@example.invalid"
    git -C "$repo" config user.name "reading-check test"
    git -C "$repo" config commit.gpgsign false
    # The fixtures are compared byte for byte, so a checkout that rewrote the
    # line endings would be deciding the result instead of the guard.
    git -C "$repo" config core.autocrlf false
    printf 'the line this fixture is about\nand a second one\n' >"$repo/src/thing.txt"
}

commit_repo() {
    git -C "$1" add -A
    git -C "$1" commit -q -m "fixture"
}

run_expect() {
    name=$1
    want=$2
    repo=$3
    if (cd "$repo" && sh "$script") >"$work/out" 2>&1; then
        got=pass
    else
        got=refuse
    fi
    if [ "$got" = "$want" ]; then
        echo "ok    $name"
    else
        echo "FAIL  $name: $got, and the script says $want"
        sed 's/^/      /' "$work/out"
        failures=$((failures + 1))
    fi
}

# A reading that still reproduces. This is the case every refusal below has to
# be told apart from, and it is written in a fenced block.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

The line is there:

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:1:the line this fixture is about
```
DOC
commit_repo "$repo"
run_expect "a reading that reproduces passes" pass "$repo"

# The same document with one byte of the pasted output changed. This is the
# refusal the check exists for.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

The line is there:

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:1:the line this fixture was about
```
DOC
commit_repo "$repo"
run_expect "a pasted output that no longer reproduces is refused" refuse "$repo"

# The line number moving is the shape that staled eight blocks at once on this
# board, so it is the near-miss worth spending a case on: the text is right and
# only the position is wrong.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

The line is there:

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:2:the line this fixture is about
```
DOC
commit_repo "$repo"
run_expect "a reading whose line number has moved is refused" refuse "$repo"

# And the same document once the stale line is corrected, which is the second
# half of that condition rather than a repeat of the first case.
sed -i 's/^origin\/master:src\/thing.txt:2:/origin\/master:src\/thing.txt:1:/' "$repo/doc.md"
commit_repo "$repo"
run_expect "the same document passes once the stale line is corrected" pass "$repo"

# A four-space-indented block is the other shape the corpus uses, and roughly
# half of it. A check that read only fences would report a clean tree over it.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

The line is there:

    git grep -n "the line this fixture is about" origin/master -- src/thing.txt
    origin/master:src/thing.txt:9:the line this fixture is about

and that is all.
DOC
commit_repo "$repo"
run_expect "a stale reading in an indented block is refused" refuse "$repo"

# A block declared as a quotation from an older commit is not re-run, even
# though its output does not reproduce.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

What it answered before the rename:

<!-- reading-check: historical -->
```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:4:what it used to say
```
DOC
commit_repo "$repo"
run_expect "a block declared historical is not re-run" pass "$repo"

# The declaration is refused when it names no block, so a marker left behind by
# a deleted block fails rather than sitting there.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

<!-- reading-check: historical -->

The block this marker was written for is gone, and the prose is not a block.

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:1:the line this fixture is about
```
DOC
commit_repo "$repo"
run_expect "a marker naming no block is refused" refuse "$repo"

# A command pasted as a pointer, with its answer deliberately not pasted.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

Where it is:

<!-- reading-check: no-output -->
```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
```
DOC
commit_repo "$repo"
run_expect "a pointer declared no-output passes" pass "$repo"

# The same declaration once somebody pastes an output under it. The marker is
# then a lie about the block, and it is refused rather than silently widened
# into "do not check this one".
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

Where it is:

<!-- reading-check: no-output -->
```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:1:the line this fixture is about
```
DOC
commit_repo "$repo"
run_expect "a no-output block that has been given an output is refused" refuse "$repo"

# A pointer that has stopped pointing anywhere. Without this the marker would
# be a way to make a reading unfalsifiable.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

Where it is:

<!-- reading-check: no-output -->
```
git grep -n "a sentence no file in this fixture holds" origin/master -- src/thing.txt
```
DOC
commit_repo "$repo"
run_expect "a pointer whose command now answers nothing is refused" refuse "$repo"

# A command outside the vocabulary this check will run. It is refused rather
# than skipped, so the vocabulary cannot become the reason a reading goes
# unchecked without anybody seeing it.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

Read from somewhere else:

```
curl -s https://example.invalid/origin/master | head -1
```

And a git command with a redirection in it:

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt > out.txt
```
DOC
commit_repo "$repo"
run_expect "a command outside the vocabulary is refused rather than skipped" refuse "$repo"

# Fail closed on the population. Markdown carrying no reading at all is a
# broken scanner rather than a clean tree.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

It makes no claim and pastes nothing.
DOC
commit_repo "$repo"
run_expect "a tree whose markdown holds no reading is refused" refuse "$repo"

# The same floor one step further: no markdown at all.
new_repo
commit_repo "$repo"
run_expect "a tree with no tracked markdown is refused" refuse "$repo"

# A reference that does not resolve. This is what a shallow checkout with the
# wrong ref name produces, and calling it clean is the failure the floor above
# is the same argument for.
new_repo
cat >"$repo/doc.md" <<'DOC'
# A document

```
git grep -n "the line this fixture is about" origin/master -- src/thing.txt
origin/master:src/thing.txt:1:the line this fixture is about
```
DOC
commit_repo "$repo"
if (cd "$repo" && READING_CHECK_REF=no-such-ref sh "$script") >"$work/out" 2>&1; then
    echo "FAIL  a reference that does not resolve: passed, and the script says it refuses"
    sed 's/^/      /' "$work/out"
    failures=$((failures + 1))
else
    echo "ok    a reference that does not resolve is refused"
fi

if [ "$failures" -gt 0 ]; then
    echo "$failures case(s) did not behave as the script says they do"
    exit 1
fi

echo "reading-check: every rule watched refusing and passing"
