using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The versions of the pairing protocol this build speaks, declared once.
/// </summary>
/// <remarks>
/// Issue #25 asks for one list in one place, read by the negotiation, by the refusal and by
/// the dashboard, because a supported set written down three times is a set that disagrees
/// with itself the first time one of the three is edited. This is that place. ALL THREE
/// READERS EXIST NOW, AND THIS PARAGRAPH SAID ONE DID. They are
/// <see cref="VersionNegotiation"/>, <see cref="Api.Refusal.Body(Api.RefusalCode)"/>, which
/// carries the range in the one refusal body the taxonomy lets carry anything, and
/// <see cref="Api.DiagnosticsAnswer"/>, which the configuration page renders. Each reads this
/// type; none holds a second copy of the numbers.
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

    /// <summary>
    /// Whether a version, as it arrived on the wire, is one this build speaks.
    /// </summary>
    /// <param name="version">The value of the version field, as it arrived.</param>
    /// <returns>True where it is a version and it is inside <see cref="Range"/>.</returns>
    /// <remarks>
    /// The shape is judged before the value, by the same predicate the signature's own field
    /// check uses, so a leading zero, a sign, whitespace, an empty field and a value past the
    /// digit limit answer false here rather than reaching a parser that would accept some of
    /// them. That is what makes this total: it takes the string that arrived and never throws,
    /// so a caller does not have to have checked the shape first.
    /// <para>
    /// It lives here rather than at the caller because this type is the one place the set is
    /// declared, and a membership test written at a call site is a second copy of the set the
    /// moment it names a number. Nothing here is a fourth reader of the range: the readers issue
    /// #25's fourth condition counts are the ones outside this type.
    /// </para>
    /// </remarks>
    public static bool Speaks(string? version) =>
        FieldShape.IsUnsignedInteger(version, FieldShape.VersionDigitLimit)
        && Range.Includes(int.Parse(version!, NumberStyles.None, CultureInfo.InvariantCulture));
}
