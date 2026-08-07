#!/bin/sh
# Resolves every link in the tracked markdown of this repository.
#
# One script rather than a block inside the workflow, so the command a person
# runs and the command CI runs are the same bytes:
#
#     sh .github/link-check.sh            relative links and anchors
#     sh .github/link-check.sh --external the links that leave this repository
#
# Without --external nothing is fetched, so the default mode is offline and
# deterministic. The external mode needs the network and is therefore not
# attached to a pull request; a link that has rotted is a fact about somebody
# else's server, and a gate that reds for it teaches people to re-run the gate.
#
# Fail closed in both directions. A run that found no markdown, or found
# markdown carrying no link at all, is a broken scanner rather than a clean
# tree, and it exits non-zero saying so.

set -eu

mode=${1:-}

# GitHub's heading slug, reduced to what the headings in this tree need:
# lowercase, drop anything that is not a letter, a digit, a space, an
# underscore or a hyphen, then spaces to hyphens.
anchors_of() {
    grep -E '^#{1,6} ' "$1" 2>/dev/null |
        sed -E 's/^#+[[:space:]]*//' |
        tr '[:upper:]' '[:lower:]' |
        sed -E 's/[^a-z0-9 _-]//g' |
        sed -E 's/[[:space:]]+/-/g'
}

# Inline links [text](target) and autolinks <https://...>. Reference-style
# links are not used in this tree; a run over a file that started using them
# would count fewer links rather than more, which the totals below catch.
targets_of() {
    grep -o '](<*[^)> ]*' "$1" 2>/dev/null | sed 's/^](<*//'
    grep -oE '<https?://[^>]+>' "$1" 2>/dev/null | sed 's/^<//; s/>$//'
}

files=$(git ls-files '*.md')
if [ -z "$files" ]; then
    echo "::error::link-check found no tracked markdown. Refusing to report a clean tree."
    exit 1
fi

checked=0
skipped=0
failed=0

for f in $files; do
    dir=$(dirname "$f")
    for t in $(targets_of "$f"); do
        case "$t" in
            mailto:*) skipped=$((skipped + 1)); continue ;;
            http://*|https://*)
                if [ "$mode" != "--external" ]; then
                    skipped=$((skipped + 1))
                    continue
                fi
                checked=$((checked + 1))
                code=$(curl -s -o /dev/null -w '%{http_code}' -L --max-time 30 \
                    -A "link-check (github actions)" "$t" || echo 000)
                case "$code" in
                    404|410)
                        echo "GONE  $f -> $t ($code)"
                        failed=$((failed + 1))
                        ;;
                    000)
                        echo "::error::link-check could not reach $t from $f. Failing closed rather than calling it reachable."
                        failed=$((failed + 1))
                        ;;
                    *) : ;;
                esac
                continue
                ;;
        esac

        [ "$mode" = "--external" ] && { skipped=$((skipped + 1)); continue; }

        path=${t%%#*}
        frag=""
        case "$t" in *#*) frag=${t#*#} ;; esac

        if [ -z "$path" ]; then
            target=$f
        elif [ "$dir" = "." ]; then
            target=$path
        else
            target="$dir/$path"
        fi

        checked=$((checked + 1))

        if [ ! -e "$target" ]; then
            echo "MISSING  $f -> $t (no such path: $target)"
            failed=$((failed + 1))
            continue
        fi

        case "$target" in
            *.md)
                if [ -n "$frag" ]; then
                    slug=$(printf '%s' "$frag" | tr '[:upper:]' '[:lower:]')
                    if ! anchors_of "$target" | grep -qxF "$slug"; then
                        echo "NO ANCHOR  $f -> $t (no heading in $target slugs to '$slug')"
                        failed=$((failed + 1))
                    fi
                fi
                ;;
        esac
    done
done

echo "link-check: $checked checked, $skipped not in this mode, $failed bad."

if [ "$checked" -eq 0 ]; then
    echo "::error::link-check resolved no link at all. An empty result here is a broken scanner, not a clean tree."
    exit 1
fi

if [ "$failed" -ne 0 ]; then
    echo "::error::$failed link(s) do not resolve."
    exit 1
fi
