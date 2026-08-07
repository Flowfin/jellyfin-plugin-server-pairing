using System.Collections.Generic;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// The providers this plugin matches on, in the precedence order
/// <c>docs/matching.md</c> argues for.
/// <para>
/// The names come from the host's own enumeration rather than from string literals
/// written here, so a name that stops being a member of it fails the build instead of
/// quietly matching nothing.
/// </para>
/// </summary>
public static class MatchedProviders
{
    /// <summary>
    /// Gets the matched providers, highest precedence first.
    /// <para>
    /// Computed on each read rather than held in a static. The assembly is allowed one
    /// static, which the plugin instance already has, and a list this short is not worth
    /// spending the exception on.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> InPrecedenceOrder =>
    [
        MetadataProvider.Imdb.ToString(),
        MetadataProvider.Tmdb.ToString(),
        MetadataProvider.Tvdb.ToString()
    ];

    /// <summary>
    /// Gets the position of a provider in the precedence order, or -1 for a provider this
    /// plugin does not match on. The comparison ignores case, because the dictionary key
    /// is written by whichever provider plugin populated it.
    /// </summary>
    /// <param name="providerName">The provider name as it appears on the item.</param>
    /// <returns>The position, or -1.</returns>
    public static int PrecedenceOf(string? providerName)
    {
        if (string.IsNullOrWhiteSpace(providerName))
        {
            return -1;
        }

        var order = InPrecedenceOrder;
        for (var i = 0; i < order.Count; i++)
        {
            if (string.Equals(order[i], providerName, System.StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
