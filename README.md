> [!NOTE]
>
> **Part of [Flowfin](https://github.com/Flowfin).** It works with any Jellyfin
> server, and with the Flowfin clients.

# Server Pairing

Server Pairing links two Jellyfin servers under a credential of its own, which
this plugin holds and which nothing else on either server accepts. It moves no
watch state, no metadata and no playlists of its own: it is the foundation the
sync plugins build on, and everything that actually travels between the two
servers belongs to them.

## What it does not do

It syncs nothing. A server carrying only this plugin can be paired and transfers
nothing, and that is the intended state rather than an unfinished one.

It does not match an item that carries no provider identifier. Nothing is
matched by title, by year, by runtime or by file name, and no setting turns that
into a guess, so a library of home video, of personal recordings or of anything
ripped without metadata will not be matched for whatever builds on this plugin.
[`docs/matching.md`](docs/matching.md) argues that and lists the identifiers the
matcher does read.

It pairs two servers under one operator, and nothing else. Pairing more than
two, pairing across operators, and pairing to something that is not a Jellyfin
server are all outside what it is for.

It has no unattended mode. An administrator at each server confirms the pairing
by hand, and there is no way to establish one from a script. What that costs is
said rather than hidden: an operator who wants a pairing without a person cannot
have one.

## What moves between two servers, and that it is personal data

[`docs/data.md`](docs/data.md) is the personal-data statement: every field that
crosses between two paired servers, where it comes to rest, and what an operator
does to remove it. The wire it describes is specified in
[`docs/protocol.md`](docs/protocol.md), and what an adversary reaches is in
[`docs/threat-model.md`](docs/threat-model.md).

The fields themselves are not listed here. They are in the document that owns
them, and a second copy in this file is the copy that would go stale.

Ending a pairing removes what arrived from the other server, on the side that
ends it. That is worth reading before pairing rather than afterwards, because
what it does not reach is the half operators assume it does: whatever this
server has already sent is sitting on the other machine, and nothing sent from
here brings it back or deletes it there. The other operator ending the pairing
on their side is what removes it, and they are as able to do that as you are.

The removal on this side is not performed by this plugin. It holds the pairing
and the mapping table; a sync plugin holds whatever it wrote from a transfer, so
it is the one that deletes those rows when the pairing ends. That is a
requirement this plugin's contract puts on every sync plugin built against it
rather than a hope, and it is why every synced row has to record the pairing it
arrived under. The longer version is in [`docs/data.md`](docs/data.md), which
also says what it does not answer. What disabling, uninstalling and reinstalling
leave behind, and the file an operator deletes by hand, is
[`docs/lifecycle.md`](docs/lifecycle.md).

Neither of those happens yet, for the same reason nothing in the next section
does.

## Pairing

Two administrators, one at each server, and a value each of them compares
against what the other has in front of them. That comparison is what the whole
arrangement rests on, and it is the one step no software performs on their
behalf. [`docs/threat-model.md`](docs/threat-model.md) states what is lost by an
operator who confirms without comparing.

Nobody can carry that out today. The one endpoint this plugin adds is the peer
plane, which a stranger reaches and an operator does not:

    git grep -lE 'ControllerBase|\[ApiController\]' -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs
    exit=0

There is nothing an administrator can call to open an enrolment; its
configuration page is still the plugin template's example fields:

    grep -c 'AnInteger\|Several Options\|A String' Jellyfin.Plugin.ServerPairing/Configuration/configPage.html
    6

and no release has been published from this repository. So this section links no
walkthrough rather than linking one that does not exist; the walkthrough from
installation to a working pairing is issue #75 and is not written.

## Which server versions it supports

Two server lines, one package for each. Which line a package is built for, and
the oldest server it claims to load on, are the `framework` and `targetAbi`
entries in [`build.yaml`](build.yaml) and
[`build.net10.0.yaml`](build.net10.0.yaml), which are the manifests the
packaging reads and the ones a server itself reads. Those values are not copied
into this file, because a supported-server list kept in two places is a list that
disagrees with itself eventually, and this is where that usually starts.

Which released versions of the plugin receive fixes is
[`SECURITY.md`](SECURITY.md). Today the answer there is none, because there has
been no release.

## Where the rest is written

- [`docs/protocol.md`](docs/protocol.md), the wire: the states, the messages,
  what is authenticated over which bytes, freshness, and the error taxonomy
- [`docs/threat-model.md`](docs/threat-model.md), the assets, the adversaries,
  and what each one reaches
- [`docs/crypto.md`](docs/crypto.md), the cryptographic building blocks, pinned
  in one place
- [`docs/data.md`](docs/data.md), what crosses the wire, where it rests, and how
  an operator removes it
- [`docs/matching.md`](docs/matching.md), how an item on one server is matched to
  an item on the other, and what that costs
- [`docs/mapping.md`](docs/mapping.md), who on this server is who on the peer,
  why nothing infers that, and what ends a mapping
- [`docs/endpoints.md`](docs/endpoints.md), how the host authenticates a
  dashboard request and what forgery is possible against it
- [`docs/keystore.md`](docs/keystore.md), where the key material is kept, what
  protects it and what does not
- [`docs/lifecycle.md`](docs/lifecycle.md), what disabling, uninstalling and
  reinstalling leave behind, and what an operator has to delete by hand
- [`docs/logging.md`](docs/logging.md), what is logged and what may never be
- [`docs/testing.md`](docs/testing.md), the three kinds of test this repository
  refuses and what replaces each one
- [`docs/prior-art.md`](docs/prior-art.md), what the earlier attempts at this
  problem did and where each of them stops
- [`docs/release.md`](docs/release.md), the checklist a release is cut against
- [`docs/RELEASING.md`](docs/RELEASING.md), how a release is actually published
  and what the run refuses on its own
- [`docs/versioning.md`](docs/versioning.md), what each part of the version
  number promises and how long a protocol version is accepted

What changed in each released version is [`CHANGELOG.md`](CHANGELOG.md), which
also states its own format and the two markers a consumer author scans for.

That list is every document under `docs/`, and it is checkable rather than
trusted:

    git ls-files 'docs/*.md' | wc -l
    15

## Contributing

Every change here starts as an issue and lands as a pull request.
[CONTRIBUTING.md](CONTRIBUTING.md) is where that is set out: what an issue has to
say, what a pull request body has to carry, which commands run locally the same
things the checks run, and the rule that a claim carries the command that
produced it. Read it before opening either.

Every commit carries a `Signed-off-by` trailer asserting the Developer
Certificate of Origin, whose text is in [DCO](DCO). Read it before you sign it.
The sign-off check refuses a pull request that contains a commit without a
matching trailer.

[CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) applies to every space this project
uses.

## Security

[SECURITY.md](SECURITY.md) is where a vulnerability in this plugin's pairing
path is reported, and it is not a public issue. It also says what is in scope,
what a reporter can expect back, and which classes of finding the threat model
already accepts as out of scope.

## Licensing

GPL-3.0. The text is in [LICENSE](LICENSE), and the intended-use notice is in
[NOTICE.md](NOTICE.md).
