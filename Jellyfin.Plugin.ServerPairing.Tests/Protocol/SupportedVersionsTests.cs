using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The set of protocol versions this build speaks, and what it answers about a version that
/// arrived on the wire.
/// </summary>
/// <remarks>
/// The membership test is total: it takes the field as it arrived, judges its shape before its
/// value, and never throws. That is what lets the pairing plane ask it without having parsed
/// anything first, and it is why the shapes below are cases here rather than at the plane, which
/// cannot reach them - a request whose signature verified has already passed the same shape
/// predicate inside verification.
/// <para>
/// The expectations are derived from <see cref="SupportedVersions.Lowest"/> and
/// <see cref="SupportedVersions.Highest"/> rather than written as numbers, so a build that speaks
/// a second version is judged by the same cases instead of by cases that quietly became about
/// something else.
/// </para>
/// </remarks>
public class SupportedVersionsTests
{
    /// <summary>
    /// Every version inside the declared range is one this build speaks.
    /// </summary>
    [Fact]
    public void EveryVersionInTheRangeIsSpoken()
    {
        for (var version = SupportedVersions.Lowest; version <= SupportedVersions.Highest; version++)
        {
            Assert.True(SupportedVersions.Speaks(version.ToString(CultureInfo.InvariantCulture)));
        }
    }

    /// <summary>
    /// One below the range and one above it are not, so the test is a range rather than a
    /// comparison against one end of it.
    /// </summary>
    [Fact]
    public void OneBelowTheRangeAndOneAboveItAreNotSpoken()
    {
        Assert.False(SupportedVersions.Speaks(
            (SupportedVersions.Lowest - 1).ToString(CultureInfo.InvariantCulture)));
        Assert.False(SupportedVersions.Speaks(
            (SupportedVersions.Highest + 1).ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// A field that is not a protocol version at all is not spoken, and none of these reaches a
    /// parser. Each is a spelling of a number this server would otherwise have accepted, which
    /// is what makes the list the interesting one rather than a list of nonsense.
    /// </summary>
    /// <param name="field">The field as it arrived.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("01")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("1.0")]
    [InlineData("one")]
    [InlineData("10000")]
    public void AFieldThatIsNotAVersionIsNotSpoken(string? field)
    {
        Assert.False(SupportedVersions.Speaks(field));
    }

    /// <summary>
    /// The shape is judged before the value, which is what the leading zero says: a value that
    /// would be inside the range once parsed is still refused where it is spelled in a way the
    /// specification does not allow.
    /// </summary>
    /// <remarks>
    /// Two implementations that disagree about how a number is spelled interoperate right up
    /// until one of them signs a field the other normalises, and the version is a field the
    /// signature covers.
    /// </remarks>
    [Fact]
    public void AVersionInTheRangeSpelledWithALeadingZeroIsNotSpoken()
    {
        var padded = "0" + SupportedVersions.Lowest.ToString(CultureInfo.InvariantCulture);

        Assert.True(SupportedVersions.Range.Includes(SupportedVersions.Lowest));
        Assert.False(SupportedVersions.Speaks(padded));
    }
}
