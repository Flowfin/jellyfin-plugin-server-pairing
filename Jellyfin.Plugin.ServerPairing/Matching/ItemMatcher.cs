using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// Resolves one item on the peer server to the local item that is the same thing, using
/// nothing but provider identifiers. The rules it implements are written in
/// <c>docs/matching.md</c> and the corpus takes its expectations from there.
/// </summary>
public static class ItemMatcher
{
    /// <summary>
    /// How one candidate stood against the peer item.
    /// </summary>
    private enum Verdict
    {
        /// <summary>Shares no matched provider with the peer item.</summary>
        Unrelated,

        /// <summary>Shares at least one and every shared one agrees.</summary>
        Agrees,

        /// <summary>Shares at least one and at least one of them contradicts.</summary>
        Contradicts
    }

    /// <summary>
    /// Which of the two routes in the document a candidate was judged by.
    /// </summary>
    private enum Route
    {
        /// <summary>The item's own provider identifiers.</summary>
        OwnIdentifiers,

        /// <summary>The series identifiers together with the season and episode numbers.</summary>
        Series
    }

    /// <summary>
    /// Matches one peer item against a list of local candidates.
    /// </summary>
    /// <param name="peerItem">The item as the peer server described it.</param>
    /// <param name="localCandidates">
    /// The local items worth considering. The caller decides how that list is narrowed;
    /// the matcher treats it as the whole world and reports positions within it.
    /// </param>
    /// <returns>The outcome, and whatever the outcome names.</returns>
    public static ItemMatch Match(MatchableItem peerItem, IReadOnlyList<MatchableItem> localCandidates)
    {
        ArgumentNullException.ThrowIfNull(peerItem);
        ArgumentNullException.ThrowIfNull(localCandidates);

        if (!IsIdentifiable(peerItem))
        {
            return new ItemMatch { Outcome = MatchOutcome.NoIdentifiers };
        }

        var matches = new List<int>();
        var contradictions = new List<int>();
        var routes = new Dictionary<int, Route>();
        var carriedBy = int.MaxValue;

        for (var i = 0; i < localCandidates.Count; i++)
        {
            var candidate = localCandidates[i];
            if (candidate is null)
            {
                continue;
            }

            var (verdict, route, provider) = Judge(peerItem, candidate);
            switch (verdict)
            {
                case Verdict.Agrees:
                    matches.Add(i);
                    routes[i] = route;
                    carriedBy = Math.Min(carriedBy, provider);
                    break;
                case Verdict.Contradicts:
                    contradictions.Add(i);
                    break;
                default:
                    break;
            }
        }

        if (matches.Count == 0)
        {
            return new ItemMatch
            {
                Outcome = contradictions.Count > 0 ? MatchOutcome.Disagreement : MatchOutcome.NoCandidate,
                Disagreements = contradictions
            };
        }

        if (!AreAllCopiesOfOneThing(localCandidates, matches))
        {
            return new ItemMatch
            {
                Outcome = MatchOutcome.Ambiguous,
                Matches = matches,
                Disagreements = contradictions
            };
        }

        var throughSeries = matches.All(index => routes[index] == Route.Series);

        return new ItemMatch
        {
            Outcome = MatchOutcome.Matched,
            Matches = matches,
            Disagreements = contradictions,
            MatchedOn = MatchedProviders.InPrecedenceOrder[carriedBy],
            MatchedThroughSeries = throughSeries
        };
    }

    /// <summary>
    /// Whether the peer item carries anything the matcher is allowed to compare. An
    /// episode is identifiable by either route, so it needs its own identifiers, or its
    /// series identifiers together with both numbers.
    /// </summary>
    /// <param name="item">The peer item.</param>
    /// <returns>True where something can be compared.</returns>
    private static bool IsIdentifiable(MatchableItem item)
    {
        if (Comparable(item.ProviderIds).Count > 0)
        {
            return true;
        }

        return item.Kind == MatchableItemKind.Episode && HasSeriesRoute(item);
    }

    /// <summary>
    /// Whether an item can be judged by the series route, which needs series identifiers
    /// and both numbers.
    /// </summary>
    /// <param name="item">The item.</param>
    /// <returns>True where the route is available.</returns>
    private static bool HasSeriesRoute(MatchableItem item)
        => item.SeasonNumber.HasValue
            && item.EpisodeNumber.HasValue
            && Comparable(item.SeriesProviderIds).Count > 0;

    /// <summary>
    /// Reduces a provider dictionary to the matched providers that carry a usable value,
    /// keyed by the spelling in the matched list and with the value trimmed. A value that
    /// is blank is dropped rather than compared, because a gap in somebody's metadata is
    /// not a contradiction.
    /// </summary>
    /// <param name="providerIds">The raw dictionary from the item.</param>
    /// <returns>The comparable subset.</returns>
    private static Dictionary<string, string> Comparable(IReadOnlyDictionary<string, string>? providerIds)
    {
        var comparable = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (providerIds is null)
        {
            return comparable;
        }

        foreach (var pair in providerIds)
        {
            var precedence = MatchedProviders.PrecedenceOf(pair.Key);
            if (precedence < 0 || string.IsNullOrWhiteSpace(pair.Value))
            {
                continue;
            }

            comparable[MatchedProviders.InPrecedenceOrder[precedence]] = pair.Value.Trim();
        }

        return comparable;
    }

    /// <summary>
    /// Judges one candidate against the peer item, choosing the route per candidate as
    /// the document describes.
    /// </summary>
    /// <param name="peerItem">The peer item.</param>
    /// <param name="candidate">The local candidate.</param>
    /// <returns>The verdict, the route it was reached by, and the precedence of the provider that carried an agreement.</returns>
    private static (Verdict Verdict, Route Route, int Provider) Judge(MatchableItem peerItem, MatchableItem candidate)
    {
        var byOwn = Compare(Comparable(peerItem.ProviderIds), Comparable(candidate.ProviderIds));
        if (byOwn.Verdict != Verdict.Unrelated)
        {
            return (byOwn.Verdict, Route.OwnIdentifiers, byOwn.Provider);
        }

        if (peerItem.Kind != MatchableItemKind.Episode || candidate.Kind != MatchableItemKind.Episode)
        {
            return (Verdict.Unrelated, Route.OwnIdentifiers, int.MaxValue);
        }

        if (!HasSeriesRoute(peerItem) || !HasSeriesRoute(candidate))
        {
            return (Verdict.Unrelated, Route.Series, int.MaxValue);
        }

        var bySeries = Compare(Comparable(peerItem.SeriesProviderIds), Comparable(candidate.SeriesProviderIds));
        if (bySeries.Verdict != Verdict.Agrees)
        {
            return (bySeries.Verdict, Route.Series, bySeries.Provider);
        }

        var sameEpisode = peerItem.SeasonNumber == candidate.SeasonNumber
            && peerItem.EpisodeNumber == candidate.EpisodeNumber;

        // A different episode of the same series is a different item rather than a
        // contradiction, so it is unrelated and is not counted as a disagreement.
        return sameEpisode
            ? (Verdict.Agrees, Route.Series, bySeries.Provider)
            : (Verdict.Unrelated, Route.Series, int.MaxValue);
    }

    /// <summary>
    /// Compares two comparable sets over the providers they both carry. Any contradiction
    /// wins, whatever precedence the agreeing providers have, which is the rule the
    /// document states as precedence never resolving a disagreement.
    /// </summary>
    /// <param name="left">One comparable set.</param>
    /// <param name="right">The other.</param>
    /// <returns>The verdict and the precedence of the highest agreeing provider.</returns>
    private static (Verdict Verdict, int Provider) Compare(
        Dictionary<string, string> left,
        Dictionary<string, string> right)
    {
        var agreed = int.MaxValue;
        var contradicted = false;
        var order = MatchedProviders.InPrecedenceOrder;

        for (var i = 0; i < order.Count; i++)
        {
            var provider = order[i];
            if (!left.TryGetValue(provider, out var leftValue) || !right.TryGetValue(provider, out var rightValue))
            {
                continue;
            }

            if (string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase))
            {
                agreed = Math.Min(agreed, i);
            }
            else
            {
                contradicted = true;
            }
        }

        if (contradicted)
        {
            return (Verdict.Contradicts, int.MaxValue);
        }

        return agreed == int.MaxValue
            ? (Verdict.Unrelated, int.MaxValue)
            : (Verdict.Agrees, agreed);
    }

    /// <summary>
    /// Whether every matching candidate is a copy of the same thing, which is the
    /// question that separates a film held twice from a genuine ambiguity.
    /// </summary>
    /// <param name="candidates">The full candidate list.</param>
    /// <param name="matches">The positions that matched.</param>
    /// <returns>True where none of them contradicts another.</returns>
    private static bool AreAllCopiesOfOneThing(IReadOnlyList<MatchableItem> candidates, List<int> matches)
    {
        for (var i = 0; i < matches.Count; i++)
        {
            for (var j = i + 1; j < matches.Count; j++)
            {
                var (verdict, _, _) = Judge(candidates[matches[i]], candidates[matches[j]]);
                if (verdict == Verdict.Contradicts)
                {
                    return false;
                }
            }
        }

        return true;
    }
}
