using MediaBrowser.Controller.Entities;

namespace Jellyfin.Plugin.ServerPairing;

/// <summary>
/// One read of a host property that the 10.11 line grew after its first release.
/// Nothing calls this; it exists so the ABI floor build can be watched refusing
/// the thing it names, and it is removed in the next commit on this branch.
/// </summary>
internal static class FloorProbe
{
    /// <summary>
    /// Reads <c>InternalItemsQuery.UseRawName</c>, which is present on Jellyfin
    /// 10.11.9 and absent on 10.11.0, the floor build.yaml claims.
    /// </summary>
    /// <param name="query">The query to read.</param>
    /// <returns>What the host holds, or null.</returns>
    internal static bool? Read(InternalItemsQuery query) => query.UseRawName;
}
