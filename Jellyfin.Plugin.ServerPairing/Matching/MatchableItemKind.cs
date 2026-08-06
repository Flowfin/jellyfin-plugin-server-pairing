namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// The kinds of item the matcher treats differently. Only <see cref="Episode"/> gets a
/// second route in <c>docs/matching.md</c>; everything else is matched on its own
/// provider identifiers or not at all.
/// </summary>
public enum MatchableItemKind
{
    /// <summary>
    /// Anything the matcher has no special rule for. Matched on its own provider
    /// identifiers.
    /// </summary>
    Other = 0,

    /// <summary>
    /// A film. Matched on its own provider identifiers.
    /// </summary>
    Movie = 1,

    /// <summary>
    /// A series. Matched on its own provider identifiers.
    /// </summary>
    Series = 2,

    /// <summary>
    /// An episode. Matched on its own provider identifiers where both sides share one,
    /// and otherwise on its series identifiers together with its season and episode
    /// numbers.
    /// </summary>
    Episode = 3
}
