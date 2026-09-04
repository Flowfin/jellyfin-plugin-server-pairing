# The operator guide

Two people, two servers, and a pairing to establish. This walks that from
installation to a working link, and then through everything that goes wrong.

Every step is written for a plain server. Nothing here assumes a reverse proxy,
a particular container layout, or anything else that is the operator's business
rather than this plugin's.

## Nothing below can be performed today

This document has never been followed, because there is nothing yet to follow it
against. It is written from [`protocol.md`](protocol.md) and from the sentences
this plugin will show, so that the walkthrough and the surface are one text
rather than two, and so the readme has a walkthrough to link. It is not a record
of anybody pairing two servers.

No pairing key is ever created, because nothing in this plugin generates a key
pair:

    git grep -lni 'ECDiffieHellman' origin/master -- Jellyfin.Plugin.ServerPairing ; echo "exit=$?"
    exit=1

THIS PARAGRAPH SAID NOTHING AN ADMINISTRATOR PRESSES REACHES THIS PLUGIN AND THAT
EVERY ACTION BEHIND ITS TWO ROUTE PREFIXES IS A READ. One thing does and one
action is not: an administrator can remove a mapping from a pairing's table,
and that is the whole of what an administrator can change here. Nothing opens a
window, confirms a ceremony, revokes a pairing or adds a mapping, so every step
below that changes something still waits:

    git grep -nE 'Http(Post|Put|Delete|Patch)' origin/master -- Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs ; echo "exit=$?"
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:483:    [HttpDelete("pairings/{pairingId}/mappings/{localUserId}")]
    exit=0

and the settings page is still the plugin template's example fields:

    git grep -c 'AnInteger' origin/master -- Jellyfin.Plugin.ServerPairing/Configuration/configPage.html
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/configPage.html:4

So each step below carries a line saying what it waits on. When a step's surface
lands, that line goes and the step is written against something a person has
done. Until then, read this as the specification in the order an operator meets
it, and read [`release.md`](release.md) for what a release is judged against.

Where a step quotes a sentence in a block quote, that sentence is what the
surface will say, word for word. The two are held equal by a case rather than by
care, so a sentence edited in one place and not the other is a red suite:

    git grep -c 'EverySentenceAnOperatorReadsIsQuotedHere' origin/master -- Jellyfin.Plugin.ServerPairing.Tests/Wording/OperatorGuideWordingTests.cs
    origin/master:Jellyfin.Plugin.ServerPairing.Tests/Wording/OperatorGuideWordingTests.cs:1

It compares in both directions. A sentence the register holds and this file does
not quote is a failure, and a block quote here that no register holds is a
failure as well, so neither document can drift away from the other quietly.
What it does not judge is everything outside a block quote: the prose around a
quotation is read by a person and by nothing else.

## What you need before you start

Two Jellyfin servers, one administrator account on each, and two people who can
talk to each other while they do this. The second person is not optional: the
step the whole arrangement rests on is a comparison between two screens, and one
person with access to both screens has not performed it.

Each server has to be able to reach the other over the network at an address you
can type. Which forms of address are accepted, and the one setting that widens
them, is [`configuration.md`](configuration.md); the short answer is an `https`
address, and an `http` one only where an operator has said in the settings that
they know what that means.

Both clocks have to be roughly right. A signed request is refused when its
timestamp is outside a window measured in minutes, so a server whose clock is
hours out cannot pair with anything. The window and what it is for are in
[`protocol.md`](protocol.md).

*Waits on: nothing. This step is about the two machines rather than the plugin.*

## 1. Install on both servers

Install the plugin on each server and restart each one. Then open the plugin's
settings page on both and check that it loads.

There is nothing to configure in order to pair. Every setting has a default that
works, and [`configuration.md`](configuration.md) is the list of them with what
each one does to a server that changes it.

What to check before going further: the two servers are running the same plugin
version, or two versions that speak a protocol version in common.
[`versioning.md`](versioning.md) says how long a protocol version is accepted,
and the failure when there is nothing in common is under
[When the peer version differs](#when-the-peer-version-differs) below.

*Waits on: a published release. No release has been cut from this repository, so
there is nothing to install.*

## 2. Open the window on one side

Decide which of the two servers starts. It does not matter which; the two sides
are the same afterwards.

On that server, enter the other server's address and open the enrolment window.

> Enter the address of the other server and open the window. Until it closes, this server will answer that address and no other.

The window is the only moment this server answers a party it has not
authenticated, so it is deliberately small. It stays open for ten minutes unless
an operator has chosen otherwise, it closes the moment an enrolment succeeds, it
closes after three failed attempts, and it refuses to open at all against a
server this one is already paired with.

While it is open and the other side has not answered:

> Nothing has arrived from that address yet. The other operator has to open a window on their server against this one.

You can close it yourself before anything has used it:

> Closing the window stops this server answering that address. Nothing from an enrolment that did not finish is kept. Opening another window later is starting again rather than undoing this.

The ten minutes are `EnrolmentWindowSeconds` in
[`configuration.md`](configuration.md), which also gives the maximum. A value
above that maximum is refused when the configuration is read, with the setting
named, rather than quietly shortened to the maximum.

*Waits on: an administrative action that opens a window. The window itself is
built and has no caller.*

## 3. What the other operator does

They do the same thing on their server: enter your server's address and open a
window. That is the whole of their part of this step.

The two servers then exchange public keys inside those two windows. Neither
operator types a code, carries a secret between the two machines, or pastes
anything into a chat. There is no transcribed secret anywhere in this design,
which is why there is nothing here for somebody reading your messages to take.

*Waits on: the same action as step 2, on the other side, and the enrolment
exchange behind it.*

## 4. Compare, which is the step that matters

Both servers now show a value. This is the step the whole arrangement rests on,
and it is the one no software performs for you.

> The value below is worked out from both servers' keys. Two servers talking to each other, and to nobody in between, work out the same value.

> Read the eight groups below out to the other operator and have them read back the eight groups on their screen. Compare all eight.

> Comparing is what makes this a pairing with that server rather than with whoever answered. Confirming without comparing establishes nothing.

Read them out to each other over a channel where you know who you are talking
to. Do not send the value in a message and have the other person reply that it
matches; somebody able to put themselves between the two servers is usually able
to put themselves between the two messages as well.

> Confirm only if all eight groups are the same as the ones the other operator read out.

If they are not the same:

> Stop. Do not confirm, and do not open another window. Values that differ mean the two servers are not talking only to each other. Find out why before pairing them.

That is the one instruction in this document with no "try again" beside it, and
that is deliberate. Two values that differ is the signature of the attack this
ceremony exists against, and opening a fresh window to compare a fresh pair of
values rules nothing out.

What an operator who confirms without comparing loses is stated in
[`threat-model.md`](threat-model.md) rather than here, because that is the
document that owns what each adversary reaches.

*Waits on: a key pair to derive the value from, and a page to show it on.*

## 5. Confirm, on both sides

Confirming is per operator. After you have confirmed and before the other
operator has:

> You have confirmed. The pairing starts working once the other operator confirms as well. Nothing moves between the servers until then.

Once both of you have:

> Both operators have confirmed. These two servers are paired.

If the window ran out before the two of you finished:

> The window closed and nothing was kept from an enrolment that did not finish. Open a new one to start again.

That is not a failure to diagnose. A window that closed unused leaves nothing
behind, and starting again costs the two of you the comparison a second time and
nothing else.

## 6. Map the users

A pairing on its own moves nothing. Until an administrator says who on this
server is who on the other, nothing has anywhere to go, and that is the intended
state rather than an unfinished one.

Mapping is a decision, never an inference. This plugin does not match users by
name, by email address or by anything else, because two households with a "Dad"
account do not hold the same person and a plugin that guesses gets it wrong
silently. [`mapping.md`](mapping.md) argues that and says what a mapping holds.

One local user maps to at most one user on the peer per pairing, and one peer
user to at most one local user. A second mapping for either side is refused and
names the mapping already standing, rather than replacing it.

Changing a mapping is therefore removing one and making another, and what that
does is worth reading before doing it:

> Changing this mapping changes where the next transfer goes and nothing else. Everything that arrived under the old mapping stays on the user it arrived on. Setting the old mapping back later does not undo that.

Removing one:

> Removing this mapping stops anything further moving for that user. What already arrived under it stays on the user it arrived on, and removing that is done wherever it was stored. This cannot be undone from here.

*Waits on: an administration surface for the table. The table itself is built
and refuses a second mapping in either direction.*

## 7. Confirm it works, and where to look when it does not

A pairing that is working shows as active on both dashboards, with the peer, the
state and when the key was last rotated.

Nothing this plugin does is visible as content moving, because it moves no
content. What travels is whatever a sync plugin built on this one sends, so
whether the sync works is answered on that plugin's own surface, and this one
answers only whether the pairing underneath it is up.

The two places to look are the dashboard and the server log.
[`logging.md`](logging.md) is the list of what this plugin writes and at what
level, and it is also the list of what it may never write: no key material, no
signature, no authorization header, and none of the peer's user identities. That
second list is why a log from this plugin can be pasted into a support thread,
and it is worth knowing before you paste one.

*Waits on: the dashboard, and the log entries for the events of a pairing's
life. Four call sites write to the log today and none of them is one of those
events.*

## When it goes wrong

### When the reason names the clock

A pairing that was working starts refusing, and the refusal names the clock
rather than the key.

Every request between two paired servers carries a timestamp, and one whose
timestamp is outside the window is refused even though its signature verified.
`TimestampWindowSeconds` in [`configuration.md`](configuration.md) is that
window, and five minutes is what a server gets whose operator has chosen
nothing.

The repair is on the machine whose clock is wrong rather than in this plugin.
Point both servers at a time source, let them settle, and try again. Nothing has
to be re-paired for this: the pairing is intact and its requests start being
accepted again as soon as the clocks agree.

Why this refusal is told apart from every other one at all is argued in
[`protocol.md`](protocol.md), and the argument is that an operator who is not
told loses an evening to what looks like a key problem and is not one.

### When the peer version differs

Two operators upgrade on their own schedules, so two paired servers run
different plugin versions for as long as it takes each of them to notice an
update.

That is ordinary and it is meant to keep working. A refusal happens only where
the two builds have no protocol version in common at all, which is what
[`versioning.md`](versioning.md) bounds: it says how long a protocol version
keeps being accepted, so an upgrade is not something both operators have to do
on the same day.

The repair is to upgrade the older side. Upgrading never requires re-pairing,
and a change that would require it is a release note in the strongest terms
rather than something you find out here.

### When items are not matching

An item carrying no provider identifier is not matched, ever. Not by title, not
by year, not by runtime, not by file name, and no setting turns that into a
guess.

So a library of home video, of personal recordings, or of anything ripped
without metadata will not be matched, and that is a deliberate limitation rather
than a fault to repair. [`matching.md`](matching.md) lists the identifiers that
are read and argues why guessing is refused, and the readme says the same before
an operator installs anything.

What this looks like is a sync plugin transferring nothing for part of a library
while the pairing itself is healthy. Check the pairing first: if it is active,
the matching limitation is the likelier answer.

### When a server was restored from a backup

A restored server is the case this design is most careful about, because a
backup carries the key store with it, and an old key coming back to life is
exactly what revocation exists to prevent.

What the store carries so that a restored copy is recognised rather than
resynchronised is [`keystore.md`](keystore.md). What a restored, copied or
corrupt store then does is an open question on this repository's tracker, and
this document does not answer it.

The safe course after restoring a server from a backup is to revoke the pairing
from the other side and enrol again. That costs the two of you one comparison
and leaves no question about which key is live.

### Rotating, revoking and unpairing

Rotating replaces a pairing's key while the pairing keeps working. Both keys
verify for a short overlap, so requests already in flight are not refused for a
reason that is not theirs, and after it the old key stops verifying. The overlap
is fixed in [`protocol.md`](protocol.md).

Revoking:

> Revoking ends this pairing here and now and destroys the key that verified it. It does not wait for the other server and works when that server is unreachable. It cannot be undone: pairing these two servers again means a fresh enrolment and a fresh comparison.

Unpairing:

> Unpairing asks the other server to end the pairing as well, then ends it here. If that server refuses or cannot be reached this side still ends, and what this server already sent is on the other server for its operator to remove. It cannot be undone.

Removing everything held about one person:

> Removing this user removes every mapping held for them here and asks each paired plugin to delete what it stored for them. What the other server holds is that operator's to remove. It cannot be undone.

The half operators most often assume wrongly is what an ending reaches. Ending a
pairing removes what arrived from the other server, on the side that ends it. It
reaches nothing on the other machine. Whatever this server already sent is
sitting over there, and the other operator ending the pairing on their side is
what removes it. [`data.md`](data.md) is the field-level version of that, and
[`lifecycle.md`](lifecycle.md) is what disabling, uninstalling and reinstalling
leave behind.

*Waits on: the actions themselves. Rotation and revocation exist as transitions
and neither has a surface an operator reaches.*

## What identifies each of those failures today

This is the part of the guide most likely to be read as stronger than it is, so
it is written as what does not answer rather than as what does.

The dashboard serves a diagnostics payload an operator can read, behind the
host's elevation policy, and it carries a counter per refusal. Every counter it
carries today is a refusal on the peer plane that a stranger could equally
cause: a request off the plane, a body over its limit, an arrival allowance
spent, no room left to count an arrival, a signature that did not verify, and a
message not accepted in the state the pairing is in.

**None of the failures above has a counter with anything behind it.** The
taxonomy in [`protocol.md`](protocol.md) names a code for the clock case and one
for the version case, and no site in this plugin produces either, so both read
zero on every server whatever happened to a clock or to a version. A zero there
is the absence of a producer rather than a measurement, and reading it as one
rules out the very cause being looked for. The matching counters have no member
in the payload at all, for the same reason, which the payload states itself.

So each failure above is identified by what its own section says to look at, and
not by a number on the diagnostics page. When a counter has a producer, the
sentence sending an operator to it goes into the section it belongs to.

## What this document does not claim

Nothing here has been measured against a running Jellyfin server. No pairing has
been established, no window has been opened by an administrator, no value has
been compared by two people, and none of the failures listed above has been
observed.

One thing this guide is asked for and does not yet have: somebody who has not
seen the plugin getting from installation to an active pairing using only this
document, with whatever was missing recorded. That cannot be done until there is
a pairing to establish, so the guide is unverified in the one way that matters
most for a guide. When it is done, what was missing is added here, and that is
the change that makes this a record rather than a specification.
