# Contributing

This plugin pairs two Jellyfin servers and holds the credential that lets them
talk. Most of what follows exists because a mistake in that path is not
recoverable by the operator who made it.

## Before anything else, run what CI runs

From the repository root:

```
dotnet build
dotnet test
```

`dotnet build` is clean or the change is not finished. Warnings are errors here,
the security analyzer rules are on, and no suppression is an acceptable way to
get to a clean build. If a rule is wrong, argue with the rule in an issue and
change the rule, in `jellyfin.ruleset` or in `Directory.Build.props`, where the
change is visible to everyone.

`dotnet test` has something to run. The test project is
`Jellyfin.Plugin.ServerPairing.Tests` and it is in the solution, so a run from
the root reaches it rather than finding nothing and exiting 0. What belongs in
that project, and which three kinds of test this repository refuses outright,
is in [`docs/testing.md`](docs/testing.md).

Both commands need an SDK that `global.json` accepts. What it pins comes with the
command that prints it, because a version asserted in a sentence is the copy that
goes stale while the file moves:

```
cat global.json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

Run them from the repository root. `global.json` is found by walking up from
wherever the command is run, so the same solution built from a directory with no
`global.json` above it never sees the pin at all, and a machine whose SDK the pin
would refuse would build there and appear to pass the gate.

The refusal itself is a claim here rather than a measurement. On the machine this
was written on the only installed SDK is 10.0.301, which `rollForward:
latestFeature` accepts, so the pinned version cannot be made to refuse anything
without removing an SDK:

```
cd .../jellyfin-plugin-server-pairing && dotnet --version ; echo "exit=$?"
10.0.301
exit=0
```

An earlier version of this passage pasted a refusal naming SDK 9.0.300, which is
what `global.json` pinned before the plugin was multi-targeted. Neither the
output nor the exit code reproduced at this commit, which is the reason the
version is now read out of the file instead of quoted beside it.

## No change without an issue

Every change starts as an issue and lands as a pull request.

An issue says what is wrong, what the evidence for that is, and what makes it
done. If the evidence is a number, it carries the command that produced it, so a
reader can run it and get the same number. An issue that describes a wish rather
than a problem is a wish, and it will sit.

The done condition is the part worth spending time on. It is what somebody else
uses to decide whether the issue may be closed, and an issue closed with its done
condition unmet costs more than one left open.

## Every claim carries the command that produced it

This applies to issue bodies, pull request bodies and commit messages, not only
to code. Run the command at the commit you are asking somebody to read, against
the reference they will have rather than against your working tree. Reading your
own checkout and reporting it as the state of the branch is the most common way
this goes wrong.

Where a claim cannot be backed by a command, write it as a claim and say so.
"Verified", "not measured" and "not evaluated here" are three different
statements and this repository treats them as three different statements.

A guard is proven by the failure it refuses. Adding a check without showing it
refusing something, and showing the same input passing once the check is removed,
proves nothing about the check.

## What a pull request carries

Four things are wrong often enough, and cheaply enough to detect, that they are
meant to stop a pull request rather than earn a note:

- the body references the issue it belongs to, by number
- every commit subject references an issue
- a change to the version in the manifest comes with a changelog entry in both
  places that hold one: [`CHANGELOG.md`](CHANGELOG.md), which whoever works here
  reads, and the `changelog` field of the manifests, which is the only text an
  operator browsing a catalogue is shown and which ships inside the package
  where it cannot be edited afterwards
- a change to the wire protocol or to the consumer contract comes with a
  changelog line marked `[protocol]` or `[contract]`, so the operator on the far
  side of a pairing and the author of a plugin built on this one can each find
  what affects them. [`CHANGELOG.md`](CHANGELOG.md) states the markers and
  [`docs/versioning.md`](docs/versioning.md) states what each kind does to the
  version number

What that fourth rule reads is a set of paths, and the paths are a proxy for the
subject. A change inside them that alters nothing a peer or a consumer can
observe earns no changelog line, because a change nobody outside this repository
can see does not belong in `CHANGELOG.md` at all. Say so on a line of its own in
the body:

```
No protocol change: the loop is rewritten to close a static-analysis alert and
no byte on the wire moves.
```

`No contract change:` is the same for the other kind. A reason is required, the
line has to start the line, and declaring one kind does nothing for the other.
Nothing verifies the declaration, exactly as nothing verifies that a `[protocol]`
line says what the change did. What it buys is that an untrue one stays in the
pull request instead of landing in the file operators read.

Three more belong in the body and are judged by a reader rather than by a
machine:

- what changed and what failure that prevents
- the commands that were run, with their output, at the head of the branch
- what is not covered: the thing you did not measure, the platform you did not
  try, the assumption you are leaving in place

Two things are worth noticing and are not worth failing over. A large diff,
because a legitimate change can be large and a size cap that fails is a size cap
that gets worked around. A change to the plugin source with no change to the
test project, because the exceptions are real and frequent, and failing on it
teaches people to add an empty test.

A negative statement in a body stays negative through every later edit of that
body. If a passage says something was not done, it is not rewritten later into
saying it was.

The first four are read by [`.github/pr-hygiene.sh`](.github/pr-hygiene.sh),
which the `pr-hygiene` workflow runs on every pull request and which you can run
yourself before pushing:

```
PR_BODY="what you are about to put in the body" \
PR_AUTHOR_TYPE=User PR_AUTHOR_ASSOCIATION=OWNER \
BASE_SHA=$(git merge-base origin/master HEAD) HEAD_SHA=$(git rev-parse HEAD) \
sh .github/pr-hygiene.sh
```

The refusing tier does not apply to a bot, which fills a template rather than
writing a body, or to an author from outside this repository, who is meeting
these rules for the first time at the moment the check reds. Both skips are
written at the point in the script that takes them.

That check is in the required set on the default branch, so a red run blocks a
merge. Read that set rather than this sentence. It is a repository setting, no
change in this tree can add to it, and it moves without any commit here, which is
why no copy of it is kept beside the reading:

```
gh api repos/Flowfin/jellyfin-plugin-server-pairing/rulesets/20464076 --jq '[.rules[] | select(.type=="required_status_checks") | .parameters.required_status_checks[].context]'
```

These lines said the check was not required until that setting moved. A copy
would have hidden the move; the command does not.

What blocks is the refusing tier and nothing else. The two annotating rules print
and never fail, by design rather than by omission, so what they report still
reaches the mainline unrefused. The three items above them that a reader judges
are refused by nobody at all.

## One topic per commit and per pull request

A commit message states what changed and what failure it prevents. Where it is
correcting something, it says what was wrong and how that was found.

A change too large to read is usually an issue whose scope was planned wrong.
Splitting the finished diff into two pull requests that only make sense together
satisfies the size and defeats the point. Re-plan the issue into smaller issues
instead, each with its own reason to exist and its own done condition.

## Sign your work

Every commit carries a `Signed-off-by` trailer matching its author:

```
git commit -s
```

The trailer asserts the Developer Certificate of Origin, whose text is in
[`DCO`](DCO) in this repository. Read it before you sign it. The sign-off check
in `.github/workflows/dco.yml` refuses a pull request containing a commit
without a matching trailer, and it is the whole of the enforcement: nothing else
in the tree reads the trailer.

It walks non-merge commits only, and it skips commits authored by the two
GitHub bot identities it names, which cannot sign their own work. Every commit
a person writes is walked. Read the check itself for the exact list rather than
this sentence, which is a summary of it.

Commits are signed cryptographically as well as signed off. A signing failure is
a reason to stop and fix the signing, never a reason to reach for
`--no-gpg-sign`.

## Code of conduct

[`CODE_OF_CONDUCT.md`](CODE_OF_CONDUCT.md) applies to every space this project
uses. It is not decoration.

## Security reports

Do not open a public issue for a vulnerability in the pairing path.
[`SECURITY.md`](SECURITY.md) says where one goes, what is in scope, what a
reporter can expect and which classes of finding are already accepted as out of
scope.
