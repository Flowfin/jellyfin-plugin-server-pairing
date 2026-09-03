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

What it reads is a set of paths, and a change inside them that moves nothing a
peer or a consumer can see has no line to add here, by the rule at the top of
this file. Such a change says so in its own body instead, on a line reading
`No protocol change:` or `No contract change:` with the reason, which is where a
claim that cannot be verified belongs rather than in this file.
[`CONTRIBUTING.md`](CONTRIBUTING.md) carries the wording.

Anything else is an ordinary line with no marker.

## 0.1.0.0

The first release, so nothing under this heading is a difference from a
published version: there is none. The lines below were written one at a time as
the wire specification moved, which is why each of them says what a server
installed today does rather than what an operator has to act on.

What an operator gets by installing this version is a plugin that answers on the
six pairing paths and refuses every request that reaches them. Nothing
verifies, and the reason is that a server has no key to verify against rather
than that nothing looks: the check that reads an arriving request looks the
pairing up in this server's key store, and nothing puts a key there, because
there is no page and no endpoint an administrator can open an enrolment from.
Two servers cannot be paired with this version. The `changelog` field in
`build.yaml` and `build.net10.0.yaml` carries that paragraph in the same words.

- An administrator can ask what this plugin holds about one local user, at
  `/ServerPairing/Administration/users/{localUserId}`, and is answered with every
  mapping for that user across every pairing, the cached peer display name marked
  as a cache. Nothing removes what it reports yet, and what the peer server holds
  about that user is the peer operator's to remove either way.
- [protocol] `unpair` is a sixth message on the pairing plane, at
  `/ServerPairing/unpair`, carrying nothing beyond the envelope and answered
  empty. A peer whose operator is finished with a pairing sends it before ending
  its own side, and a server that receives a verified one completes its own side
  without asking its operator, exactly as it does for `revoke`; what it records
  names `unpair` rather than `revoke`, so an operator can tell the two apart
  afterwards. Nothing on a server sends one or acts on one yet: the path is served
  and refuses like the other five, for want of a key rather than for want of a
  route. It is a message of protocol version 1, because no version of this plugin
  has been published for a peer to be behind.
- The plugin's configuration page now says whether an enrolment window is open on
  this server, and an administrative endpoint answers the same question for
  anything else that asks. A window an operator opened and forgot is the failure
  the enrolment bounds exist against, and until now the only place a server said
  one had been opened was a log line written once at the moment it opened. What
  this version answers is empty on every server, because nothing yet joins the
  window to the record it would be reported from, and the page says an operator
  has no window open rather than saying nothing.
- [protocol] A request that arrives with a correct signature is now judged for
  freshness before this server acts on it. One whose timestamp is further from this
  server's clock than the tolerated skew is answered `clock`, one carrying a nonce
  already seen for that pairing is answered `replay`, and one arriving when that
  pairing has no room left to remember another nonce is answered `busy`. A captured
  request sent again is refused instead of being served. Only a peer that has
  proved it holds the pairing's key is told which of the three happened; every
  other caller gets the same refusal as before, because freshness is judged after
  the signature and never before it. An operator whose two servers disagree about
  the time now reads a clock refusal rather than debugging a signature error. As
  with every other line here, nothing on a server produces this yet: no route puts
  a key into a key store, so nothing verifies, and what changed is what a server
  will answer rather than what one answers today.
- [protocol] A pairing's state is now kept in a file of its own, beside the key
  store and under the same permissions, so what a server believes about a pairing
  survives a restart instead of living only in whatever object happened to hold
  it. A pairing that has been offered but not yet answered is held under an
  identifier this server mints for itself, which no peer can ever name, and it is
  retired the moment the two public keys derive the real one. Nothing on a server
  writes a record yet: no enrolment exists to put a pairing into any state, so
  what changed is what a server can remember rather than what one remembers today.
  An operator will find two files in this plugin's directory rather than one, and
  both move together.
- [protocol] A refusal that says the two servers have no protocol version in
  common now carries the range this server speaks, as `versionLow` and
  `versionHigh` beside the code, in the same member names a pairing request uses
  for the same two numbers. Every other refusal is unchanged and still carries the
  code alone. Nothing on either server produces this refusal yet: no route on the
  pairing plane negotiates a version, so what changed is what a server will answer
  with rather than what one answers with today. An operator whose two servers are
  on versions that do not overlap gets the numbers to compare instead of a refusal
  they can do nothing with, and the numbers were already public: they are what a
  pairing request advertises.
- A key store file that is there and is not a key store is now refused instead of
  being read as an empty one. Truncated bytes, a partial overwrite, something
  that is not JSON, JSON that is not the shape this plugin writes, and an
  envelope whose pairings are not pairings all produce the same answer: no
  pairing works, and the message names the file and says to move it aside and
  keep it rather than to pair afresh over it. That last part is the whole of the
  change. An empty store is what a fresh installation has, so a damaged one that
  read as empty told an operator their pairings were gone and invited them to
  make that true. Nothing is repaired, truncated or rewritten on the way to the
  refusal, including for a file in the older format, which is read before it is
  carried up rather than after. A store this plugin has never written to still
  answers with nothing, and a store in a format newer than this build is still a
  separate refusal with its own message. What is still not detected is a file
  that is an intact key store and the wrong one: a store restored from a backup,
  or a copy of another server's, holds well-formed keys and cannot be told from
  the store it came from.
- An administrator can ask this server what it has refused on the pairing plane
  and why, at `GET /ServerPairing/Administration/diagnostics`. It answers one
  number per refusal code the protocol defines and one per cause this server can
  tell apart, which is what separates a peer sending too fast from a scanner on
  the wrong path from a peer whose signature does not verify. Nothing a peer is
  told changes: every one of those causes is still answered with the same
  refusal, so the split is visible to an operator and to nobody else. The answer
  names no pairing, no address and no person, and it requires the same elevation
  as the rest of the dashboard. What it does not carry is a state per pairing,
  the version each side speaks or a last error, because nothing in this version
  produces any of the three.
- An administrator can ask this server which pairings it holds a key for, at
  `GET /ServerPairing/Administration/pairings`, which answers the identifiers and
  nothing else. Until now that question was answered only by a line written once
  at startup, so a server that had been running for a week answered it only in a
  log file nobody kept. The endpoint requires the same elevation as the rest of
  the dashboard. It changes nothing and there is still no way to make a pairing,
  so on a server today it answers an empty list.
- [protocol] A request arriving from a peer is now verified against the key this
  server holds for the pairing it names, and against the key a rotation has just
  replaced while the overlap for it is open. Nothing an operator can reach puts a
  key in that store yet, so every request is still refused; what this removes is
  the case where a peer signing correctly under a key both servers hold would have
  been refused anyway, and the case where a peer that had not yet caught up with a
  rotation would have been refused for the length of the overlap. A pairing this
  server does not hold and a signature that does not verify stay one answer, so
  nothing here tells a stranger whether a pairing exists.

- [protocol] How far an arriving request's timestamp may be from this server's
  clock is now an operator's setting rather than a fixed five minutes, with a
  quarter of an hour as the widest it accepts. Two servers disagree by seconds
  without anything being wrong and by minutes when one of them has no time source,
  which is what the setting is for; what a second added to it buys is a second in
  which a request captured off the network is still worth sending. A value outside
  its range is refused with the setting named and the server keeps the span it
  ships with rather than being narrowed to the widest accepted one. How long a
  repeated request is remembered follows the setting and is not a second setting,
  so the two cannot be put into a state where a repeat is forgotten while it would
  still be accepted. A server nobody has configured behaves exactly as before, and
  nothing in this version consults the window, so the setting is read and refused
  and reaches nothing yet.
- [protocol] How long an enrolment window stays open is now an operator's setting
  rather than a fixed ten minutes, with half an hour as the longest it accepts. A
  value above that is refused with the setting named and the window keeps the
  length it ships with, rather than being shortened to half an hour, because a
  window that closes while an operator is still reading an address out is a
  failure they have nothing to look for. The far side of a pairing sees it as how
  long their own request has to arrive. A server nobody has configured behaves
  exactly as before. Nothing in this version opens a window, so the setting is
  read and refused and reaches nothing yet.
- [protocol] How many pairing requests may arrive inside a window is now an
  operator's setting rather than a fixed number: the length of the window, the
  allowance one pairing identifier has inside it, and the harder allowance the
  identifier every enrolment carries has. A server nobody has configured behaves
  exactly as before, and a configuration file written by an older build keeps
  behaving that way, because a setting the file does not mention keeps the value
  the plugin ships with. A value outside its range is refused with the setting
  named and the plane runs on the value it ships with until the value is one the
  plugin accepts, and an enrolment allowance set above the pairing allowance is
  refused for the same reason it is the harder of the two. Raising an allowance
  does not make a pairing faster; it makes a flood claiming that identifier
  cheaper.
- [protocol] The plugin configuration now carries the address of the peer this
  server pairs with, and a setting saying whether an `http` address may be used
  for it. A fresh installation has no peer address and the acknowledgement off,
  which is a server that pairs with nobody and refuses a cleartext address. An
  address outside the forms the specification fixes is refused when the
  configuration is read, with the setting named and the value left exactly where
  it was put rather than corrected to something else, and the plugin stays loaded
  and does not pair. The refusals are written to the log at Error when the server
  starts, and a server whose acknowledgement is on writes one line at Warning on
  every start saying that request and response bodies, the mapping table among
  them, are readable by anything on the path between the two servers. Neither
  setting is on the settings page yet, so both are edited in the configuration
  file for now.
- The plugin's configuration file now carries the number of the format it is in,
  the way the key store's file already does. It is written by the plugin rather
  than set by an operator, and a file that mentions no number is read as the shape
  every build up to this one wrote, so an existing configuration keeps working and
  gains the number the next time it is saved. A configuration written by a newer
  plugin is refused: the server does not pair and names the member at Error when it
  starts, and the file is left exactly as it was rather than being written back
  with whatever this build could not read dropped out of it. That is the case the
  number exists for, and it is an operator who installed a newer plugin, configured
  it, and then rolled the plugin back. A number below zero, which no build of this
  plugin writes and which can only have been typed into the file by hand, is refused
  too and with a different sentence, because the way out of that one is to set it back
  rather than to install anything.
- The key store's file now carries the number of the format it is in, and a
  plugin that meets one written by a newer plugin refuses it instead of reading
  the parts it recognises. Every pairing stops working until the newer plugin is
  installed again or the file is moved aside, and the message says which format
  was found and which this build understands. A file written before this version
  is carried up to the new shape the first time it is read, with a copy of what
  was there left beside it named for the format it is in. One line is written to
  the log when that happens, naming both formats and the copy, and nothing removes
  the copy afterwards. A migration that fails
  part way leaves the original file exactly as it was and the plugin refusing to
  pair, rather than running on half of one.
- [protocol] A pairing request is now refused when too many have already arrived
  claiming the same pairing identifier: sixty inside a minute, and six for the
  identifier every enrolment carries, which every enrolment shares. It is
  answered with the same refusal every other caller gets, it is counted against
  the identifier the request claims rather than one it has proved, and the
  allowance comes back a minute after the first request that spent it. Nothing
  on this server was measured under a flood; what the limit is for is that a
  stranger cannot make this server compute a signature per request, and it does
  not keep a pairing reachable while somebody is flooding the identifier it uses.
- When the server starts, this plugin now writes one line to the log for each
  pairing the key store already holds, naming it. The store is not in the
  plugin's directory, so it survives an uninstall and a reinstall comes up paired
  with whatever it was paired with before; that line is what says so instead of
  the pairings simply being there. Nothing is written when the store holds
  nothing, and looking at a store that does not exist does not create one. A
  store that cannot be read produces one line at Error and leaves the server
  running.
- [protocol] A pairing request that runs into a fault on this server is now
  answered with the same refusal every other caller gets, rather than with
  whatever the server produces for an error. The detail is written to the log at
  Error instead, where the operator of this server can read it and the peer
  cannot. What such a request was answered with before was never measured on a
  running server and nothing here claims it.
- The key store's directory and its file are created with permissions of their own
  where the server runs on a platform that has them: the directory readable,
  writable and traversable by its owner alone, the file readable and writable by
  its owner alone, and both set as they are created rather than afterwards, so
  there is no moment at which the keys sit on disk under wider ones. Nothing is
  created at all until the first pairing. A store directory that is already there
  with wider permissions is refused, with the path named, rather than narrowed
  under an operator who may have widened it deliberately. On Windows none of this
  applies: what protects the store there is the access control on the server's
  data directory, which this plugin does not set.
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
- [protocol] A pairing ending now takes the user mappings held for it. That is
  true whether it ended by being revoked, which keeps its record, or by an
  enrolment window expiring, which does not, and the cached peer display name
  beside each mapping goes with it. A mapping can only be made by an
  administrator naming themselves, only under a pairing that exists and has not
  been revoked, and a user with no mapping is skipped rather than guessed at.
  Nothing on the wire moved and no server answers differently: there is still no
  page and no endpoint through which a mapping can be made, so a server installed
  today holds an empty table.
