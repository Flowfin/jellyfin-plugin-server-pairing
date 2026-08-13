#!/bin/sh
# Reads the SDK's own vulnerability listing and refuses a build that carries a
# vulnerable package.
#
#     dotnet list <solution> package --vulnerable --include-transitive > listing.txt
#     sh .github/vulnerable-check.sh listing.txt
#
# The listing may also arrive on standard input, with `-` or no argument.
#
# One script rather than a block inside the workflow, so the command a person
# runs and the command CI runs are the same bytes.
# .github/vulnerable-check-test.sh is where each rule below is watched refusing
# something.
#
# Why the phrase rather than the exit code. `dotnet list package --vulnerable`
# exits 0 whether or not it found anything, so a job that trusts the exit code
# passes on a vulnerable graph. The tool says what it found in words, and those
# words are what this reads:
#
#     The given project `X` has the following vulnerable packages
#     The given project `X` has no vulnerable packages given the current sources.
#
# Why an unrecognised listing is also refused. Keying on the bad phrase alone
# means the day the wording changes, or the day the command fails and writes
# nothing, the check stops refusing and says nothing about having stopped. So a
# listing naming no project in either direction is a refusal rather than a pass.
#
# The wording is English because the caller pins DOTNET_CLI_UI_LANGUAGE, and on
# a machine with a different locale the SDK prints the same sentences in that
# language and this script refuses them as unrecognised. That is the fail-closed
# direction and it is the reason the pin is in the workflow rather than assumed.

set -eu

listing=${1:--}

if [ "$listing" = "-" ]; then
    text=$(cat)
elif [ -f "$listing" ]; then
    text=$(cat "$listing")
else
    echo "FAIL  no listing at ${listing}"
    exit 1
fi

found=$(printf '%s\n' "$text" | grep -c 'has the following vulnerable packages' || true)
clean=$(printf '%s\n' "$text" | grep -c 'has no vulnerable packages' || true)

if [ "$found" -gt 0 ]; then
    echo "FAIL  ${found} project(s) carry a vulnerable package"
    printf '%s\n' "$text"
    exit 1
fi

if [ "$clean" -eq 0 ]; then
    echo "FAIL  the listing names no project in either direction, so nothing was judged"
    printf '%s\n' "$text"
    exit 1
fi

echo "ok    ${clean} project(s) listed, none carrying a vulnerable package"
