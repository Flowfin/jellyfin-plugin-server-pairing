#!/bin/sh
# Compares every manifest against the build it describes, before anything is
# packaged.
#
#     sh .github/manifest-check.sh                  every comparison that reads tracked files
#     sh .github/manifest-check.sh --output DIR     and the artefact list against a build output
#
# One script rather than a block inside a workflow, so the command a person runs
# and the command CI runs are the same bytes. .github/manifest-check-test.sh is
# where each comparison below is watched refusing something.
#
# What it is for. The manifests and the project each carry facts about which
# server this plugin is for and what it is, and nothing compares them. When they
# disagree the package builds, publishes, installs and then does not load, which
# is the most expensive place to find out. Every value below is read out of a
# tracked file, so a disagreement is caught before a build rather than after a
# publication.
#
# It reads files and never the environment. There is no variable that can be set
# to make a comparison pass, because a check whose verdict can be handed to it
# from outside is a check that will be handed one.
#
# Fail closed on its own inputs. A manifest this script cannot read, a field it
# cannot find, a project file that is not there, or a floor table it cannot
# parse is a scanner that has fallen behind the tree rather than a clean result,
# and each of those exits non-zero saying which.

set -eu

root=.
output=""

while [ $# -gt 0 ]; do
    case "$1" in
    --root)
        root=${2:?--root needs a directory}
        shift 2
        ;;
    --output)
        output=${2:?--output needs a directory}
        shift 2
        ;;
    *)
        echo "::error::manifest-check does not know the argument ${1}."
        exit 1
        ;;
    esac
done

project="${root}/Jellyfin.Plugin.ServerPairing/Jellyfin.Plugin.ServerPairing.csproj"
props="${root}/Directory.Build.props"
source_file="${root}/Jellyfin.Plugin.ServerPairing/Plugin.cs"
floor_script="${root}/.github/abi-floor.sh"

for f in "$project" "$props" "$source_file" "$floor_script"; do
    if [ ! -f "$f" ]; then
        echo "::error::manifest-check found no ${f}. Refusing to report agreement it could not check."
        exit 1
    fi
done

manifests=$(ls "${root}"/build*.yaml 2>/dev/null || true)
if [ -z "$manifests" ]; then
    echo "::error::manifest-check found no manifest to read. Refusing to report agreement it never checked."
    exit 1
fi

# A scalar field of a manifest, as the packaging tool reads it: the value is
# quoted and the key is at column zero.
field_of() {
    sed -n "s/^${2}:[[:space:]]*\"\(.*\)\"[[:space:]]*\$/\1/p" "$1"
}

# The artefact list, which is a YAML sequence rather than a scalar.
artifacts_of() {
    sed -n '/^artifacts:/,/^[a-z]/p' "$1" |
        sed -n 's/^-[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p'
}

# Which package carries which floor, read out of .github/abi-floor.sh rather
# than repeated here. That table is measured against published packages and the
# measurement is written at it; a second copy in this file is the copy that goes
# stale, which is the failure this whole script exists to catch one level up.
floor_package() {
    sed -n 's/^[[:space:]]*\([0-9][0-9.]*\))[[:space:]]*echo[[:space:]]*\([^ ;]*\).*/\1 \2/p' "$floor_script" |
        while read -r floor package; do
            [ "$floor" = "$1" ] && { echo "$package"; break; }
        done
}

floor_rows=$(sed -n 's/^[[:space:]]*\([0-9][0-9.]*\))[[:space:]]*echo[[:space:]]*\([^ ;]*\).*/\1 \2/p' "$floor_script" | wc -l)
if [ "$floor_rows" -eq 0 ]; then
    echo "::error::manifest-check could not read a single floor from ${floor_script}. Refusing to report a floor it never resolved."
    exit 1
fi

# The shipping build's package version for a target framework, from the property
# the whole tree builds against.
shipping_package() {
    sed -n "s/.*<JellyfinPackageVersion Condition=\"[^\"]*'\$(TargetFramework)' == '${1}'\">\([^<]*\)<.*/\1/p" "$props"
}

# Not older than, over two versions on one server line. sort -V puts a
# prerelease before the release it leads to and orders rc1 before rc3, which is
# the ordering this comparison needs and the one a plain string comparison gets
# wrong.
not_older() {
    [ "$1" = "$2" ] && return 0
    [ "$(printf '%s\n%s\n' "$1" "$2" | sort -V | tail -n1)" = "$1" ]
}

assembly_version=$(sed -n 's|.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*|\1|p' "$props")
if [ -z "$assembly_version" ]; then
    echo "::error::manifest-check found no AssemblyVersion in ${props}. Refusing to compare a version against nothing."
    exit 1
fi

source_guid=$(sed -n 's/.*Guid\.Parse("\([0-9a-fA-F-]*\)").*/\1/p' "$source_file" | head -n1)
if [ -z "$source_guid" ]; then
    echo "::error::manifest-check found no plugin identifier in ${source_file}. Refusing to compare an identifier against nothing."
    exit 1
fi

project_frameworks=$(sed -n 's|.*<TargetFrameworks>\(.*\)</TargetFrameworks>.*|\1|p' "$project" | tr ';' ' ')
if [ -z "$project_frameworks" ]; then
    echo "::error::manifest-check found no TargetFrameworks in ${project}. Refusing to compare a framework against nothing."
    exit 1
fi

bad=0
checked=0
manifest_frameworks=""

for manifest in $manifests; do
    version=$(field_of "$manifest" version)
    abi=$(field_of "$manifest" targetAbi)
    framework=$(field_of "$manifest" framework)
    guid=$(field_of "$manifest" guid)
    artifacts=$(artifacts_of "$manifest")

    for pair in "version:${version}" "targetAbi:${abi}" "framework:${framework}" "guid:${guid}"; do
        if [ "${pair#*:}" = "" ]; then
            echo "::error::${manifest} has no ${pair%%:*} this script could read."
            exit 1
        fi
    done

    if [ -z "$artifacts" ]; then
        echo "::error::${manifest} has no artefact list this script could read."
        exit 1
    fi

    manifest_frameworks="${manifest_frameworks} ${framework}"

    # 1. The framework the manifest claims is one the project actually builds.
    found=0
    for tfm in $project_frameworks; do
        [ "$tfm" = "$framework" ] && found=1
    done
    checked=$((checked + 1))
    if [ "$found" -eq 0 ]; then
        echo "DISAGREES  ${manifest}: framework ${framework}, and the project builds ${project_frameworks}"
        bad=$((bad + 1))
    fi

    # 2. The version the manifest publishes is the version the assembly carries.
    checked=$((checked + 1))
    if [ "$version" != "$assembly_version" ]; then
        echo "DISAGREES  ${manifest}: version ${version}, and AssemblyVersion is ${assembly_version}"
        bad=$((bad + 1))
    fi

    # 3. The floor the manifest claims is not above the package the shipping
    # build compiles against. A floor nobody has a package for is refused rather
    # than skipped, so a new manifest cannot pass by being unrecognised.
    checked=$((checked + 1))
    floor_pkg=$(floor_package "$abi")
    shipping=$(shipping_package "$framework")
    if [ -z "$floor_pkg" ]; then
        echo "DISAGREES  ${manifest}: floor ${abi}, and ${floor_script} holds no package for it"
        bad=$((bad + 1))
    elif [ -z "$shipping" ]; then
        echo "DISAGREES  ${manifest}: framework ${framework}, and ${props} sets no JellyfinPackageVersion for it"
        bad=$((bad + 1))
    elif ! not_older "$shipping" "$floor_pkg"; then
        echo "DISAGREES  ${manifest}: floor ${abi} is package ${floor_pkg}, and the shipping build uses ${shipping}"
        bad=$((bad + 1))
    fi

    # 5. The plugin the manifest describes is the plugin in the source.
    checked=$((checked + 1))
    if [ "$(echo "$guid" | tr 'A-Z' 'a-z')" != "$(echo "$source_guid" | tr 'A-Z' 'a-z')" ]; then
        echo "DISAGREES  ${manifest}: guid ${guid}, and ${source_file} is ${source_guid}"
        bad=$((bad + 1))
    fi

    # 4. Every artefact the manifest names is one the build produced. The
    # opposite direction, an assembly in the package the manifest does not name,
    # is .github/package-audit.sh and is deliberately not repeated here.
    if [ -n "$output" ]; then
        for artifact in $artifacts; do
            checked=$((checked + 1))
            if [ ! -f "${output}/${artifact}" ]; then
                echo "DISAGREES  ${manifest}: names ${artifact}, and ${output} does not hold it"
                bad=$((bad + 1))
            fi
        done
    fi

    echo "${manifest}: version ${version}, floor ${abi} on ${framework}, $(echo "$artifacts" | wc -w) artefact(s)"
done

# Every framework the project builds has a manifest. Without this a target
# framework added to the project ships no package for that server line and every
# comparison above still passes, because each of them starts from a manifest.
for tfm in $project_frameworks; do
    checked=$((checked + 1))
    found=0
    for claimed in $manifest_frameworks; do
        [ "$claimed" = "$tfm" ] && found=1
    done
    if [ "$found" -eq 0 ]; then
        echo "DISAGREES  the project builds ${tfm}, and no manifest claims it"
        bad=$((bad + 1))
    fi
done

if [ -z "$output" ]; then
    echo "manifest-check: ${checked} comparison(s), ${bad} disagreement(s). The artefact list was not compared: no --output was given."
else
    echo "manifest-check: ${checked} comparison(s), ${bad} disagreement(s)."
fi

if [ "$bad" -gt 0 ]; then
    echo "::error::manifest-check found ${bad} disagreement(s) between the manifests and the build."
    exit 1
fi
