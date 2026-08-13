# Prior art

Three projects already move watch state between Jellyfin servers, and four
patterns from outside the Jellyfin world already solve parts of the problem this
plugin has. Reading what each one settled, and what each one left open, is
cheaper than rediscovering it.

## How to read this document

Every claim about another project is either a quotation from that project's own
documentation, marked with quotation marks and attributed, or an observation,
marked with the word observation. An observation is something read out of a
document rather than stated by it, and the most common one here is an absence:
the document does not say. An absence is written as an absence and is never
upgraded into a claim that the project does not do the thing.

Each README was read at a named blob, so the quotation can be checked against
the same bytes rather than against whatever the file says later:

    gh api repos/<owner>/<repo>/readme --jq '.sha'
    gh api repos/<owner>/<repo>/readme --jq '.content' | base64 -d

## Server Sync, a plugin

<https://github.com/JPKribs/jellyfin-plugin-serversync>, README blob
`49ac9d520fdf1d0601e7a7f5478ad98273aa0473`.

That repository's README has moved on since it was read, which is what naming
the blob is for. Every quotation below is against the blob and not against
whatever the file says now:

    gh api repos/JPKribs/jellyfin-plugin-serversync/readme --jq '.sha'
    63c340f8af0ab670a99316ba0b06f067ac1c71bc

The other two are unchanged at the time of writing, checked the same way.

Trust. The credential is a Jellyfin API key generated on the other server and
pasted into this one. The README instructs the operator to "Generate an API Key
on the source server", "Go to Dashboard > API Keys on the source server", and
then to configure "Server URL: The full URL of the source server (e.g.,
`http://192.168.1.100:8096`)" and "API Key: The API key you generated on the
source server". It also states that "No modifications are required on the Source
server", which is the same fact from the other direction: the source server is
not asked to agree to anything, because the key already speaks for it.

This is the credential problem in its purest form, and it is why the first design
constraint of this plugin exists. A Jellyfin API key is not scoped to a plugin or
to an endpoint, so the string that exists to move watch state also opens every
administrative endpoint on the server that issued it. The command that
establishes that about the server is in #11 and is not repeated here.

Matching. By file path. The phrase appears four times in that blob, three of
them emphasised, which is the count rather than an impression of one:

    gh api repos/JPKribs/jellyfin-plugin-serversync/git/blobs/49ac9d520fdf1d0601e7a7f5478ad98273aa0473 --jq '.content' | tr -d '\n' | base64 -d | grep -c -i "by file path"
    4
    gh api repos/JPKribs/jellyfin-plugin-serversync/git/blobs/49ac9d520fdf1d0601e7a7f5478ad98273aa0473 --jq '.content' | tr -d '\n' | base64 -d | grep -c -F '**by file path**'
    3

The unemphasised one is "Content is matched by file path, allowing the plugin to
track what needs to be downloaded, updated, or removed", and one of the three
emphasised is "Source Server watch history is compared, **by file path**,
against the Local Server". Two servers with
different mount layouts therefore cannot be paired at all, and the README's own
Library Mapping section exists to rewrite one layout into the other by hand.

Conflicts. Per field, and mostly last-writer-wins by timestamp: "Played Status
(from the most recently played server)", "Play Count (uses the greater value
between servers)", "Playback Position (from the most recently played server)",
"Last Played Date (from the most recently played server)", "Favorite Status
(always taken from Source Server)". The play count rule is the interesting one,
because taking the greater value is a merge rather than an overwrite, and the
favourite rule is the opposite, an unconditional overwrite in one direction.

Revocation and what happens after. Observation: the README describes no
revocation step and says nothing about what happens to synced data once the two
servers stop being connected. Deleting content is a separate always-on-the-source
question rather than an unpairing one: "Files no longer on Source Server are set
to Delete only when `Delete Missing Content` is enabled (off by default)".

Observation about blast radius, read out of the whitelist section rather than
stated by it: "A whitelisted Collection or Playlist syncs whatever it currently
contains, checked on every Refresh, so anyone who can edit it on the Source
Server controls what syncs." That sentence is the README's own, and it is a rare
and honest statement of a delegation that most such documents leave implicit.

## Jellyfin Server Sync, a plugin

<https://github.com/GermanCoding/jellyfin-server-sync>, README blob
`1352e0d6b7e4c0d6336afe4da61a6e91922158a7`. The README describes itself as
"Alpha stage" and "mostly a proof of concept", and what follows is read as such.

Identifiers. "User ids and media ids must be exactly identical on all Jellyfin
instances", because "Jellyfin generates media ids from the full path & filename.
This means that on all instances the full filename, including path, must be
identical." The recommended way to satisfy that is to make the two servers
identical: "This is best achieved using Docker, as you can use bind mounts to
mount your media folders to the same virtual path inside docker".

Identical identifiers across independently administered servers is not an
assumption a paired-servers design can make. It is the assumption that turns two
servers into one server with two front doors, and it is the thing this plugin has
to do without, because the whole point of pairing is that the two sides were set
up by different people at different times.

Conflicts. "Sync is pretty basic right now and always sends user updates to the
other server, overwriting whatever was there. No merging or other smart sync
logic is currently implemented." Always-overwrite is a conflict rule that loses
data by construction rather than by accident, and the README says so plainly
rather than hiding it.

Deployment. "This plugin does not work with official versions of Jellyfin, due to
limitations in Jellyfin's code. Instead it requires a custom build of Jellyfin
adjusted for my needs". A plugin that needs a forked server is not a plugin an
operator can install, and this plugin's own constraint is the opposite one: it
loads into a stock server or it does not ship.

Trust. Observation: the README names no credential, no enrolment step and no
revocation step, and states no requirement that either side agree to the link.
What it does say about failure is "Errors are not yet handled correctly and can
cause severe issues, e.g if one instance suddenly goes down", which is a
fail-open posture stated as a limitation.

## JellySync, an external tool

<https://github.com/SamVellaUK/JellySync>, README blob
`d2fe5e5d1f1aacfe54cc84dc079988a9c4ea256e`.

Shape. Not a plugin. "Runs in a single lightweight Docker container", with
"Jellyfin Webhook Plugin installed on servers that will send updates".

Trust. It sidesteps trust establishment between the servers by holding both
servers' credentials itself: the prerequisites list "API keys for all Jellyfin
servers", and the configuration file holds a `subscribers` array where each entry
carries a `url` and an `apiKey`. Neither server knows the other exists. That is a
real design, and its cost is that the container is now a single place holding
unrestricted administrative credentials for every server in the set, in a plain
JSON file on disk.

Matching. By provider identifier rather than by path: "JellySync finds the same
content on your other servers (matching by IMDB/TVDB IDs)". Of the three, this is
the matching approach closest to the one this plugin needs, and #38 is where the
precedence between provider identifiers is settled.

Conflicts. Directional, with a freshness guard: "Smart Syncing: Only updates
items if the master server has a newer playback date, preventing overwriting of
newer data", and "Playback positions are synced if the master server has newer
data". Observation: the guard is one-directional, so a position that is newer on
the child is not carried back by that rule.

Topology. "Recommended topology: one master server and one child server with
matching usernames. Multi-server setups and complex user mapping topologies are
supported but have not been extensively tested."

Revocation. Observation: revocation is not a step this tool has. Withdrawing
access means deleting an API key on a Jellyfin server, which is Jellyfin's
mechanism rather than the tool's, and it revokes everything that key could do
rather than the pairing alone.

## The gap all three leave

Observation, and it is the reason this plugin exists. None of the three READMEs
documents a trust establishment step in which both sides agree before anything
flows, a revocation story owned by the link rather than by the credential, or
what happens to data that already moved once the link is broken.

Two of the three use a Jellyfin API key as the inter-server credential, which
makes the second gap follow from the first: there is nothing to revoke that is
narrower than the server itself.

## Four patterns from outside

Each of these is a pattern with published failure modes, which is the point of
preferring one to something invented here.

### Mutual enrolment, where both sides must accept before anything flows

Taken. The enrolment ceremony requires an administrator on each server to
complete a step before any pairing exists, which is #19, and the resulting
pairing is a single object with a state machine that only reaches an established
state through those steps, which is #17. This is the direct answer to the gap
above: in all three prior projects, one side can be enrolled without anyone on
that side doing anything.

Not taken: the transitive part. Some device-enrolment systems let an already
enrolled device vouch for a new one, so trust spreads without a human at each
new edge. This plugin does not, because decision 9 in #1 holds the scope at two
servers and one operator pair, and an enrolment that can be delegated is an
enrolment whose root is no longer the thing #19 makes it. Every other `#`
reference in this file is an issue; that one was a decision number written the
same way.

### A short authentication string compared by a human out of band

Taken, and the string is fixed. It is a fingerprint of the two exchanged public
keys, compared on both dashboards, rather than a code transcribed in one
direction. That was the open half of decision 1 in #1 when this section was
written, and the answer is stated where the protocol is specified rather than
restated here:

    git grep -n "Enrolment is static key pairs" origin/master -- docs/protocol.md
    origin/master:docs/protocol.md:34:worth stating before the tables. Enrolment is static key pairs with a fingerprint

#54 holds the wording and the readability of that comparison. The known failure
mode is the one to design against rather than the cryptography: a human who
clicks confirm without comparing, which is why #54 is about the wording and not
only about the value.

Not taken: a number of digits chosen for convenience. The length is a security
parameter rather than a usability one, and it is pinned with the rest of the
cryptographic building blocks in [`crypto.md`](crypto.md), which also argues why
a longer fingerprint is worse rather than safer.

### Static public keys exchanged with no certificate authority

Taken. This was shape B of decision 1 in #1 and it is the shape that was chosen,
so the long term key pair and the exchanged public keys are what enrolment rests
on. No certificate authority and no third party enters at any point, because
there is none available to two servers run by two people, and the primitives are
pinned in [`crypto.md`](crypto.md) rather than named here.

Taken alongside it, and independent of the key question: the peer is held to the
address the operator approved rather than to whatever address later claims to be
it, which is #22. That is the part the three prior projects have no equivalent
of.

### One-time codes that are single-use, short-lived and rate-limited

Taken, and it is the one pattern that is settled in full. The enrolment window is
small, single-use and fail-closed, which is #18. Single-use means the second
presentation of a code is refused even where the first succeeded; fail-closed
means an error in the check refuses rather than admits.

Not taken: reusable enrolment codes, and standing enrolment. A code that can be
presented twice is a code that can be replayed by whoever read it out of a
support thread, which is the same failure the logging rules in `logging.md` exist
to prevent from the other end.

## What this document does not settle

Nothing here chooses anything. Where a pattern above turns on a fork in the
plan, the section names the fork and points at the document that carries the
answer instead of restating it.

Three passages in this file said decision 1 in #1 was open and named the two
shapes without choosing. It was answered after they were written:

    gh issue view 1 --repo Flowfin/jellyfin-plugin-server-pairing --json comments \
      --jq '[.comments[] | select(.body | contains("## Decision 1:")) | .createdAt] | .[]'
    2026-08-09T02:07:17Z

so they now state the answer. None of them was wrong when it was written, which
is the ordinary way a document here goes stale.

What this document still does not do is read the three prior projects against
what this plugin has since built. Every reading of them above is of their own
documentation at a named blob, and none has been taken again.
