#!/bin/sh
# Reads a built plugin package and says what is inside it.
#
#     sh .github/package-audit.sh <package.zip> [manifest] [output.json]
#
# Defaults: manifest build.yaml, output components.cdx.json.
#
# One script rather than a block inside the workflow, so the command a person
# runs and the command CI runs are the same bytes. .github/package-audit-test.sh
# is where each rule below is watched refusing something.
#
# Two things, and both are read out of the package rather than out of the
# project file, because those two differ exactly when it matters.
#
# The refusal: an assembly in the package that the manifest does not name. A
# package carries whatever the build copied next to the plugin, and the manifest
# is what an operator is told the package holds. The first time a package
# reference is added that is not the host's own, a second assembly appears in
# the package, and nothing in this repository notices today.
#
# The component list: every entry in the package with the digest of its bytes,
# in CycloneDX, so a scanner can read it. Generated from the package, so a file
# that arrived without anyone deciding to ship it is in the list.
#
# The opposite direction, a manifest naming an artefact the package does not
# hold, is not checked here. That is issue #71 and it is not this script's.
#
# What the list does not carry: an assembly version or a package identity per
# component. Reading those means reading assembly metadata out of a .dll, which
# needs a runtime this script does not have. Every component is a file with its
# digest, and that is the whole of it.
#
# Fail closed on its own inputs. A package that could not be extracted, a
# package with no entries, a package with no assembly at all, or a manifest with
# no artefact list is a broken scanner rather than a clean package, and each of
# those exits non-zero saying so.

set -eu

package=${1:-}
manifest=${2:-build.yaml}
output=${3:-components.cdx.json}

if [ -z "$package" ]; then
    echo "::error::package-audit needs the path of a built package. Refusing to report a clean package."
    exit 1
fi

if [ ! -f "$package" ]; then
    echo "::error::No package at ${package}. Refusing to report a clean package."
    exit 1
fi

if [ ! -f "$manifest" ]; then
    echo "::error::No manifest at ${manifest}. Refusing to report a clean package."
    exit 1
fi

# Removed at the end rather than from an exit trap, for the reason
# .github/pr-hygiene-test.sh gives: a trap set here also fires when a command
# substitution subshell exits, which deletes the working files out from under
# the run still using them.
tmp=$(mktemp -d)

# The artefact list out of the manifest. Read as the list items inside the
# `artifacts:` block and nothing else, so a file name appearing in the
# description does not silently widen what the package is allowed to hold. The
# range ends at the next key at column zero.
sed -n '/^artifacts:[[:space:]]*$/,/^[^[:space:]-]/p' "$manifest" |
    sed -n 's/^[[:space:]]*-[[:space:]]*//p' |
    tr -d '\042\047' >"$tmp/artefacts"

if [ ! -s "$tmp/artefacts" ]; then
    echo "::error::${manifest} declares no artifacts. Refusing to report a clean package."
    rm -rf "$tmp"
    exit 1
fi

# Guarded rather than used inside a `for ... in $(...)` word list, where a
# failing command substitution does not trip `set -e` and an unreadable archive
# would be reported as a package with nothing wrong in it.
if ! unzip -qq -o -d "$tmp/contents" "$package" >"$tmp/unzip.log" 2>&1; then
    echo "::error::Could not extract ${package}. Refusing to report a clean package."
    cat "$tmp/unzip.log"
    rm -rf "$tmp"
    exit 1
fi

# The entry list comes from what was extracted rather than from the archive
# index, so the list describes bytes that exist rather than names the archive
# claims. Sorted so two runs over one package produce one list.
(cd "$tmp/contents" && find . -type f | sed 's|^\./||' | LC_ALL=C sort) >"$tmp/entries"

if [ ! -s "$tmp/entries" ]; then
    echo "::error::${package} holds no files. Refusing to report a clean package."
    rm -rf "$tmp"
    exit 1
fi

grep -i '\.dll$' "$tmp/entries" >"$tmp/assemblies" || true

if [ ! -s "$tmp/assemblies" ]; then
    echo "::error::${package} holds no assembly. Refusing to report a clean package."
    rm -rf "$tmp"
    exit 1
fi

# The refusal. Compared on the file name rather than on the path inside the
# package, because the manifest names artefacts and not locations.
: >"$tmp/unnamed"
while IFS= read -r entry; do
    [ -n "$entry" ] || continue
    name=$(basename "$entry")
    if ! grep -qxF "$name" "$tmp/artefacts"; then
        echo "$entry" >>"$tmp/unnamed"
    fi
done <"$tmp/assemblies"

# The component list, written whether or not the refusal fires, because a
# reader looking at a refused package wants to see what was in it.
#
# No timestamp field. Two runs over one package would then differ in the one
# place a reader compares, and what this list is about is the bytes.
escape() {
    printf '%s' "$1" | sed -e 's|\\|\\\\|g' -e 's|"|\\"|g'
}

package_digest=$(sha256sum "$package" | cut -d' ' -f1)

{
    printf '{\n'
    printf '  "bomFormat": "CycloneDX",\n'
    printf '  "specVersion": "1.6",\n'
    printf '  "version": 1,\n'
    printf '  "metadata": {\n'
    printf '    "component": {\n'
    printf '      "type": "application",\n'
    printf '      "name": "%s",\n' "$(escape "$(basename "$package")")"
    printf '      "hashes": [{ "alg": "SHA-256", "content": "%s" }]\n' "$package_digest"
    printf '    }\n'
    printf '  },\n'
    printf '  "components": [\n'

    first=yes
    while IFS= read -r entry; do
        [ -n "$entry" ] || continue
        digest=$(sha256sum "$tmp/contents/$entry" | cut -d' ' -f1)
        [ "$first" = yes ] || printf ',\n'
        first=no
        printf '    {\n'
        printf '      "type": "file",\n'
        printf '      "name": "%s",\n' "$(escape "$entry")"
        printf '      "hashes": [{ "alg": "SHA-256", "content": "%s" }]\n' "$digest"
        printf '    }'
    done <"$tmp/entries"

    printf '\n  ]\n'
    printf '}\n'
} >"$output"

count=$(wc -l <"$tmp/entries" | tr -d ' ')
echo "Read ${count} entries out of $(basename "$package") into ${output}."
sed 's|^|  |' "$tmp/entries"

if [ -s "$tmp/unnamed" ]; then
    while IFS= read -r entry; do
        [ -n "$entry" ] || continue
        echo "::error::${entry} is in the package and ${manifest} does not name it."
    done <"$tmp/unnamed"
    echo "The artifacts ${manifest} names:"
    sed 's|^|  |' "$tmp/artefacts"
    rm -rf "$tmp"
    exit 1
fi

echo "Every assembly in the package is named by ${manifest}."
rm -rf "$tmp"
