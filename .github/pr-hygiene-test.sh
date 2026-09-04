#!/bin/sh
# Proves .github/pr-hygiene.sh refuses what it says it refuses.
#
#     sh .github/pr-hygiene-test.sh
#
# A check nobody has watched refuse something is a check nobody knows the state
# of, so every rule in the script beside this one has a case here that goes red
# without it and a case that passes with it.
#
# The fixtures are built with git plumbing in a throwaway repository under the
# temporary directory: blobs, trees and commit-tree. No fixture reaches a branch
# and none is ever pushed. The throwaway repository is pointed at empty git
# configuration files so the same bytes produce the same fixture on every
# machine rather than picking up whatever the machine happens to set.

set -eu

script=$(cd "$(dirname "$0")" && pwd)/pr-hygiene.sh
if [ ! -f "$script" ]; then
    echo "cannot find pr-hygiene.sh next to this test"
    exit 1
fi

# Removed at the end rather than from an exit trap. A trap set here also fires
# when a command substitution subshell exits, which deletes the fixtures out
# from under the run that is still using them. A run that dies before the end
# leaves its fixture directory behind, which is where you look at it.
tmp=$(mktemp -d)

: >"$tmp/gitconfig"
GIT_CONFIG_GLOBAL="$tmp/gitconfig"
GIT_CONFIG_SYSTEM="$tmp/gitconfig"
GIT_AUTHOR_NAME=fixture
GIT_AUTHOR_EMAIL=fixture@invalid
GIT_COMMITTER_NAME=fixture
GIT_COMMITTER_EMAIL=fixture@invalid
GIT_AUTHOR_DATE="2026-01-01T00:00:00+0000"
GIT_COMMITTER_DATE="2026-01-01T00:00:00+0000"
export GIT_CONFIG_GLOBAL GIT_CONFIG_SYSTEM
export GIT_AUTHOR_NAME GIT_AUTHOR_EMAIL GIT_COMMITTER_NAME GIT_COMMITTER_EMAIL
export GIT_AUTHOR_DATE GIT_COMMITTER_DATE

git init --quiet --bare "$tmp/repo.git"
cd "$tmp/repo.git"

# Writes a blob from a file and returns its object name.
blob_of() {
    git hash-object -w "$1"
}

# Builds the fixture root tree. The two project directories are real subtrees,
# because the rule about source changing without tests is a rule about path
# prefixes and a flat fixture would not exercise it.
#
# Arguments are the manifest, plugin source and test source blobs. Any further
# top-level entries are read from standard input as mktree lines.
root_tree() {
    plugin=$(printf '100644 blob %s\tPlugin.cs\n' "$2" | git mktree)
    tests=$(printf '100644 blob %s\tPluginTests.cs\n' "$3" | git mktree)
    {
        printf '100644 blob %s\tbuild.yaml\n' "$1"
        printf '040000 tree %s\tJellyfin.Plugin.ServerPairing\n' "$plugin"
        printf '040000 tree %s\tJellyfin.Plugin.ServerPairing.Tests\n' "$tests"
        cat
    } | git mktree
}

# Commits a tree with a subject, on top of a parent where one is given.
commit_of() {
    if [ -n "$2" ]; then
        printf '%s\n' "$1" | git commit-tree "$3" -p "$2"
    else
        printf '%s\n' "$1" | git commit-tree "$3"
    fi
}

# A manifest carrying the version and changelog text it is given. Written with
# printf rather than a here-document: a here-document inside a function inside a
# command substitution hangs intermittently under the shell this runs on, and a
# test that hangs is worse than one that fails.
manifest() {
    {
        printf -- '---\n'
        printf 'name: "Server Pairing"\n'
        printf 'version: "%s"\n' "$1"
        printf 'targetAbi: "10.11.0.0"\n'
        printf 'framework: "net9.0"\n'
        printf 'owner: "iderex"\n'
        printf 'artifacts:\n'
        printf -- '- "Jellyfin.Plugin.ServerPairing.dll"\n'
        printf 'changelog: >\n'
        printf '  %s\n' "$2"
    } >"$tmp/build.yaml"
    blob_of "$tmp/build.yaml"
}

printf 'source\n' >"$tmp/source.cs"
source_blob=$(blob_of "$tmp/source.cs")
printf 'changed source\n' >"$tmp/source2.cs"
source2_blob=$(blob_of "$tmp/source2.cs")
printf 'test\n' >"$tmp/test.cs"
test_blob=$(blob_of "$tmp/test.cs")
printf 'changed test\n' >"$tmp/test2.cs"
test2_blob=$(blob_of "$tmp/test2.cs")
printf 'entry\n' >"$tmp/changelog.md"
changelog_blob=$(blob_of "$tmp/changelog.md")
printf -- '- [protocol] Version 1 is no longer accepted.\n' >"$tmp/changelog-protocol.md"
changelog_protocol_blob=$(blob_of "$tmp/changelog-protocol.md")
printf -- '- [contract] The consumer contract gained a member.\n' >"$tmp/changelog-contract.md"
changelog_contract_blob=$(blob_of "$tmp/changelog-contract.md")
printf 'the wire\n' >"$tmp/protocol.md"
protocol_blob=$(blob_of "$tmp/protocol.md")
printf 'the contract\n' >"$tmp/consumer-interface.md"
contract_blob=$(blob_of "$tmp/consumer-interface.md")
printf 'canonical form\n' >"$tmp/canonical.cs"
canonical_blob=$(blob_of "$tmp/canonical.cs")
awk 'BEGIN { for (i = 0; i < 401; i++) print "a line that is long enough to count" }' >"$tmp/big.txt"
big_blob=$(blob_of "$tmp/big.txt")

base_manifest=$(manifest 0.0.0.0 "Nothing has been released yet.")
bumped_manifest=$(manifest 0.0.0.1 "Nothing has been released yet.")
bumped_with_entry=$(manifest 0.0.0.1 "The first release.")

# The base every case starts from.
base_tree=$(root_tree "$base_manifest" "$source_blob" "$test_blob" </dev/null)
base=$(commit_of "Start the fixture (#0)" "" "$base_tree")

# One head tree per shape the cases need.
both_tree=$(root_tree "$base_manifest" "$source2_blob" "$test2_blob" </dev/null)
source_only_tree=$(root_tree "$base_manifest" "$source2_blob" "$test_blob" </dev/null)
bumped_tree=$(root_tree "$bumped_manifest" "$source_blob" "$test_blob" </dev/null)
bumped_entry_tree=$(root_tree "$bumped_with_entry" "$source_blob" "$test_blob" </dev/null)
bumped_changelog_tree=$(printf '100644 blob %s\tCHANGELOG.md\n' "$changelog_blob" |
    root_tree "$bumped_manifest" "$source_blob" "$test_blob")
bumped_both_tree=$(printf '100644 blob %s\tCHANGELOG.md\n' "$changelog_blob" |
    root_tree "$bumped_with_entry" "$source_blob" "$test_blob")
big_tree=$(printf '100644 blob %s\tbig.txt\n' "$big_blob" |
    root_tree "$base_manifest" "$source_blob" "$test_blob")

# The protocol and contract cases. A document under `docs/` and a file under the
# plugin's `Protocol/` directory are the two arms of the protocol pattern, so
# both are walked rather than one standing in for the other.
docs_tree_of() {
    printf '100644 blob %s\t%s\n' "$1" "$2" | git mktree
}

# The same root shape as root_tree, with the plugin subtree carrying one file
# under Protocol/. Written out rather than folded into root_tree, which builds a
# flat plugin subtree that every other fixture here depends on.
#
# Takes an optional CHANGELOG.md blob. Since #217 the marker is owed by a
# SOURCE change and not by a document one, so the three cases that prove
# the marker discipline reach the guard through this root, not a docs tree.
protocol_source_root() {
    changelog=$1
    inner=$(printf '100644 blob %s\tCanonicalForm.cs\n' "$canonical_blob" | git mktree)
    plugin=$(printf '100644 blob %s\tPlugin.cs\n040000 tree %s\tProtocol\n' "$source_blob" "$inner" | git mktree)
    tests=$(printf '100644 blob %s\tPluginTests.cs\n' "$test_blob" | git mktree)
    {
        [ -n "$changelog" ] && printf '100644 blob %s\tCHANGELOG.md\n' "$changelog"
        printf '100644 blob %s\tbuild.yaml\n' "$base_manifest"
        printf '040000 tree %s\tJellyfin.Plugin.ServerPairing\n' "$plugin"
        printf '040000 tree %s\tJellyfin.Plugin.ServerPairing.Tests\n' "$tests"
    } | git mktree
}

protocol_doc_tree=$(printf '040000 tree %s\tdocs\n' "$(docs_tree_of "$protocol_blob" protocol.md)" |
    root_tree "$base_manifest" "$source_blob" "$test_blob")
protocol_marked_tree=$(protocol_source_root "$changelog_protocol_blob")
protocol_unmarked_tree=$(protocol_source_root "$changelog_blob")
protocol_wrong_marker_tree=$(protocol_source_root "$changelog_contract_blob")
protocol_source_tree=$(protocol_source_root "")
contract_doc_tree=$(printf '040000 tree %s\tdocs\n' "$(docs_tree_of "$contract_blob" consumer-interface.md)" |
    root_tree "$base_manifest" "$source_blob" "$test_blob")
contract_marked_tree=$({
    printf '100644 blob %s\tCHANGELOG.md\n' "$changelog_contract_blob"
    printf '040000 tree %s\tdocs\n' "$(docs_tree_of "$contract_blob" consumer-interface.md)"
} | root_tree "$base_manifest" "$source_blob" "$test_blob")

good=$(commit_of "Change the plugin (#65)" "$base" "$both_tree")
unreferenced=$(commit_of "Change the plugin" "$base" "$both_tree")
bumped=$(commit_of "Bump the manifest version (#65)" "$base" "$bumped_tree")
bumped_entry=$(commit_of "Bump the manifest version (#65)" "$base" "$bumped_entry_tree")
bumped_changelog=$(commit_of "Bump the manifest version (#65)" "$base" "$bumped_changelog_tree")
bumped_both=$(commit_of "Bump the manifest version (#65)" "$base" "$bumped_both_tree")
big=$(commit_of "Add a large file (#65)" "$base" "$big_tree")
source_only=$(commit_of "Change only the plugin source (#65)" "$base" "$source_only_tree")
protocol_doc=$(commit_of "Change the protocol document (#76)" "$base" "$protocol_doc_tree")
protocol_marked=$(commit_of "Change the protocol document (#76)" "$base" "$protocol_marked_tree")
protocol_unmarked=$(commit_of "Change the protocol document (#76)" "$base" "$protocol_unmarked_tree")
protocol_wrong_marker=$(commit_of "Change the protocol document (#76)" "$base" "$protocol_wrong_marker_tree")
protocol_source=$(commit_of "Change the protocol source (#76)" "$base" "$protocol_source_tree")
contract_doc=$(commit_of "Change the consumer contract (#76)" "$base" "$contract_doc_tree")
contract_marked=$(commit_of "Change the consumer contract (#76)" "$base" "$contract_marked_tree")

passes=0
failures=0

# Runs the script over one fixture and asserts the exit status and a phrase the
# output has to carry, so a case cannot pass on the right status for the wrong
# reason.
expect() {
    name=$1
    want=$2
    phrase=$3
    body=$4
    author_type=$5
    association=$6
    head=$7

    set +e
    out=$(PR_BODY="$body" PR_AUTHOR_TYPE="$author_type" PR_AUTHOR_ASSOCIATION="$association" \
        BASE_SHA="$base" HEAD_SHA="$head" sh "$script" 2>&1)
    got=$?
    set -e

    if [ "$got" -ne "$want" ]; then
        echo "FAIL  $name: expected exit $want, got $got"
        printf '%s\n' "$out" | sed 's/^/      /'
        failures=$((failures + 1))
        return
    fi
    if ! printf '%s' "$out" | grep -qF "$phrase"; then
        echo "FAIL  $name: output does not carry \"$phrase\""
        printf '%s\n' "$out" | sed 's/^/      /'
        failures=$((failures + 1))
        return
    fi
    echo "ok    $name"
    passes=$((passes + 1))
}

expect "a clean pull request passes" \
    0 "carries what the refusing tier asks for" "Closes #65." User OWNER "$good"

expect "a body with no issue reference is refused" \
    1 "the pull request body references no issue by number" "Some prose with no number." User OWNER "$good"

expect "a commit subject with no issue reference is refused" \
    1 "commit subjects reference no issue" "Closes #65." User OWNER "$unreferenced"

# The version bump rule, one case per arm. The two middle cases are the ones
# that carry it: each has one arm green and the other red, so a repair that
# collapsed the two back into alternatives would go red here rather than in a
# catalogue six weeks later, which is where #345 found it.
expect "a manifest version change with neither changelog is refused" \
    1 "the manifest version changed and CHANGELOG.md did not" "Closes #65." User OWNER "$bumped"

expect "a manifest version change carrying only the manifest field is refused" \
    1 "the manifest version changed and CHANGELOG.md did not" "Closes #65." User OWNER "$bumped_entry"

expect "a manifest version change carrying only CHANGELOG.md is refused" \
    1 "the manifest changelog field still describes the version before it" "Closes #65." User OWNER "$bumped_changelog"

expect "a manifest version change carrying both changelogs passes" \
    0 "the manifest changelog field changed with it" "Closes #65." User OWNER "$bumped_both"

expect "a protocol document change alone is not a protocol change" \
    0 "nothing in this pull request changes the protocol" "Closes #217." User OWNER "$protocol_doc"

expect "a protocol source change with no changelog line is refused" \
    1 "the protocol changed and CHANGELOG.md gained no [protocol] line" "Closes #76." User OWNER "$protocol_source"

expect "a protocol change with an unmarked changelog line is refused" \
    1 "the protocol changed and CHANGELOG.md gained no [protocol] line" "Closes #76." User OWNER "$protocol_unmarked"

expect "a protocol change carrying the other marker is refused" \
    1 "the protocol changed and CHANGELOG.md gained no [protocol] line" "Closes #76." User OWNER "$protocol_wrong_marker"

expect "a protocol change with a marked changelog line passes" \
    0 "the protocol changed and CHANGELOG.md gained a [protocol] line" "Closes #76." User OWNER "$protocol_marked"

# The declaration route. It widens what passes, so the cases that matter are the
# three that still refuse: without them the route is a way past the rule rather
# than a way of saying the paths over-refused.
declared=$(printf 'Closes #314.\n\nNo protocol change: the loop is rewritten and no byte on the wire moves.\n')
declared_bare=$(printf 'Closes #314.\n\nNo protocol change:\n')
declared_midline=$(printf 'Closes #314.\n\nThis pull request is not saying No protocol change: it changes the wire.\n')

expect "a protocol source change the body declares passes" \
    0 "the body declares no protocol change" "$declared" User OWNER "$protocol_source"

expect "a declaration carrying no reason is refused" \
    1 "the protocol changed and CHANGELOG.md gained no [protocol] line" "$declared_bare" User OWNER "$protocol_source"

expect "a declaration that is not at the start of a line is refused" \
    1 "the protocol changed and CHANGELOG.md gained no [protocol] line" "$declared_midline" User OWNER "$protocol_source"

expect "a contract change with no changelog line is refused" \
    1 "the contract changed and CHANGELOG.md gained no [contract] line" "Closes #76." User OWNER "$contract_doc"

expect "declaring one kind does nothing for the other" \
    1 "the contract changed and CHANGELOG.md gained no [contract] line" "$declared" User OWNER "$contract_doc"

expect "a contract change with a marked changelog line passes" \
    0 "the contract changed and CHANGELOG.md gained a [contract] line" "Closes #76." User OWNER "$contract_marked"

expect "a pull request touching neither says so" \
    0 "nothing in this pull request changes the protocol" "Closes #65." User OWNER "$good"

expect "a large diff annotates and does not fail" \
    0 "over the 400 that are comfortable to read" "Closes #65." User OWNER "$big"

expect "source without a test change annotates and does not fail" \
    0 "the test project did not" "Closes #65." User OWNER "$source_only"

expect "a bot is not refused for a missing issue reference" \
    0 "the author is a bot" "Bumps a dependency." Bot NONE "$good"

expect "an author from outside this repository is not refused" \
    0 "from outside this repository" "Some prose with no number." User CONTRIBUTOR "$good"

# The scanner's own failure modes. Both are the check being unable to judge, and
# both are refusals rather than a clean report.
set +e
out=$(PR_BODY="Closes #65." BASE_SHA="" HEAD_SHA="$good" sh "$script" 2>&1)
got=$?
set -e
if [ "$got" -eq 1 ] && printf '%s' "$out" | grep -qF "Refusing to report a clean pull request"; then
    echo "ok    a missing revision is refused rather than skipped"
    passes=$((passes + 1))
else
    echo "FAIL  a missing revision should be refused, got exit $got"
    failures=$((failures + 1))
fi

set +e
out=$(PR_BODY="Closes #65." PR_AUTHOR_TYPE=User PR_AUTHOR_ASSOCIATION=OWNER \
    BASE_SHA="$good" HEAD_SHA="$good" sh "$script" 2>&1)
got=$?
set -e
if [ "$got" -eq 1 ] && printf '%s' "$out" | grep -qF "found no changed file"; then
    echo "ok    an empty range is refused rather than reported clean"
    passes=$((passes + 1))
else
    echo "FAIL  an empty range should be refused, got exit $got"
    failures=$((failures + 1))
fi

echo "${passes} passed, ${failures} failed"
cd /
rm -rf "$tmp"
[ "$failures" -eq 0 ]
