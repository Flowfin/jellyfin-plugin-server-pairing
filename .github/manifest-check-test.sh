#!/bin/sh
# Watches .github/manifest-check.sh refuse each disagreement it claims to catch.
#
#     sh .github/manifest-check-test.sh
#
# Every case builds a tree from the real one, changes exactly one thing, and
# asserts the verdict. The unchanged tree is the first case, so a run where the
# check refuses everything is a failure here rather than a pass, and each
# refusal is a near-miss: a value that is wrong by one field, of the kind
# somebody actually writes.
#
# Nothing is built and nothing is downloaded. Every comparison the check makes
# reads a tracked file, which is what makes this cheap enough to run on every
# change.

set -eu

here=$(cd "$(dirname "$0")/.." && pwd)
check="${here}/.github/manifest-check.sh"

scratch=$(mktemp -d)
cleanup() { rm -rf "$scratch"; }

passed=0
failed=0

# A tree holding only the files the check reads, which is also the statement of
# what it reads: if this list is short, that is the point.
tree() {
    dest="${scratch}/$1"
    rm -rf "$dest"
    mkdir -p "${dest}/.github" "${dest}/Jellyfin.Plugin.ServerPairing"
    cp "${here}/build.yaml" "${here}/build.net10.0.yaml" "$dest/"
    cp "${here}/Directory.Build.props" "$dest/"
    cp "${here}/.github/abi-floor.sh" "${dest}/.github/"
    cp "${here}/Jellyfin.Plugin.ServerPairing/Plugin.cs" \
        "${here}/Jellyfin.Plugin.ServerPairing/Jellyfin.Plugin.ServerPairing.csproj" \
        "${dest}/Jellyfin.Plugin.ServerPairing/"
    echo "$dest"
}

# Runs the check over a tree and compares the verdict to the one expected.
# "accepts" means exit 0, "refuses" means any non-zero exit.
expect() {
    name=$1
    want=$2
    dir=$3
    shift 3

    set +e
    out=$(sh "$check" --root "$dir" "$@" 2>&1)
    code=$?
    set -e

    got=accepts
    [ "$code" -ne 0 ] && got=refuses

    if [ "$got" = "$want" ]; then
        passed=$((passed + 1))
        printf '  ok    %s (%s, exit %s)\n' "$name" "$got" "$code"
    else
        failed=$((failed + 1))
        printf '  FAIL  %s (expected %s, got %s at exit %s)\n' "$name" "$want" "$got" "$code"
        printf '%s\n' "$out" | sed 's/^/        /'
    fi
}

trap cleanup EXIT INT TERM

echo "manifest-check-test: the tree as it stands"
base=$(tree base)
expect "the tracked tree agrees with itself" accepts "$base"

echo "manifest-check-test: one comparison at a time"

# The framework a manifest claims against the ones the project builds. net8.0 is
# the near-miss: it is a real framework, the template shipped against it, and it
# is one keystroke from the value that is right.
d=$(tree framework)
sed -i 's/^framework: "net9.0"$/framework: "net8.0"/' "${d}/build.yaml"
expect "a manifest claiming a framework the project does not build" refuses "$d"

# The version a manifest publishes against the version the assembly carries.
# This is the case the issue asks for by name, and the near-miss is a manifest
# bumped for a release with the assembly version left behind.
d=$(tree version)
sed -i 's/^version: "0.1.0.0"$/version: "0.2.0.0"/' "${d}/build.yaml"
expect "a manifest version the assembly does not carry" refuses "$d"

# The floor a manifest claims against the package the shipping build compiles
# against. 12.0.0.0 is a floor this repository really holds, in the other
# manifest, so the case is a floor copied from the neighbouring line rather than
# an invented number.
d=$(tree floor-above)
sed -i 's/^targetAbi: "10.11.0.0"$/targetAbi: "12.0.0.0"/' "${d}/build.yaml"
expect "a floor above the package the shipping build uses" refuses "$d"

# The other direction, and the one that shipped. The pin sat at 10.11.9 while
# the manifest promised 10.11.0.0, which the rule above accepts because 10.11.9
# is not older than the floor package; the assembly it produces binds every
# server reference at 10.11.9.0, and a 10.11.0 server admits the package on the
# promise and then refuses every type in it. The fixture is the released state,
# so what this watches refusing is what an operator met.
d=$(tree binds-above-the-floor)
sed -i "s|'net9.0'\">10.11.0<|'net9.0'\">10.11.9<|" "${d}/Directory.Build.props"
expect "a shipping build that binds above the floor the manifest promises" refuses "$d"

# A floor nothing holds a package for. Refused rather than skipped, so a new
# server line cannot pass by being unrecognised.
d=$(tree floor-unknown)
sed -i 's/^targetAbi: "10.11.0.0"$/targetAbi: "10.10.0.0"/' "${d}/build.yaml"
expect "a floor the shared table holds no package for" refuses "$d"

# The plugin identifier in the manifest against the one in the source. One digit
# is changed, because a whole different identifier is the mistake nobody makes.
d=$(tree guid)
sed -i 's/^guid: "130cc961-461b-49fd-8a3e-f9eb46db0716"$/guid: "130cc961-461b-49fd-8a3e-f9eb46db0717"/' "${d}/build.yaml"
expect "a manifest identifier the source does not carry" refuses "$d"

# A target framework the project builds and no manifest claims. Without this
# every comparison still passes, because each of them starts from a manifest,
# and the server line simply ships nothing.
d=$(tree unclaimed-framework)
sed -i 's|<TargetFrameworks>net9.0;net10.0</TargetFrameworks>|<TargetFrameworks>net9.0;net10.0;net11.0</TargetFrameworks>|' \
    "${d}/Jellyfin.Plugin.ServerPairing/Jellyfin.Plugin.ServerPairing.csproj"
expect "a target framework no manifest claims" refuses "$d"

echo "manifest-check-test: the artefact list against a build output"

# With an output holding what both manifests name, the comparison passes.
d=$(tree artefacts-present)
mkdir -p "${scratch}/out-good"
: >"${scratch}/out-good/Jellyfin.Plugin.ServerPairing.dll"
expect "an output holding every artefact named" accepts "$d" --output "${scratch}/out-good"

# With an empty output it refuses, which is the direction .github/package-audit.sh
# deliberately does not cover.
d=$(tree artefacts-absent)
mkdir -p "${scratch}/out-empty"
expect "an output missing an artefact the manifest names" refuses "$d" --output "${scratch}/out-empty"

echo "manifest-check-test: it fails closed on its own inputs"

# A manifest with a field removed rather than changed. A scanner that reads a
# missing field as agreement is worse than one that reads it as disagreement,
# because the tree it passes is the tree nobody wrote.
d=$(tree missing-field)
sed -i '/^targetAbi:/d' "${d}/build.yaml"
expect "a manifest with no floor at all" refuses "$d"

# No manifest to read. The check may not report agreement it never checked.
d=$(tree no-manifest)
rm -f "${d}"/build*.yaml
expect "a tree with no manifest" refuses "$d"

# The floor table gone. Same rule one file over: the mapping is read out of
# .github/abi-floor.sh, so a check that cannot read it has resolved no floor.
d=$(tree no-floor-table)
: >"${d}/.github/abi-floor.sh"
expect "a floor table this check cannot read" refuses "$d"

echo "manifest-check-test: ${passed} passed, ${failed} failed."

if [ "$failed" -gt 0 ]; then
    echo "::error::manifest-check-test: ${failed} case(s) did not get the verdict they expected."
    exit 1
fi
