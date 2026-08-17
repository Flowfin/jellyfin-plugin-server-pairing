# Changelog

One line per user-visible change, under the version that first carries it,
written in the words an operator would use rather than the words the commit
used. A change nobody outside this repository can see does not belong here.

The rules the version numbers follow are in
[`docs/versioning.md`](docs/versioning.md). How a release is cut, and where the
entry has to be before the version moves, is in
[`docs/RELEASING.md`](docs/RELEASING.md).

## How an entry is written

Newest version first. Each version is a heading of the form `## X.Y.Z.W`,
spelled exactly as `version` in `build.yaml` for that release, and unreleased
work sits under `## Unreleased` until a release takes it.

A line that changes what the two servers say to each other on the wire carries
`[protocol]`. A line that changes the interface a consumer plugin compiles
against carries `[contract]`. Both markers are there so that somebody
maintaining the other side of a pairing, or a plugin built on this one, can find
what affects them without reading the whole entry:

    - [protocol] A peer running version 1 is no longer accepted. Re-pair both
      servers before upgrading the second one.

Those two markers are read by a machine and not only by a person.
[`.github/pr-hygiene.sh`](.github/pr-hygiene.sh) refuses a pull request that
touches the protocol or the consumer contract and adds no line carrying the
marker for what it touched.

Anything else is an ordinary line with no marker.

## Unreleased

No release has been published from this repository, and nothing in the tree yet
changes what an operator sees on a server that installs the plugin. The first
release replaces this section with what it carries, and the `changelog` field in
`build.yaml` and `build.net10.0.yaml` carries the same words.

- [protocol] A refused request can now say that its nonce was already seen, or
  that the pairing has no room to remember another one, instead of both arriving
  as the one undistinguished refusal. Nothing implements either code yet, so a
  server installed today answers exactly as it did before, and the line is here
  because the specification of the wire moved.
- [protocol] An enrolment window now has bounds: it lasts ten minutes, it closes
  the moment one peer key is accepted through it, and it closes after three
  failed attempts. A peer that arrives late, twice, or after guessing is refused
  in the one shape a stranger is always refused in. Nothing opens a window yet,
  so a server installed today answers exactly as it did before.
- [protocol] The versions this build speaks are now written down in one place,
  and a pairing between two servers that speak different sets settles on the
  highest version both of them have rather than on the oldest either of them
  remembers. Two servers with no version in common are not paired at a version
  one of them cannot speak. The set has one member today, so nothing an operator
  can see changes, and nothing calls the selection yet.
