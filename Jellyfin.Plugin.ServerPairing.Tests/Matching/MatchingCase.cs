using System.Collections.Generic;
using Jellyfin.Plugin.ServerPairing.Matching;

namespace Jellyfin.Plugin.ServerPairing.Tests.Matching;

/// <summary>
/// One row of the corpus. A row is data rather than a test method, so the situations the
/// matcher has to get right are readable as a list instead of being spread over a file of
/// near-identical functions.
/// </summary>
internal sealed class MatchingCase
{
    /// <summary>
    /// Gets the name of the situation this row represents. It is what a failure prints,
    /// so it says what the case is about rather than what the values are.
    /// </summary>
    public required string Situation { get; init; }

    /// <summary>
    /// Gets the item as the peer server described it.
    /// </summary>
    public required MatchableItem PeerItem { get; init; }

    /// <summary>
    /// Gets the local items the matcher is given.
    /// </summary>
    public required IReadOnlyList<MatchableItem> LocalCandidates { get; init; }

    /// <summary>
    /// Gets the outcome the rules in <c>docs/matching.md</c> give for this row.
    /// </summary>
    public required MatchOutcome Expected { get; init; }

    /// <summary>
    /// Gets the candidate positions the outcome should name. Empty where the outcome
    /// names none.
    /// </summary>
    public IReadOnlyList<int> ExpectedMatches { get; init; } = [];

    /// <summary>
    /// Gets the provider the document says carried the match, or null where the row does
    /// not pin one.
    /// </summary>
    public string? ExpectedMatchedOn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the document says the match was carried by the
    /// series identifiers and the numbering rather than by the item's own identifiers.
    /// Every row pins it, including the rows that match nothing, because a result that
    /// names no match is a result whose route is false.
    /// </summary>
    public bool ExpectedMatchedThroughSeries { get; init; }
}
