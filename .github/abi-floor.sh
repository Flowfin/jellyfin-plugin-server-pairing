#!/bin/sh
# Compiles the plugin against the oldest server each manifest claims to load on.
#
# One script rather than a block inside a workflow, so the command a person runs
# and the command CI runs are the same bytes:
#
#     sh .github/abi-floor.sh
#
# What it is for. The shipping build compiles against a package chosen by the
# target framework, and that package is at or above every floor the manifests
# claim. So an ordinary build cannot tell whether a host API this plugin calls
# exists on the oldest server the manifest promises, and the first person to
# find out is an operator on that server watching a method that is not there.
# This compiles the same source against the floor package and nothing else, so
# a call to something newer is a compile error here instead.
#
# It compiles and does not test. The question is whether the API surface exists,
# and answering more than that would need a server.
#
# Fail closed on its own inputs. A manifest this script cannot read, or a floor
# it holds no package for, is a scanner that has fallen behind the tree rather
# than a clean result, and it exits non-zero saying which.

set -eu

project=Jellyfin.Plugin.ServerPairing/Jellyfin.Plugin.ServerPairing.csproj

# Which package carries which floor.
#
# This is a lookup rather than string surgery on targetAbi, and the reason is
# the 12.0 line. A package version and a server ABI are different things there,
# because the line has had no stable release and every release candidate on it
# carries the same assembly version:
#
#     [Reflection.AssemblyName]::GetAssemblyName(
#       "$HOME/.nuget/packages/jellyfin.controller/$v/lib/$tfm/MediaBrowser.Controller.dll").Version
#     10.11.0     10.11.0.0
#     10.11.9     10.11.9.0
#     12.0.0-rc1  12.0.0.0
#     12.0.0-rc3  12.0.0.0
#
# So "12.0.0.0" names four published packages and "12.0.0" names none of them,
# and dropping the fourth component of the ABI would ask NuGet for a package
# that does not exist. The floor package is the oldest published one whose
# assembly version is the floor, which is a fact somebody measures once and
# writes here. A floor this table does not name is refused rather than skipped,
# so a new manifest cannot pass by being unrecognised.
floor_package() {
    case "$1" in
    10.11.0.0) echo 10.11.0 ;;
    12.0.0.0) echo 12.0.0-rc1 ;;
    *) return 1 ;;
    esac
}

manifests=$(ls build*.yaml 2>/dev/null || true)
if [ -z "$manifests" ]; then
    echo "::error::abi-floor found no manifest to read. Refusing to report a floor that was never built."
    exit 1
fi

# The restore here is not the shipping one and cannot be. Handing in a different
# package version produces a different graph from the committed lock file, so
# locked mode is off and the lock file is written to a scratch path instead of
# over the one in the tree. What this proves is a compile against the floor. It
# says nothing about whether the shipping graph resolves, which is the ordinary
# build's job and is done in locked mode there.
scratch=$(mktemp -d)
trap 'rm -rf "$scratch"' EXIT INT TERM

built=0
for manifest in $manifests; do
    abi=$(sed -n 's/^targetAbi:[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$manifest")
    framework=$(sed -n 's/^framework:[[:space:]]*"\(.*\)"[[:space:]]*$/\1/p' "$manifest")

    if [ -z "$abi" ] || [ -z "$framework" ]; then
        echo "::error::${manifest} has no targetAbi or no framework this script could read."
        exit 1
    fi

    if ! package=$(floor_package "$abi"); then
        echo "::error::${manifest} claims a floor of ${abi} and .github/abi-floor.sh holds no package for it. Add the mapping, with the command that established it."
        exit 1
    fi

    echo "${manifest}: floor ${abi} on ${framework}, building against Jellyfin ${package}"

    # TargetFrameworks is narrowed to the one this manifest is for. The package
    # version is a single value for the whole restore, and the other target
    # framework has no package at it: asking NuGet for 12.0.0-rc1 while net9.0
    # is still in the set is an NU1202 about a package that supports net10.0.
    dotnet build "$project" \
        --configuration Release \
        -p:TargetFrameworks="$framework" \
        -p:JellyfinPackageVersion="$package" \
        -p:RestoreLockedMode=false \
        -p:NuGetLockFilePath="${scratch}/${framework}.lock.json"

    built=$((built + 1))
done

echo "${built} floor build(s) passed."
