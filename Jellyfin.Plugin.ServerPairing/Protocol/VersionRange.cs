using System;
using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The lowest and the highest protocol version one side speaks.
/// </summary>
/// <remarks>
/// A <c>hello</c> carries a range rather than a single version, which is what lets two servers
/// that upgraded at different moments still find a version in common. The range is two of the
/// fields <c>docs/protocol.md</c> gives a limit to, so it is parsed against that limit rather
/// than against whatever <c>int.Parse</c> would take, and a value outside it is refused rather
/// than clamped to the nearest thing that fits.
/// <para>
/// Nothing here decides which versions this server supports. That is
/// <see cref="SupportedVersions"/>, and keeping the two apart is what lets a test drive the
/// selection over ranges this server does not ship, which is the only way the rule can be
/// exercised while there is one version.
/// </para>
/// </remarks>
public readonly struct VersionRange : IEquatable<VersionRange>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VersionRange"/> struct.
    /// </summary>
    /// <param name="low">The lowest version spoken.</param>
    /// <param name="high">The highest version spoken.</param>
    /// <exception cref="ArgumentOutOfRangeException">Where either endpoint is negative, or the
    /// low endpoint is above the high one.</exception>
    public VersionRange(int low, int high)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(low);
        ArgumentOutOfRangeException.ThrowIfLessThan(high, low);

        Low = low;
        High = high;
    }

    /// <summary>
    /// Gets the lowest version spoken.
    /// </summary>
    public int Low { get; }

    /// <summary>
    /// Gets the highest version spoken.
    /// </summary>
    public int High { get; }

    /// <summary>
    /// Whether two ranges are the same range.
    /// </summary>
    /// <param name="left">One range.</param>
    /// <param name="right">The other.</param>
    /// <returns>True where both endpoints agree.</returns>
    public static bool operator ==(VersionRange left, VersionRange right) => left.Equals(right);

    /// <summary>
    /// Whether two ranges are different ranges.
    /// </summary>
    /// <param name="left">One range.</param>
    /// <param name="right">The other.</param>
    /// <returns>True where either endpoint differs.</returns>
    public static bool operator !=(VersionRange left, VersionRange right) => !left.Equals(right);

    /// <summary>
    /// Reads a range out of the two fields a <c>hello</c> carries.
    /// </summary>
    /// <remarks>
    /// Both endpoints are judged by <see cref="FieldShape.IsUnsignedInteger"/> at the version
    /// digit limit before either is converted, so a leading zero, a sign, whitespace, a value
    /// past the digit limit and an empty field are all refused here rather than reaching a
    /// parser that would accept some of them. A low endpoint above the high one is refused
    /// too: it is not a range, and the alternative is a server silently swapping two numbers a
    /// peer sent deliberately.
    /// </remarks>
    /// <param name="low">The low field, as it arrived.</param>
    /// <param name="high">The high field, as it arrived.</param>
    /// <param name="range">The range read, where this returns true.</param>
    /// <returns>True where both fields are versions and together they are a range.</returns>
    public static bool TryParse(string? low, string? high, out VersionRange range)
    {
        range = default;

        if (!FieldShape.IsUnsignedInteger(low, FieldShape.VersionDigitLimit)
            || !FieldShape.IsUnsignedInteger(high, FieldShape.VersionDigitLimit))
        {
            return false;
        }

        var lowValue = int.Parse(low!, NumberStyles.None, CultureInfo.InvariantCulture);
        var highValue = int.Parse(high!, NumberStyles.None, CultureInfo.InvariantCulture);

        if (lowValue > highValue)
        {
            return false;
        }

        range = new VersionRange(lowValue, highValue);
        return true;
    }

    /// <summary>
    /// Whether a version is inside this range.
    /// </summary>
    /// <param name="version">The version to judge.</param>
    /// <returns>True where it is at or between the endpoints.</returns>
    public bool Includes(int version) => version >= Low && version <= High;

    /// <inheritdoc/>
    public bool Equals(VersionRange other) => Low == other.Low && High == other.High;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is VersionRange other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Low, High);

    /// <inheritdoc/>
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Low}-{High}");
}
