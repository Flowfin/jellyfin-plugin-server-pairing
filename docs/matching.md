# Matching an item on one server to an item on the other

Two servers hold the same film, and nothing about the two copies is the same
except what a metadata provider said about it. The item identifiers differ, the
file paths differ, the libraries they sit in differ, and the runtime differs by
however much the two rips differ. Provider identifiers are the vocabulary both
sides share, so they are what this plugin matches on. The one thing beside them
is an episode carrying none of its own, which is allowed its series identifiers
plus its season and episode number under the conditions the Episodes section
below fixes.

This document fixes the rules. The test corpus takes its expected outcome from
here rather than from the code, so a disagreement between the two is a failing
test rather than a discovery someone makes later.

## What the matcher is

A pure function. It takes the identifying fields of one item from the peer and a
list of local candidates, and it returns an outcome. It reads no library, no
database and no disk, holds no state, and is given no clock. Nothing in it
depends on `ILibraryManager`, which is what makes the whole of it testable from a
table.

Its inputs are in `Jellyfin.Plugin.ServerPairing/Matching/MatchableItem.cs`.
Whoever calls it is responsible for filling that record from whatever the host
handed them, and that translation is not part of this document.

## The providers this plugin matches on, in order

    Imdb
    Tmdb
    Tvdb

Nothing else. The names are the ones the host uses as keys in an item's provider
dictionary, and they are not string literals invented here: the matched list is
built from `MediaBrowser.Model.Entities.MetadataProvider`, so a name that stops
being a member of that enumeration is a compile error rather than a matcher that
silently stops matching on that provider.

That guarantee is the construction's rather than a test's, and the difference is
worth stating because a reader who takes it for a test's expects the suite to
redden where the compiler is what stops the change. While the list is built this
way the membership assertion cannot fail: a name the enumeration produced is a
name the enumeration has. What that assertion holds is the construction itself,
and it reddens the moment an entry is written as a literal instead.

    dotnet test --filter MatchedProviders

The order is a precedence over identifiers, not a preference between providers,
and it does exactly one thing: it names which identifier carried a match when
several agree. It never resolves a disagreement. What the order is built on:

IMDb identifiers are carried by films and by episodes, are stable once assigned,
and are the identifier the other providers most often cross-reference, so they
are the most likely of the three to be present on both sides of a pair of
independently built libraries. That is the reason for the position and it is a
design position rather than a measurement. Nothing here has counted how often
each provider is populated in real libraries, and this document does not claim
it has.

TMDb comes next because it covers both films and series. TVDB is last because it
is series-shaped, so on a film library it is usually the one that is absent.

A provider outside that list is ignored, and ignored means ignored: two items
carrying the same MusicBrainz identifier and nothing else do not match. Adding a
provider to the list is a change to this document, a change to the list, and
new rows in the corpus, and a test refuses the change if the rows are missing.

## Comparing two identifiers

Provider names are compared without regard to case, because the dictionary keys
arrive from whichever provider plugin wrote them and the case is that plugin's
habit rather than part of the identifier.

Values are trimmed of surrounding whitespace before comparison, and compared
without regard to case. An IMDb identifier written `tt0111161` and one written
`TT0111161` are the same identifier, and the numeric identifiers the other two
use are unaffected by either rule.

A value that is empty, or that is only whitespace, is treated as absent. A
provider present with an empty value tells the matcher nothing, and treating it
as a value that fails to compare would turn a gap in someone's metadata into a
disagreement.

## Agreement, disagreement, and what precedence does not do

For a given candidate, look only at the providers in the matched list that carry
a value on both sides.

- If any of them disagree, the candidate does not match, whatever the others
  say. A higher-precedence provider does not overrule a lower one. Two items
  agreeing on IMDb and disagreeing on TMDb are two items about which the
  metadata is inconsistent, and picking one of the two identifiers to believe is
  the guess this plugin does not make.
- If none disagree and at least one agrees, the candidate matches, and the
  highest-precedence agreeing provider is recorded as the one that carried it.
- If none of them carry a value on both sides, the candidate is unrelated. That
  is not a disagreement and it is not counted as one.

## Episodes

Many setups have no episode-level identifier at all, so an episode is allowed a
second route.

An episode is matched on its own provider identifiers where both sides have at
least one of the matched providers in common. Where they do not, it falls back
to the identifiers of its series, plus its season number and its episode number,
and all three have to be there and have to agree.

The series is compared by the same rules as anything else, so a series carrying
different identifiers on the two servers is a disagreement and the episode does
not match. Season and episode numbers are compared as numbers. An episode
missing either of them, on either side, cannot be identified by this route and
matches nothing.

Two episodes of one series with different numbering are different items and not
a contradiction, so they are unrelated rather than a disagreement. Every episode
of a series would otherwise be reported as contradicting every other one.

An episode that has its own identifiers is never rescued by the fallback. If the
two sides both carry an episode-level identifier and those identifiers disagree,
that is a disagreement and the series numbering does not overturn it. The reverse
holds as well: where the two sides agree on an identifier of their own, the
fallback is not consulted at all, so the series identifiers may differ and the
numbering may differ without changing the outcome.

The fallback needs all three parts, and an episode that has none of its own
identifiers and cannot supply all three is not identifiable by either route. That
is `NoIdentifiers` rather than a failure to find a candidate, and each of the
three parts stands on its own: a missing season number, a missing episode number
and a series carrying no identifier of a matched provider each close the route by
themselves.

Both sides have to be reachable by the route. An item the host did not call an
episode is never judged by it, whatever season number, episode number and series
identifiers it happens to carry, because the route is what the episode kind buys
and not a second way to compare any two items. A local episode that is missing
either number is in the same position: the route is closed for that candidate, so
it is unrelated, and a series identifier it carries that differs from the peer's
is not read and is not a disagreement.

A match records which of the two routes carried it. That is what tells a caller
whether the two items were tied together by an identifier of the item itself or
by its series plus two numbers, which are different strengths of evidence about
the same claim.

A match can name more than one candidate, and those candidates need not have
arrived by the same route. The record says the series route where every named
candidate came by it, so one candidate tied by an identifier of its own is
enough for the answer to be no. It is a statement about the whole result rather
than about any one member of it, and a caller that needs to know how a
particular candidate was reached cannot read that off this record.

## Two local items with the same identifier

A film held twice, in two qualities, is two items on the local server carrying
one IMDb identifier between them. That is an ordinary library and not a fault,
so it is not reported as one.

The rule is about whether the candidates agree with each other, not about how
many there are:

- Where every matching candidate agrees with every other matching candidate on
  each of the matched providers they share, they are copies of one thing. The
  outcome is a match and every one of them is named in it. What the caller does
  with two local copies is the caller's decision and not the matcher's.
- Where two matching candidates disagree with each other, they are not copies,
  and the outcome is ambiguous. Nothing picks the first.

## The outcomes

| Outcome | What it means |
| --- | --- |
| `Matched` | One local item, or several that agree with each other, carry an identifier agreeing with the peer item. The result names them and the provider that carried it. |
| `Ambiguous` | Several local items match the peer item and disagree with each other. Nothing is chosen. |
| `Disagreement` | Nothing matched, and at least one candidate shares a matched provider with the peer item and gives a different value for it. The result names those candidates. |
| `NoCandidate` | The peer item is identifiable and no local candidate shares a matched provider with it. |
| `NoIdentifiers` | The peer item carries no value for any matched provider, and for an episode has no usable series identifiers and numbering either. Nothing about it can be compared, so nothing is. |

`Disagreement` and `NoCandidate` both mean no match. They are separate outcomes
because they are separate problems for whoever is looking at them: one is
metadata that contradicts itself across two servers, the other is a film one
server does not have.

## What this costs an operator

The rules above have a consequence an operator meets before they meet any of
the rules, so it belongs in the document rather than being inferred from a
counter. An item carrying no value for any matched provider matches nothing,
ever. It is not matched by title, by year, by runtime or by file name. The
matcher is never given those fields at all:
`Jellyfin.Plugin.ServerPairing/Matching/MatchableItem.cs` does not carry them,
which makes the refusal a property of the type rather than a branch that could
be relaxed later.

So a library of home video, of personal recordings, of anything ripped without
metadata, or of anything no provider plugin has identified, will not match, and
nothing built on this plugin will move anything for it. No setting turns that
into a guess and none is planned. The attempts at this problem that came before
matched on file paths or on internal item identifiers, which works in one
operator's setup and produces wrong results in somebody else's, and a wrong
match here writes one person's watch state onto another person's film.

An operator watching half a library fail to match is watching this rule work
rather than watching a fault. Which half, and why, is what the refusal counters
are for, and the surface that shows them is issue #51.

## The corpus

The cases the corpus has to cover, and what each is for, are in
`Jellyfin.Plugin.ServerPairing.Tests/Matching/MatchingCorpus.cs`. Every row names
its situation, every row is executed, and the expected outcome of each is the
one this document gives. A row whose expectation cannot be read out of the rules
above is a row that is testing the code against itself.

Every row also pins the route, including the rows that match nothing, where the
route a result reports is false. A row that named only the outcome would leave
the rule about which route carried a match with nothing behind it.

The rules in this section are one row each rather than one row per situation. A
situation walks several conditions at once, so a row per situation can be green
while any one of the conditions it walks is inverted, and that is measured rather
than supposed: three rows walked the episode route and eleven mutations of it
went unnoticed, which is issue #124 and the run it names.

Adding a provider to the matched list is not a one-line change. Two guards in
`MatchingCorpusTests` refuse it until the corpus has caught up: one requires a
row where the new provider carries a match, and the other requires a row where
its presence produces something other than a match. A provider covered only by
the first has no case written down for its refusals, and the refusals are the
half that goes wrong quietly.
