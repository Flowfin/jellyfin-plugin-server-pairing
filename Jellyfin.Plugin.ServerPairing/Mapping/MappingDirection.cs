namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// Which way a mapping moved, which is the whole of what the audit entry says about the change
/// itself.
/// </summary>
/// <remarks>
/// The row in <c>docs/logging.md</c> carries the pairing, the administrator and the direction,
/// and the direction is these two values rather than the identifiers on either side of the
/// mapping. That is the answer taken on issue #40 on 2026-08-31 rather than a shape chosen
/// here: the identities the change moved between are what the never-log list forbids first, and
/// widening the entry to carry them would put exactly that data into the record an operator
/// keeps longest.
/// <para>
/// What is given up by it is stated where it is felt rather than left for a reader to discover.
/// An operator asking which peer user a local user was mapped to reads the mapping table, live,
/// and never the log. The audit answers that a mapping changed, who changed it and when, which
/// is what lets a change be noticed at all; it does not answer what the mapping was.
/// </para>
/// <para>
/// Changing a mapping is two acts, so it is two entries, one in each direction. There is no
/// third value for a replacement because there is no replacement:
/// <see cref="UserMappings.Map"/> refuses a second mapping rather than overwriting one.
/// </para>
/// </remarks>
public enum MappingDirection
{
    /// <summary>
    /// A mapping was made where there was none.
    /// </summary>
    Mapped = 0,

    /// <summary>
    /// A mapping that existed was removed.
    /// </summary>
    Unmapped = 1,
}
