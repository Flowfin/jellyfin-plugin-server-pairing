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

Both commands need the SDK that `global.json` pins. Where a different one is
installed they do not start at all, and the error says which version was asked
for, so a machine that cannot run the gate says so instead of appearing to pass
it.

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

Three things are wrong often enough, and cheaply enough to detect, that they are
meant to stop a pull request rather than earn a note:

- the body references the issue it belongs to, by number
- every commit subject references an issue
- a change to the version in the manifest comes with a changelog entry

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

Nothing enforces any of this today. The check that would read a pull request is
issue #65 and it is not built, so a pull request that carries none of the above
passes every route in this repository. The three tiers are written here in the
shape that issue specifies, so that the check and this file agree when it lands,
and until then this section describes what is expected rather than what is
refused.

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
`SECURITY.md` does not exist yet and the reporting address is not fixed. Until it
is, use GitHub's private vulnerability reporting on this repository.
