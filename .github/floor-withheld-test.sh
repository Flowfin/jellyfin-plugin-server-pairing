#!/bin/sh
# Watches .github/floor-withheld.sh refuse each thing it claims to refuse.
#
# A check that has never been seen refusing anything is a check nobody knows the shape of. This is
# the arrangement .github/floor-install-test.sh and .github/package-audit-test.sh are in, with the
# same ordering: every case builds its own input, runs the real script, and asserts on the exit code
# and on the sentence, because a script that exits 1 for the wrong reason passes a check that reads
# only the code.
#
# THE NEAR-MISS IS THE ONE THAT MATTERS HERE and it is the expiry arm. The withheld arm passing
# proves only that a script can print; what the leg is for is going red the day the server line it
# waits on has a release. That day cannot be manufactured, so the arm points the script at the
# OTHER manifest in this repository, whose floor derives into an image the registry does publish.
# It is the same code path reaching the same refusal, one manifest over.
#
# WHAT THAT ARM COSTS is a call to the registry, and the script's own third answer is why that does
# not make this flaky: a registry that does not answer is NOT EVALUATED rather than absent. This
# suite reads that state back and reports the arm as not evaluated instead of failed, and says so on
# its last line, so a run that could not ask the question cannot be read as one that asked it and
# was satisfied.
#
# usage: sh .github/floor-withheld-test.sh

set -eu

work=$(mktemp -d)
passed=0
failed=0
unevaluated=0

trap 'rm -rf "$work"' EXIT

refuses() {
    what=$1
    expected=$2
    shift 2

    if sh .github/floor-withheld.sh "$@" >"$work/out" 2>&1; then
        if grep -qF "NOT EVALUATED" "$work/out"; then
            echo "  --  $what (NOT EVALUATED: the registry did not answer, so this arm was not run)"
            sed 's/^/        /' "$work/out"
            unevaluated=$((unevaluated + 1))
            return
        fi
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

# Asserts a pass whose output says the thing the leg exists to say. The assertion is on the line
# naming the image rather than on the verdict, because that line is printed before the registry is
# asked and is therefore the same in all three of the script's answers.
says() {
    what=$1
    expected=$2
    shift 2

    if ! sh .github/floor-withheld.sh "$@" >"$work/out" 2>&1; then
        echo "FAIL  $what (refused, exit non-zero)"
        sed 's/^/        /' "$work/out"
        failed=$((failed + 1))
        return
    fi

    if grep -qF "$expected" "$work/out"; then
        echo "  ok  $what (passes, and names it)"
        passed=$((passed + 1))
    else
        echo "FAIL  $what (passed, but says nothing about $expected)"
        sed 's/^/        /' "$work/out"
        failed=$((failed + 1))
    fi
}

echo "floor-withheld-test: the arms that need no registry"

refuses "a manifest that is not there" "no such manifest" \
    "$work/absent.yaml"

printf 'name: "Server Pairing"\ntargetAbi: "12.0"\n' >"$work/short-abi.yaml"
refuses "a manifest whose targetAbi is not four parts" "declares no four-part targetAbi" \
    "$work/short-abi.yaml"

printf 'name: "Server Pairing"\n' >"$work/no-abi.yaml"
refuses "a manifest with no targetAbi at all" "declares no four-part targetAbi" \
    "$work/no-abi.yaml"

echo "floor-withheld-test: the arms that ask the registry"

refuses "a floor whose image the registry already publishes" "has expired" \
    build.yaml

says "the floor this leg withholds names the image it will use" "jellyfin/jellyfin:12.0.0" \
    build.net10.0.yaml

echo "floor-withheld-test: $passed passed, $failed failed, $unevaluated not evaluated."
test "$failed" -eq 0
