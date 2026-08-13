#!/bin/sh
# Proves .github/vulnerable-check.sh refuses what it says it refuses.
#
#     sh .github/vulnerable-check-test.sh
#
# A check nobody has watched refuse something is a check nobody knows the state
# of, so every rule in that script has a case here that goes red without it and
# a case that passes with it.
#
# The fixtures are listings written here rather than produced by the SDK. A case
# that ran `dotnet list` against this tree would prove the state of the tree on
# the day it ran, which is the opposite of proving the guard: this repository's
# graph carries no advisory today, so the refusing case could never fire from
# it.

set -eu

script=$(cd "$(dirname "$0")" && pwd)/vulnerable-check.sh
if [ ! -f "$script" ]; then
    echo "cannot find vulnerable-check.sh next to this test"
    exit 1
fi

work=$(mktemp -d)
clean_up() {
    rm -rf "$work"
}

failures=0

expect_refusal() {
    name=$1
    file=$2
    if sh "$script" "$file" >"${work}/out" 2>&1; then
        echo "FAIL  ${name}: passed and should have refused"
        sed 's/^/      /' "${work}/out"
        failures=$((failures + 1))
    else
        echo "ok    ${name}"
    fi
}

expect_pass() {
    name=$1
    file=$2
    if sh "$script" "$file" >"${work}/out" 2>&1; then
        echo "ok    ${name}"
    else
        echo "FAIL  ${name}: refused and should have passed"
        sed 's/^/      /' "${work}/out"
        failures=$((failures + 1))
    fi
}

# A listing the SDK writes when every project is clean. This is the only shape
# that may pass.
cat >"${work}/clean.txt" <<'LISTING'
  Determining projects to restore...
  All projects are up-to-date for restore.

The following sources were used:
   https://api.nuget.org/v3/index.json

The given project `Example` has no vulnerable packages given the current sources.
The given project `Example.Tests` has no vulnerable packages given the current sources.
LISTING
expect_pass "a listing with every project clean passes" "${work}/clean.txt"

# The refusal this check exists for. Note the exit code the SDK sets on this
# listing is 0, which is why the phrase is what is read.
cat >"${work}/vulnerable.txt" <<'LISTING'
The given project `Example` has the following vulnerable packages
   [net9.0]:
   Top-level Package      Requested   Resolved   Severity   Advisory URL
   > Some.Package         1.0.0       1.0.0      High       https://github.com/advisories/GHSA-0000-0000-0000

The given project `Example.Tests` has no vulnerable packages given the current sources.
LISTING
expect_refusal "a listing carrying one vulnerable project is refused" "${work}/vulnerable.txt"

# The near-miss worth spending the effort on: a listing that says nothing in
# either direction. That is what an SDK printing another language produces, and
# what a command that failed and wrote its error somewhere else produces. Keying
# only on the bad phrase would pass both.
cat >"${work}/silent.txt" <<'LISTING'
  Determining projects to restore...
  All projects are up-to-date for restore.
LISTING
expect_refusal "a listing naming no project in either direction is refused" "${work}/silent.txt"

# An empty listing is the same case one step further, and is the shape a
# redirected command that produced nothing at all leaves behind.
: >"${work}/empty.txt"
expect_refusal "an empty listing is refused" "${work}/empty.txt"

# A listing that is not there at all, which is what a mistyped path in the
# workflow produces.
expect_refusal "a missing listing is refused" "${work}/absent.txt"

# The listing may arrive on standard input, and the same judgement applies.
if printf 'The given project `Example` has the following vulnerable packages\n' | sh "$script" - >"${work}/out" 2>&1; then
    echo "FAIL  a vulnerable listing on standard input: passed and should have refused"
    failures=$((failures + 1))
else
    echo "ok    a vulnerable listing on standard input is refused"
fi

clean_up

if [ "$failures" -gt 0 ]; then
    echo "${failures} case(s) did not behave as the script says they do"
    exit 1
fi

echo "vulnerable-check: every rule watched refusing and passing"
