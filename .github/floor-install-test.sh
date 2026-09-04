#!/bin/sh
# Watches .github/floor-install.sh refuse each thing it claims to refuse.
#
# A check that has never been seen refusing anything is a check nobody knows the shape of. This is
# the arrangement .github/package-audit-test.sh is in, for the same reason and with the same
# ordering: every case builds its own input, runs the real script, and asserts on the exit code and
# on the sentence.
#
# THE CASES THAT NEED A SERVER ARE MARKED AND THERE IS ONE. Starting a container, completing a
# wizard and reading an endpoint is the expensive part, so the arms that can be decided before a
# container starts are decided there. The one that cannot is the arm about what the server says,
# and the near-miss chosen for it is the cheaper of the two the script names: a package with no
# assembly in it, which the server never lists at all. The other arm, a package the server lists as
# NotSupported, is what 0.1.0.0 does on this floor, and reproducing it here would mean downloading
# a published release asset on every run - a check that reds when a network call fails says nothing
# about the package under test, so that arm is proved by hand and the reading is in the pull
# request that landed this.
#
# usage: sh .github/floor-install-test.sh [port]

set -eu

port=${1:-18196}
work=$(mktemp -d)
passed=0
failed=0

trap 'rm -rf "$work"' EXIT

# Runs the script and asserts it refused, naming what it refused for. A case that expects a refusal
# and gets a pass is the one that matters, so the assertion is on both the code and the text: a
# script that exits 1 for the wrong reason passes a check that reads only the code.
refuses() {
    what=$1
    expected=$2
    shift 2

    if sh .github/floor-install.sh "$@" >"$work/out" 2>&1; then
        echo "FAIL  $what (passed, exit 0)"
        sed 's/^/        /' "$work/out"
        failed=$((failed + 1))
        return
    fi

    if grep -qF "$expected" "$work/out"; then
        echo "  ok  $what (refuses, exit 1)"
        passed=$((passed + 1))
    else
        echo "FAIL  $what (refused, but for something else)"
        sed 's/^/        /' "$work/out"
        failed=$((failed + 1))
    fi
}

echo "floor-install-test: the arms that need no server"

refuses "an archive that is not there" "no such archive" \
    "$work/absent.zip" build.yaml "$port"

refuses "a manifest that is not there" "no such manifest" \
    build.yaml "$work/absent.yaml" "$port"

printf 'name: "Server Pairing"\ntargetAbi: "10.11"\n' >"$work/short-abi.yaml"
: >"$work/empty.zip"
refuses "a manifest whose targetAbi is not four parts" "declares no four-part targetAbi" \
    "$work/empty.zip" "$work/short-abi.yaml" "$port"

printf 'name: "Server Pairing"\n' >"$work/no-abi.yaml"
refuses "a manifest with no targetAbi at all" "declares no four-part targetAbi" \
    "$work/empty.zip" "$work/no-abi.yaml" "$port"

echo "floor-install-test: the arm that needs a server"

# A package holding a manifest and no assembly. The server has nothing to load, so it lists no
# plugin under that name, and an absent plugin is a different failure from a refused one: a check
# that treated absence as silence would pass an install that put the files somewhere the server
# never looks.
mkdir -p "$work/no-assembly"
printf '{ "name": "Server Pairing", "version": "0.0.0.0", "targetAbi": "10.11.0.0" }\n' \
    >"$work/no-assembly/meta.json"
(cd "$work/no-assembly" && zip -qr ../no-assembly.zip .)

refuses "a package with no assembly in it" "the package was not seen at all" \
    "$work/no-assembly.zip" build.yaml "$port"

echo "floor-install-test: $passed passed, $failed failed."
test "$failed" -eq 0
