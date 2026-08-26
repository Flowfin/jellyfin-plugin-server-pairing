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

## 0.1.0.0

The first release, so nothing under this heading is a difference from a
published version: there is none. The lines below were written one at a time as
the wire specification moved, which is why each of them says what a server
installed today does rather than what an operator has to act on.

What an operator gets by installing this version is a plugin that answers on the
five pairing paths and refuses every request that reaches them. Nothing
verifies: the check that reads an arriving request is given the key source of a
server that holds no keys, and there is no page and no endpoint an administrator
can open an enrolment from. Two servers cannot be paired with this version. The
`changelog` field in `build.yaml` and `build.net10.0.yaml` carries that
paragraph in the same words.

- [protocol] A sixth path is no longer served. Beside the five the specification
  fixes, the host routed a request at `/ServerPairing` itself, under no method
  constraint and with nothing of the server's credentials asked for, to a helper
  the test suite drives rather than to the refusal the five paths give. It was
  never a path the specification names: a public method on a controller is an
  endpoint whether or not it says so, and this one did not say otherwise. It says
  so now. What such a request was answered with was never measured on a running
  server and nothing here claims it.
- [protocol] A server running this plugin now answers on the five pairing paths the
  specification fixes, and every answer is the refusal a stranger gets. A request
  whose path carries a trailing slash, a query string, a percent-encoded byte or a
  different case is refused rather than read as one of the five; a body over the
  limit for its message is refused without being read past that limit; and nothing
  a request carries is looked at past its signature. Nothing verifies, because the
  check that reads an arriving request is given the key source of a server that
  holds no keys, so no pairing exists and the answer is the same refusal in every
  case rather than a working handshake.
- [protocol] The specification's explanation of how long a nonce is remembered
  was wrong and is corrected. It said 600 seconds is the window in both
  directions plus the window again; 600 seconds is the window taken in both
  directions and nothing more, and that span is the widest gap there can be
  between the first arrival of a request and the last instant a copy of it would
  still be inside the window. The number on the wire is unchanged, so a server
  behaves exactly as it did before and only the reasoning a far-side
  implementer would derive from the document moves.
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
