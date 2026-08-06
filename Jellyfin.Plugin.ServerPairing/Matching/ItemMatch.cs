using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// The result of one matching attempt.
/// <para>
/// Candidates are named by their position in the list handed to the matcher rather than
/// by the item objects themselves. Two local copies of one film are equal in every field
/// the matcher can see, so returning the objects would lose which of them was meant.
/// </para>
/// </summary>
public sealed class ItemMatch
{
    /// <summary>
    /// Gets what was decided.
    /// </summary>
    public MatchOutcome Outcome { get; init; }

    /// <summary>
    /// Gets the positions of the candidates that matched. Empty unless the outcome is
    /// <see cref="MatchOutcome.Matched"/> or <see cref="MatchOutcome.Ambiguous"/>.
    /// </summary>
    public IReadOnlyList<int> Matches { get; init; } = [];

    /// <summary>
    /// Gets the positions of the candidates that contradicted the peer item. Filled
    /// whenever a contradiction was seen, including where something else matched, because
    /// a contradiction is worth showing an operator either way.
    /// </summary>
    public IReadOnlyList<int> Disagreements { get; init; } = [];

    /// <summary>
    /// Gets the name of the highest-precedence provider that carried the match, in the
    /// spelling of the matched list rather than the spelling the item used. Null unless
    /// the outcome is <see cref="MatchOutcome.Matched"/>.
    /// </summary>
    public string? MatchedOn { get; init; }

    /// <summary>
    /// Gets a value indicating whether the match was made through the series identifiers
    /// and the numbering rather than through the item's own identifiers. False for
    /// anything that is not an episode.
    /// </summary>
    public bool MatchedThroughSeries { get; init; }
}
