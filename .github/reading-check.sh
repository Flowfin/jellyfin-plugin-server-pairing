#!/bin/sh
# Re-runs every pasted reading in the tracked markdown of this repository and
# refuses one whose output no longer reproduces.
#
# One script rather than a block inside the workflow, so the command a person
# runs and the command CI runs are the same bytes:
#
#     sh .github/reading-check.sh
#     READING_CHECK_REF=<rev> sh .github/reading-check.sh
#
# The documents make their claims by pasting a command naming `origin/master`
# and the output it produced. Nothing read either half back, so a reading went
# stale the moment the code under it moved, and it went stale silently: the
# block still looked like evidence, and the only thing that said otherwise was
# somebody choosing to re-run it. Thirty-three were repaired by hand across two
# passes on issue #80, and every merge that staled one was green.
#
# WHAT IT JUDGES AT IS THE COMMIT IN FRONT OF IT, NOT `origin/master`, AND THAT
# IS THE POINT. A document claims a fact about the mainline. A branch is a
# proposed mainline, so the question worth asking at a branch is whether the
# reading still reproduces once this change IS the mainline. Judging at
# `origin/master` would pass the branch that stales a reading and red afterwards
# on somebody else's push, which is the failure this exists to move earlier. So
# the `origin/master` in the command is substituted with the commit being
# judged, and that commit is substituted back out of the output before the
# comparison, leaving the bytes the document pastes on both sides of it.
#
# The comparison is exact bytes rather than a judgement, which is what makes
# this a check rather than a review note.
#
# Two blocks are not that claim. Both declare themselves in the tree, on the
# line above the block, rather than by a flag on a command line:
#
#     <!-- reading-check: historical -->   quotes an older commit on purpose
#     <!-- reading-check: no-output -->    pastes the command as a pointer only
#
# Each fails closed in the other direction as well. A marker naming no block is
# refused as dangling, a `no-output` block that has since been given an output
# is refused, and a `no-output` command that has stopped answering anything is
# refused, because a pointer that points nowhere is not a pointer.
#
# WHAT MAY BE RUN OUT OF A DOCUMENT IS A CLOSED LIST. A command line in a
# document is text somebody wrote, so this runs `git`, optionally piped into a
# reader, optionally followed by the exit-code echo the corpus uses, and nothing
# else. A reading outside that vocabulary is REFUSED rather than skipped, so the
# list cannot quietly become the reason a reading goes unchecked.
#
# Fail closed on the population too. A run that found no markdown, or found
# markdown carrying no reading at all, is a broken scanner rather than a clean
# tree, and it exits non-zero saying so.
#
# WHAT IT DOES NOT READ, stated so an empty result is not read as every reading
# in the tree reproducing. A reading pointed at anything other than
# `origin/master` - a working tree, a Jellyfin tag, an older commit named by its
# hash - is outside this walk; those are the same claim read a different way,
# and `9c8dedd` repaired three of them by hand. A command broken across lines
# with a trailing backslash is refused rather than reassembled. A fenced block
# is read at column zero, so a reading indented inside a list item is not seen.

set -eu

root=$(git rev-parse --show-toplevel)
cd "$root"

ref=${READING_CHECK_REF:-HEAD}
sha=$(git rev-parse --verify --quiet "$ref^{commit}" || true)
if [ -z "$sha" ]; then
    echo "::error::reading-check cannot resolve '$ref' to a commit. Refusing to report a clean tree."
    exit 1
fi

# The documents are read out of the commit rather than out of the working tree,
# for the reason the rule about claims gives: what a reader will have is the
# committed bytes. It also puts the same kind of thing on both sides of the
# comparison, since the output side comes out of git as well, so a checkout that
# rewrote the line endings cannot make a reading look stale.
files=$(git ls-tree -r --name-only "$sha" | sed -n '/\.md$/p')
if [ -z "$files" ]; then
    echo "::error::reading-check found no markdown at $ref. Refusing to report a clean tree."
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT INT TERM

# One record per reading, written as a pair of files rather than as a stream,
# because an expected output is many lines and carrying it through a shell
# variable would lose the trailing newline the comparison is about. The record
# carries the verdict on the command as well, so the walk over the document and
# the judgement of what may be run happen in the one pass rather than in a
# process per reading.
extract() {
    git show "$sha:$1" | awk -v out="$work" -v file="$1" -v sha="$sha" '
    # The command with every quoted span replaced by one Q, so the shell
    # constructs this check refuses are looked for where they would act rather
    # than inside a pattern that merely contains the character. A pipe, a
    # backtick and a semicolon all appear inside the quoted arguments of this
    # corpus. An unbalanced quote answers "?" and is refused below.
    function skeleton(s,   i, c, q, o) {
        q = ""; o = ""
        for (i = 1; i <= length(s); i++) {
            c = substr(s, i, 1)
            if (q == "") {
                if (c == "\"" || c == "'"'"'") { q = c; o = o "Q" } else { o = o c }
            } else if (c == q) { q = "" }
        }
        return q == "" ? o : "?"
    }
    function verdict(s,   k, stage) {
        if (s == "?")                   return "carries an unbalanced quote"
        if (s !~ /^git /)               return "does not start with git"
        if (s ~ /[$][(]|`|>|<|&|;/)     return "carries a shell construct this check will not run"
        if (s ~ /\\$/)                  return "is broken across lines and cannot be reassembled"
        k = s
        while (index(k, "|") > 0) {
            k = substr(k, index(k, "|") + 1)
            sub(/^[[:space:]]+/, "", k)
            stage = k
            sub(/[[:space:]].*$/, "", stage)
            sub(/[|].*$/, "", stage)
            if (stage !~ /^(wc|grep|sed|sort|head|tail|cut|tr|uniq|awk)$/) {
                return "pipes into " stage ", which is not one of the readers this check will run"
            }
        }
        return "ok"
    }
    function runnable(s,   o, p) {
        o = ""
        while ((p = index(s, "origin/master")) > 0) {
            o = o substr(s, 1, p - 1) sha
            s = substr(s, p + length("origin/master"))
        }
        return o s
    }
    function flush(   i, fh, body, tail) {
        if (cmd == "") return
        while (nout > 0 && expect[nout] ~ /^[[:space:]]*$/) { nout-- }
        n++
        fh = out "/" n
        body = cmd
        tail = "; echo \"exit=$?\""
        if (substr(body, length(body) - length(tail) + 1) == tail) {
            body = substr(body, 1, length(body) - length(tail))
        }
        print file          > (fh ".meta")
        print cline         > (fh ".meta")
        print mark          > (fh ".meta")
        print verdict(skeleton(body)) > (fh ".meta")
        print runnable(cmd) > (fh ".meta")
        print cmd           > (fh ".meta")
        close(fh ".meta")
        printf "" > (fh ".expect")
        for (i = 1; i <= nout; i++) { print expect[i] > (fh ".expect") }
        close(fh ".expect")
        cmd = ""; nout = 0
    }
    function dangle() {
        if (pending == "-") return
        print file, pline, pending > (out "/dangling")
        pending = "-"
    }
    BEGIN { n = 0; cmd = ""; nout = 0; mark = "-"; pending = "-"; fence = 0; indented = 0 }

    /^(```|~~~)/ {
        if (fence) { flush(); fence = 0; mark = "-" }
        else       { fence = 1; mark = pending; pending = "-" }
        next
    }

    fence {
        if ($0 ~ /^git .*origin\/master/) { flush(); cmd = $0; cline = FNR; next }
        if (cmd != "") { expect[++nout] = $0 }
        next
    }

    # Outside a fence, a run of four-space-indented lines is a block. A blank
    # line inside such a run does not end it; any other unindented line does.
    /^    / {
        if (!indented) { indented = 1; mark = pending; pending = "-" }
        stripped = substr($0, 5)
        if (stripped ~ /^git .*origin\/master/) { flush(); cmd = stripped; cline = FNR; next }
        if (cmd != "") { expect[++nout] = stripped }
        next
    }

    /^[[:space:]]*$/ {
        if (indented && cmd != "") { expect[++nout] = "" }
        next
    }

    {
        if (indented) { flush(); indented = 0; mark = "-" }
        if ($0 ~ /^<!-- reading-check: [a-z-]+ -->[[:space:]]*$/) {
            dangle()
            pending = $0
            sub(/^<!-- reading-check: /, "", pending)
            sub(/ -->[[:space:]]*$/, "", pending)
            pline = FNR
            next
        }
        dangle()
    }

    END { flush(); dangle(); print n > (out "/count") }
    '
}

total=0
reproduced=0
historical=0
pointers=0
failed=0

for f in $files; do
    rm -f "$work"/*.meta "$work"/*.expect "$work"/count "$work"/dangling
    extract "$f"
    count=$(cat "$work/count")
    i=0
    while [ "$i" -lt "$count" ]; do
        i=$((i + 1))
        {
            IFS= read -r file
            IFS= read -r line
            IFS= read -r marker
            IFS= read -r verdict
            IFS= read -r runnable
            IFS= read -r cmd
        } <"$work/$i.meta"
        total=$((total + 1))

        if [ "$marker" = "historical" ]; then
            historical=$((historical + 1))
            continue
        fi

        if [ "$verdict" != "ok" ]; then
            echo "NOT RE-RUNNABLE  $file:$line  the command $verdict"
            echo "    $cmd"
            failed=$((failed + 1))
            continue
        fi

        set +e
        sh -c "$runnable" >"$work/actual.raw" 2>&1
        set -e
        sed "s@$sha@origin/master@g" "$work/actual.raw" >"$work/actual"

        if [ "$marker" = "no-output" ]; then
            pointers=$((pointers + 1))
            if [ -s "$work/$i.expect" ]; then
                echo "MARKER STALE  $file:$line  declared no-output and an output is pasted under it"
                failed=$((failed + 1))
            elif [ ! -s "$work/actual" ]; then
                echo "POINTER GONE  $file:$line  declared no-output and the command now answers nothing"
                echo "    $cmd"
                failed=$((failed + 1))
            fi
            continue
        fi

        if cmp -s "$work/$i.expect" "$work/actual"; then
            reproduced=$((reproduced + 1))
        else
            echo "STALE  $file:$line  the pasted output no longer reproduces at $ref"
            echo "    $cmd"
            diff -u "$work/$i.expect" "$work/actual" | sed -e '1,2d' -e 's/^/    /' || true
            failed=$((failed + 1))
        fi
    done

    if [ -f "$work/dangling" ]; then
        while read -r dfile dline dmarker; do
            echo "DANGLING MARKER  $dfile:$dline  '$dmarker' names no block"
            failed=$((failed + 1))
        done <"$work/dangling"
    fi
done

echo "reading-check: $total reading(s) judged at $ref ($sha) - $reproduced reproduced, $historical historical, $pointers pointer(s), $failed bad."

if [ "$total" -eq 0 ]; then
    echo "::error::reading-check found no pasted reading at all. An empty result here is a broken scanner, not a clean tree."
    exit 1
fi

if [ "$failed" -ne 0 ]; then
    echo "::error::$failed pasted reading(s) do not reproduce."
    exit 1
fi
