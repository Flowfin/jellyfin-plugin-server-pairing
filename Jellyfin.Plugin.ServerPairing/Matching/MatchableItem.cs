using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// Everything the matcher is allowed to look at. Whatever the host hands a caller is
/// translated into this before the matcher sees it, which is what keeps the matcher free
/// of the library manager and testable from a table.
/// <para>
/// There is deliberately no title, no year, no runtime and no path on this record. Those
/// are the fields the prior art matched on, and matching on them is what this plugin
/// refuses to do.
/// </para>
/// </summary>
public sealed class MatchableItem
{
    /// <summary>
    /// Gets the kind of item. Decides whether the episode route in
    /// <c>docs/matching.md</c> applies.
    /// </summary>
    public MatchableItemKind Kind { get; init; }

    /// <summary>
    /// Gets the item's own provider identifiers, keyed by provider name. Keys outside the
    /// matched list are carried and ignored, so a caller does not have to filter first.
    /// </summary>
    public IReadOnlyDictionary<string, string> ProviderIds { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the provider identifiers of the series this item belongs to, where it belongs
    /// to one. Empty for anything that is not an episode.
    /// </summary>
    public IReadOnlyDictionary<string, string> SeriesProviderIds { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the season number, where the item has one. Null means the host did not supply
    /// it, which is a different thing from season zero.
    /// </summary>
    public int? SeasonNumber { get; init; }

    /// <summary>
    /// Gets the episode number within the season, where the item has one. Null means the
    /// host did not supply it.
    /// </summary>
    public int? EpisodeNumber { get; init; }
}
