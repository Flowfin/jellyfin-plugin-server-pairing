namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The versions of the pairing protocol this build speaks, declared once.
/// </summary>
/// <remarks>
/// Issue #25 asks for one list in one place, read by the negotiation, by the refusal and by
/// the dashboard, because a supported set written down three times is a set that disagrees
/// with itself the first time one of the three is edited. This is that place. One of the three
/// readers exists today, which is <see cref="VersionNegotiation"/>; the dashboard is issue #49
/// and there is no page, and what a refusal is allowed to say about the set is the open
/// question recorded on issue #25 rather than something decided here.
/// <para>
/// Version 1 is <c>docs/protocol.md</c> as it stands, which is the whole set. That makes every
/// selection this build performs return 1, so the rule the negotiation implements cannot be
/// separated from a constant by driving it through this range alone. It is separated by
/// driving <see cref="VersionNegotiation.Select(VersionRange, VersionRange)"/> over ranges
/// this server does not ship, which is why that overload takes the local range rather than
/// reading it.
/// </para>
/// <para>
/// Dropping a version from this range strands every peer that speaks only it, so it is a
/// change that carries a <c>[protocol]</c> line in <c>CHANGELOG.md</c>. The pull request
/// hygiene check refuses one that does not, over every file in this directory.
/// </para>
/// </remarks>
public static class SupportedVersions
{
    /// <summary>
    /// The lowest version this build speaks.
    /// </summary>
    public const int Lowest = 1;

    /// <summary>
    /// The highest version this build speaks.
    /// </summary>
    public const int Highest = 1;

    /// <summary>
    /// Gets the range this build speaks, as a peer is told it in a <c>hello</c>.
    /// </summary>
    public static VersionRange Range => new VersionRange(Lowest, Highest);
}
