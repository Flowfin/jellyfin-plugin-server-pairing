#!/bin/sh
# Prints the Jellyfin server image tag the floor in a manifest names, or refuses the manifest.
#
# usage: sh .github/floor-image.sh <manifest.yaml>
#
# THE FLOOR IS READ FROM THE MANIFEST RATHER THAN WRITTEN ANYWHERE, so a floor that moves moves
# every reader of it, and no check can end up judging a server the manifest does not promise. The
# image tag is the first three components: a Jellyfin release is tagged X.Y.Z and targetAbi carries
# the fourth component the manifest format requires.
#
# ONE DERIVATION RATHER THAN TWO. .github/floor-install.sh installs a package on that image and
# .github/floor-withheld.sh says why one cannot be installed yet. Both need the same tag out of the
# same field, and a second copy of the reading is a second thing to keep honest: the day one of
# them learned to read a differently-quoted targetAbi and the other did not, the two would disagree
# about which server the promise is about and neither would say so.
#
# Only the tag reaches standard output, because the callers read it with a command substitution. A
# refusal goes to standard error and carries the manifest it is about.

set -eu

manifest=${1:?the manifest declaring the floor}

test -f "$manifest" || { echo "FAIL  no such manifest: $manifest" >&2; exit 1; }

abi=$(grep -E '^targetAbi:' "$manifest" | head -1 | sed -E 's/^targetAbi:[[:space:]]*"?([^"]*)"?[[:space:]]*$/\1/')
case "$abi" in
    [0-9]*.[0-9]*.[0-9]*.[0-9]*) ;;
    *) echo "FAIL  $manifest declares no four-part targetAbi, read as '$abi'" >&2; exit 1 ;;
esac

printf '%s\n' "$abi" | cut -d. -f1-3
