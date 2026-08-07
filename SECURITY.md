# Security policy

This plugin establishes trust between two Jellyfin servers and holds the key
material that trust rests on. A defect on that path is not something an operator
can see, and it is not something they can recover from once it has been used. So
a report about it goes somewhere private before it goes anywhere else.

## Reporting a vulnerability

Do not open a public issue for one. An issue in this repository is world
readable from the moment it is filed, and a description of how to forge a
pairing request is worth more to whoever reads it first than to the person
fixing it.

Use GitHub's private vulnerability reporting on this repository instead. It is
enabled:

```
gh api repos/iderex/jellyfin-plugin-server-pairing/private-vulnerability-reporting --jq '.enabled'
true
```

From a browser it is the "Report a vulnerability" button under this repository's
Security tab.

An ordinary bug that is not a security defect belongs in a public issue, and
this policy is not a reason to route it privately.

## What is in scope

This plugin, which is everything in this repository: the pairing protocol, the
key store, the endpoints the plugin adds, its dashboard page, its build and
packaging, and the claims its documents make.

The Jellyfin server itself is not in scope here, and neither is the web client.
Those go to the Jellyfin project, whose own policy is at
<https://github.com/jellyfin/.github/blob/master/SECURITY.md>. That document
states its own supported versions, its triage rules and its reporting address,
and none of them are restated here so that there is no second copy to go stale.

A report about a plugin that consumes this one belongs to that plugin's own
repository, unless the defect is in what this plugin hands it.

## What a reporter can expect

An acknowledgement that the report arrived and was read.

An assessment: whether it is a defect, what it reaches, and whether the threat
model already covers it.

Either a fix, or an explanation of why there will not be one. A report that is
judged out of scope gets the reasoning rather than silence, because the
disagreement is worth having in writing.

No timeline is promised here. A policy that names one it cannot keep is worse
than one that names none, and this project has no release history to base a
number on.

## Credit

A reporter is credited by name in the advisory and in the changelog entry for
the fix, unless they ask not to be. Ask, and the fix ships without the
attribution.

## Supported versions

No release has been published from this repository yet, so no version receives
fixes today and the table that would list them would have no rows. This section
is the honest statement of that rather than a placeholder that reads as a
promise.

The first release replaces this section with the versions it supports. The
server line the plugin builds against is `targetAbi` in
[`build.yaml`](build.yaml), which is the manifest the packaging reads, and it is
not copied here.

## What is already known and accepted

Three classes of finding are out of scope by design rather than by oversight,
and they are argued in the threat model rather than restated here:
[out of scope](docs/threat-model.md#out-of-scope) names a compromised host
operating system, a malicious administrator on either side, and traffic
analysis.

The threat model is also the place to check before reporting anything else. It
says which mechanisms exist, which are owed by a milestone, and which are
design positions with nothing behind them yet, and at the time of writing it
opens by saying that none of the mechanisms it describes is enforced, because
there is no pairing, no key store and no endpoint in the tree. A report that a
mechanism named there does not refuse anything is a report that the tree already
makes in words.
