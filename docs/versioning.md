# Versioning

Two servers running different versions of this plugin have to talk to each
other, and they are upgraded by two different people at two different moments.
So a version number here is a statement to somebody else rather than a counter,
and what each part of it promises is fixed before the first release instead of
after the first complaint.

There are three version numbers in play and they are not the same number.

The plugin version is what an operator installs and what a catalog shows. It is
the one this document is mostly about.

The protocol version is an unsigned integer on the wire, and version 1 is
[`protocol.md`](protocol.md). It moves when the bytes two servers exchange
change, and it moves on its own schedule.

The consumer contract version is what a plugin built on this one compiles
against. Decision 6 in #1 made that contract an in-process .NET interface, so a
consumer is bound to the assembly it was built against and a mismatch is a load
failure on the operator's server rather than a message anybody chose the wording
of. #44 owns the contract's own version constant and the set of versions it
accepts.

## Where the plugin version is written

Three files carrying five fields between them, and they say the same thing at
every commit:

    grep '^version:' build.yaml build.net10.0.yaml
    build.yaml:version: "0.1.0.0"
    build.net10.0.yaml:version: "0.1.0.0"

    grep '<Version>\|<AssemblyVersion>\|<FileVersion>' Directory.Build.props
            <Version>0.1.0.0</Version>
            <AssemblyVersion>0.1.0.0</AssemblyVersion>
            <FileVersion>0.1.0.0</FileVersion>

Neither block asks for a line number, and that is a repair rather than a style.
The second one carried `-n` and pasted lines 3, 4 and 5 until a property group
landed above the fields and moved them to 34, 35 and 36, at which point the
paste stopped reproducing. What the sentence above claims is which files hold
the value and that the value agrees across them, and a position in a file is no
part of that: it is a number this document carries, nothing derives, and any
insertion above the field breaks. The first block still reproduced when this was
written and lost its `-n` for the same fragility rather than for a defect of its
own.

`0.1.0.0` is the number the first release carries. It is written into those five
fields ahead of the tag rather than by it, which is the order
[`RELEASING.md`](RELEASING.md) fixes. Nothing has been published from this
repository yet, so the agreement above has never been tested by a release, which
is worth less than it looks.

The publish run refuses a tag whose numeric part is not the `version` in
`build.yaml`, and refuses a build whose stamped assembly version is not that
same value. [`RELEASING.md`](RELEASING.md) is where those refusals are listed;
they are not repeated here.

## What moves the plugin version

The shape is `X.Y.Z.W`, four parts, because that is what a Jellyfin catalog
compares and what the manifest already carries.

**X, the first part, moves when an operator has to do something.** A pairing
that stops working until somebody re-pairs it, a setting whose meaning changes
under an operator's feet, a stored file this plugin can no longer read, or a
protocol version dropped out of the accepted range. This is the only part that
means "read the entry before you upgrade".

**Y moves when something is added that nobody has to act on.** A new protocol
version offered alongside the ones already accepted, a new member on the
consumer contract, a new setting with a safe default, a new endpoint.

**Z moves for a fix that changes no interface.** A refusal that was wrong, a
document that described something the code does not do, a defect in the pairing
path that costs nobody their pairing.

**W is the packaging part.** It moves when the same source is republished, for
instance because a release run produced an incomplete set of assets and
[`RELEASING.md`](RELEASING.md) refuses to touch a release that exists. It never
carries a change to the code.

## What a protocol change does to it

A protocol version is added, or removed, or neither. The plugin version follows
what that does to a pairing.

**Adding a version, keeping the old ones, moves Y.** Two servers pick the
highest version both speak, so a server that has not been upgraded keeps its
selected version and notices nothing. That selection is fixed for the life of
the pairing, which is [`protocol.md`](protocol.md)'s rule and not this
document's.

**Removing a version moves X.** Every pairing that had selected it stops
verifying, and the only repair is a fresh enrolment with a fresh ceremony on
both dashboards. That is an operator standing at two servers, so it is the
loudest thing this scheme can say.

**A change to what a version already means is a removal wearing a smaller
number.** If the bytes under version 1 change, a peer that speaks version 1 and
was never upgraded is now talking to a server that answers differently. Give it
a new protocol version and keep the old one accepted, or accept that it moves X.

Every one of the three carries a `[protocol]` line in
[`../CHANGELOG.md`](../CHANGELOG.md).

## How long a previous protocol version is accepted

The rule, so it is not decided per release by whoever is cutting it:

**The version before the current one is always accepted.** A server one protocol
version behind pairs, verifies and keeps working. Nothing removes that.

**A version two behind may be dropped**, in a release that moves X and carries a
`[protocol]` line saying which version went and what an operator does about it.

That is a rule about protocol versions and not about time or about plugin
releases, because the thing an operator is actually behind is the protocol
rather than the calendar. Two servers that sat unpatched for a year are fine as
long as neither is two protocol versions behind the other, and two servers a
week apart across a version removal are not.

The floor under all of it is that a pairing pins its version at enrolment. A
release that drops a version is therefore not a compatibility question a running
pairing can renegotiate its way out of; it ends that pairing. That asymmetry is
why the previous version has no expiry here.

## What a consumer contract change does to it

A consumer compiles against this plugin's assembly, so the operator sees a
contract break as a plugin that fails to load rather than as a message.

**Adding a member moves Y.** Existing consumers keep loading.

**Removing or changing a member moves X**, because every consumer built against
the old shape stops loading until its author ships a new build and the operator
installs it. That is two people and two releases before the operator's server
works again, which is the most expensive thing in this document.

Both carry a `[contract]` line in [`../CHANGELOG.md`](../CHANGELOG.md).

## Breaking for an operator is not breaking for a consumer author

They are different audiences and the same release can be one and not the other,
which is why the changelog marks the lines rather than the releases.

An operator is broken when a pairing they had stops working, when they have to
touch both dashboards again, or when a setting they wrote no longer means what
they wrote. They read the plugin version and the unmarked lines.

A consumer author is broken when the interface they compiled against changed
shape. They read the `[contract]` lines, and the plugin version tells them
whether their existing build survives.

A release can move X for an operator without touching the contract, and it can
move X for a contract change that no operator would notice until a consumer
plugin they installed stops loading. The version has one number for both, so the
marked lines carry the difference.

## The entry comes before the number

The changelog entry and the version bump are one change, in this order: write
the entry, copy it into the `changelog` field of both manifests, then raise the
version in the five fields above. [`RELEASING.md`](RELEASING.md) carries that as
the first step of cutting a release, and it names the same five.

A version raised in one commit and described in another leaves a published
version whose entry has to be reconstructed from a list of commits, which is the
reconstruction nobody does. [`.github/pr-hygiene.sh`](../.github/pr-hygiene.sh)
refuses a manifest version change that arrives with no changelog entry, and
refuses a protocol or contract change that arrives with no line marked for it
and no declaration in its body that nothing a peer or a consumer can see moved.

## What this document does not decide

The contract's own version constant and its supported-version set, issue #44.
This document says what a contract change does to the plugin version; what the
contract declares about itself is that issue's.

Which server lines a release supports. That is decision 7 in #1, answered as
both, and the two manifests above are what carries it.

Whether a given change is breaking. No command decides that. It is read out of
this document by a person before the version moves, and the release list in
[`release.md`](release.md) is where that reading is recorded.
