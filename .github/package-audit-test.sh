#!/bin/sh
# Proves .github/package-audit.sh refuses what it says it refuses.
#
#     sh .github/package-audit-test.sh
#
# A check nobody has watched refuse something is a check nobody knows the state
# of, so every rule in that script has a case here that goes red without it and
# a case that passes with it.
#
# The fixtures are packages built in a throwaway directory under the temporary
# directory. Nothing here reaches the tree, nothing is pushed, and no fixture is
# the repository's own manifest: a case that judged against build.yaml would
# prove the state of this tree on the day it ran rather than the guard.

set -eu

script=$(cd "$(dirname "$0")" && pwd)/package-audit.sh
if [ ! -f "$script" ]; then
    echo "cannot find package-audit.sh next to this test"
    exit 1
fi

for tool in unzip zip sha256sum; do
    if ! command -v "$tool" >/dev/null 2>&1; then
        echo "package-audit-test needs ${tool} and it is not on this machine"
        exit 1
    fi
done

# Removed at the end rather than from an exit trap, for the reason
# .github/pr-hygiene-test.sh gives. A run that dies before the end leaves its
# fixture directory behind, which is where you look at it.
tmp=$(mktemp -d)
cd "$tmp"

failures=0

# A manifest whose artifact list names one assembly, and which carries a second
# list, under another key, naming a second one. That second list is the
# near-miss: dropping the range from the block reader leaves a one-line reader
# over every list item in the file, which is the mistake somebody makes, and it
# lets Extra.dll through.
manifest() {
    cat >"$1" <<'YAML'
---
name: "Fixture"
version: "0.0.0.0"
overview: "A fixture."
retired:
- "Extra.dll"
artifacts:
- "Plugin.dll"
changelog: >
  Nothing.
YAML
}

# A manifest with the key and no items under it.
manifest_without_items() {
    cat >"$1" <<'YAML'
---
name: "Fixture"
artifacts:
changelog: >
  Nothing.
YAML
}

# Builds a package out of the files named after the first argument.
package() {
    target=$1
    shift
    rm -rf pack
    mkdir pack
    for entry in "$@"; do
        printf 'bytes of %s\n' "$entry" >"pack/$entry"
    done
    (cd pack && zip -q -X "../$target" ./*)
}

run() {
    # Runs the script and captures both streams and the exit status without
    # tripping `set -e`, so a refusal is a result rather than the end of the run.
    status=0
    "$@" >"$tmp/out.txt" 2>&1 || status=$?
    return 0
}

expect_refusal() {
    label=$1
    needle=$2
    if [ "$status" -eq 0 ]; then
        echo "FAIL  ${label}: exited 0, and it should have refused"
        sed 's|^|      |' "$tmp/out.txt"
        failures=$((failures + 1))
        return 0
    fi
    if ! grep -qF "$needle" "$tmp/out.txt"; then
        echo "FAIL  ${label}: refused without saying \"${needle}\""
        sed 's|^|      |' "$tmp/out.txt"
        failures=$((failures + 1))
        return 0
    fi
    echo "ok    ${label}"
}

expect_pass() {
    label=$1
    if [ "$status" -ne 0 ]; then
        echo "FAIL  ${label}: exited ${status}, and it should have passed"
        sed 's|^|      |' "$tmp/out.txt"
        failures=$((failures + 1))
        return 0
    fi
    echo "ok    ${label}"
}

manifest manifest.yaml
manifest_without_items manifest-without-items.yaml

# 1. The package this repository means to ship. One named assembly beside the
#    metadata the packager writes. Nothing to refuse.
package clean.zip Plugin.dll meta.json
run sh "$script" clean.zip manifest.yaml clean.cdx.json
expect_pass "a package holding only the named assembly passes"

# 2. The rule this script exists for. A second assembly, which is what happens
#    the first time a package reference is added that is not the host's own.
package extra.zip Plugin.dll Extra.dll meta.json
run sh "$script" extra.zip manifest.yaml extra.cdx.json
expect_refusal "an assembly the manifest does not name is refused" \
    "Extra.dll is in the package and manifest.yaml does not name it"

# 3. The same package against the same manifest with the offending assembly
#    added to the artifact list. It passes, so case 2 measures the rule rather
#    than something else about that package.
sed 's|^- "Plugin.dll"|- "Plugin.dll"\n- "Extra.dll"|' manifest.yaml >widened.yaml
run sh "$script" extra.zip widened.yaml extra-widened.cdx.json
expect_pass "the same package passes once the manifest names both"

# 4. The near-miss on how the artifact list is read. Extra.dll is a list item in
#    the fixture manifest, under another key, and case 2 refuses it anyway. A
#    reader that took every list item in the file would pass case 2 and prove
#    nothing, so this asserts the fixture still contains the trap.
run grep -qxF -e '- "Extra.dll"' manifest.yaml
expect_pass "the fixture manifest carries Extra.dll as a list item elsewhere"

# 5. A manifest with the key and nothing under it. A scanner with an empty
#    allowed set refuses everything or allows everything; either is wrong, and
#    reporting a clean package is the wrong one.
run sh "$script" clean.zip manifest-without-items.yaml empty-list.cdx.json
expect_refusal "a manifest with no artifact list is refused" \
    "declares no artifacts"

# 6. A package holding no assembly at all. A build that produced metadata and
#    no plugin passes every name comparison, because there is no name to fail.
package metadata-only.zip meta.json
run sh "$script" metadata-only.zip manifest.yaml metadata-only.cdx.json
expect_refusal "a package with no assembly is refused" \
    "holds no assembly"

# 7. A package holding no file at all. One directory entry and nothing in it,
#    which is what an archive built from an empty output directory looks like.
rm -rf hollow
mkdir -p hollow/empty
(cd hollow && zip -q -X -r ../hollow.zip empty)
run sh "$script" hollow.zip manifest.yaml hollow.cdx.json
expect_refusal "a package holding no file is refused" \
    "holds no files"

# 8. Something that is not an archive. An extraction that failed and a package
#    with nothing wrong in it print the same thing to a walk that does not check.
printf 'not an archive\n' >broken.zip
run sh "$script" broken.zip manifest.yaml broken.cdx.json
expect_refusal "a package that cannot be extracted is refused" \
    "Could not extract"

# 9. No package at all.
run sh "$script" absent.zip manifest.yaml absent.cdx.json
expect_refusal "a missing package is refused" "No package at"

# 10. The list is read out of the package rather than out of the manifest. Two
#     packages holding one file name with different bytes produce different
#     digests, and each digest is the one sha256sum gives for those bytes.
rm -rf pack
mkdir pack
printf 'first\n' >pack/Plugin.dll
printf 'meta\n' >pack/meta.json
(cd pack && zip -q -X ../first.zip ./*)
printf 'second\n' >pack/Plugin.dll
rm -f second.zip
(cd pack && zip -q -X ../second.zip ./*)

run sh "$script" first.zip manifest.yaml first.cdx.json
expect_pass "the first package passes"
run sh "$script" second.zip manifest.yaml second.cdx.json
expect_pass "the second package passes"

first_digest=$(printf 'first\n' | sha256sum | cut -d' ' -f1)
second_digest=$(printf 'second\n' | sha256sum | cut -d' ' -f1)

if grep -qF "$first_digest" first.cdx.json && grep -qF "$second_digest" second.cdx.json; then
    echo "ok    each component list carries the digest of the bytes in its own package"
else
    echo "FAIL  a component list does not carry the digest of the bytes in its package"
    failures=$((failures + 1))
fi

if grep -qF "$second_digest" first.cdx.json; then
    echo "FAIL  the first component list carries the second package's digest"
    failures=$((failures + 1))
else
    echo "ok    the two component lists differ where the packages differ"
fi

# 11. Every entry in the package reaches the list, not only the assemblies. A
#     list that held the artifacts alone would be the manifest read back.
if grep -qF '"name": "meta.json"' clean.cdx.json; then
    echo "ok    a file the manifest does not name is still in the component list"
else
    echo "FAIL  meta.json is in the package and not in the component list"
    failures=$((failures + 1))
fi

# 12. The list is written for a refused package too, because a reader looking at
#     a refusal wants to see what was in it.
if grep -qF '"name": "Extra.dll"' extra.cdx.json; then
    echo "ok    a refused package still produced its component list"
else
    echo "FAIL  the refused package produced no component list"
    failures=$((failures + 1))
fi

cd /
rm -rf "$tmp"

if [ "$failures" -ne 0 ]; then
    echo "${failures} case(s) failed"
    exit 1
fi

echo "every case held"
