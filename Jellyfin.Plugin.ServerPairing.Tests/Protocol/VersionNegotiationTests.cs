using System;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Reading a version range off a <c>hello</c>, and selecting the one version a pairing runs at.
/// </summary>
/// <remarks>
/// The rule under test is the versions section of <c>docs/protocol.md</c>: the receiver selects
/// the highest version inside both ranges, and where the ranges do not overlap there is no
/// version and no fallback.
/// <para>
/// Almost every case here drives the two-range overload rather than the one this build would
/// call, and that is the point rather than a convenience. This build speaks a range with one
/// member, so a selection made through it returns 1 whatever the code does, and a suite that
/// only exercised the shipping range would pass against a method that returned the constant.
/// The ranges below are ranges no build ships.
/// </para>
/// </remarks>
public class VersionNegotiationTests
{
    /// <summary>
    /// Two overlapping ranges settle on the highest version both speak, not the lowest. A
    /// pairing that took the lowest would hold two upgraded servers at the oldest wire either
    /// of them ever supported.
    /// </summary>
    [Theory]
    [InlineData(1, 3, 2, 5, 3)]
    [InlineData(2, 5, 1, 3, 3)]
    [InlineData(1, 9, 1, 9, 9)]
    [InlineData(4, 4, 1, 7, 4)]
    [InlineData(1, 7, 4, 4, 4)]
    [InlineData(1, 2, 2, 2, 2)]
    public void TheHighestVersionBothSidesSpeakIsSelected(
        int localLow, int localHigh, int offeredLow, int offeredHigh, int expected)
    {
        var selection = VersionNegotiation.Select(
            new VersionRange(localLow, localHigh),
            new VersionRange(offeredLow, offeredHigh));

        Assert.Equal(VersionOutcome.Selected, selection.Outcome);
        Assert.Equal(expected, selection.Version);
    }

    /// <summary>
    /// Ranges that do not meet produce no version, in both directions, and the number carried
    /// beside the refusal is not a version this protocol has.
    /// </summary>
    /// <remarks>
    /// The near-miss this case is built for is a selection that takes the lower of the two high
    /// endpoints and stops there. That value is inside one range and outside the other in every
    /// row below, so an implementation that skipped the containment check would answer 2, 3 or
    /// 1 here and read as a successful negotiation at a version one side cannot speak.
    /// </remarks>
    [Theory]
    [InlineData(1, 2, 5, 9)]
    [InlineData(5, 9, 1, 2)]
    [InlineData(1, 1, 2, 2)]
    [InlineData(2, 2, 1, 1)]
    [InlineData(3, 3, 4, 9)]
    public void RangesThatDoNotOverlapSelectNothing(
        int localLow, int localHigh, int offeredLow, int offeredHigh)
    {
        var selection = VersionNegotiation.Select(
            new VersionRange(localLow, localHigh),
            new VersionRange(offeredLow, offeredHigh));

        Assert.Equal(VersionOutcome.NoVersionInCommon, selection.Outcome);
        Assert.Equal(0, selection.Version);
    }

    /// <summary>
    /// Which side is the local one does not change the answer. Two servers that ran the
    /// selection against each other and disagreed about the result would each be signing at a
    /// version the other refuses, and neither operator would see why.
    /// </summary>
    [Theory]
    [InlineData(1, 3, 2, 5)]
    [InlineData(1, 2, 5, 9)]
    [InlineData(2, 7, 2, 7)]
    [InlineData(4, 4, 1, 9)]
    public void SelectionDoesNotDependOnWhichSideIsAsking(
        int leftLow, int leftHigh, int rightLow, int rightHigh)
    {
        var left = new VersionRange(leftLow, leftHigh);
        var right = new VersionRange(rightLow, rightHigh);

        var one = VersionNegotiation.Select(left, right);
        var other = VersionNegotiation.Select(right, left);

        Assert.Equal(one.Outcome, other.Outcome);
        Assert.Equal(one.Version, other.Version);
    }

    /// <summary>
    /// The overload a caller would use reads the set this build declares rather than a range of
    /// its own.
    /// </summary>
    /// <remarks>
    /// What this proves is bounded and the bound is stated rather than left for a reader to
    /// find. <see cref="SupportedVersions"/> holds one version today, so this case cannot tell
    /// a method that reads the declared range from one that returns
    /// <see cref="SupportedVersions.Highest"/> directly. What it does refuse is a second
    /// hard-coded range beside the declared one: a peer offering versions above the set is
    /// refused, and a peer offering versions below it is refused, so the overload is bounded on
    /// both sides by the same two numbers the set carries.
    /// </remarks>
    [Fact]
    public void TheShippingOverloadReadsTheDeclaredSet()
    {
        var matching = VersionNegotiation.Select(SupportedVersions.Range);

        Assert.Equal(VersionOutcome.Selected, matching.Outcome);
        Assert.Equal(SupportedVersions.Highest, matching.Version);

        var above = VersionNegotiation.Select(
            new VersionRange(SupportedVersions.Highest + 1, SupportedVersions.Highest + 4));
        Assert.Equal(VersionOutcome.NoVersionInCommon, above.Outcome);

        var below = VersionNegotiation.Select(new VersionRange(0, SupportedVersions.Lowest - 1));
        Assert.Equal(VersionOutcome.NoVersionInCommon, below.Outcome);

        var spanning = VersionNegotiation.Select(
            new VersionRange(SupportedVersions.Lowest - 1, SupportedVersions.Highest + 1));
        Assert.Equal(VersionOutcome.Selected, spanning.Outcome);
        Assert.Equal(SupportedVersions.Highest, spanning.Version);
    }

    /// <summary>
    /// The declared set is a range rather than two numbers that happen to sit beside each
    /// other, and the value it hands out agrees with the two constants.
    /// </summary>
    [Fact]
    public void TheDeclaredSetIsARange()
    {
        Assert.True(SupportedVersions.Lowest <= SupportedVersions.Highest);
        Assert.Equal(SupportedVersions.Lowest, SupportedVersions.Range.Low);
        Assert.Equal(SupportedVersions.Highest, SupportedVersions.Range.High);
        Assert.True(SupportedVersions.Range.Includes(SupportedVersions.Lowest));
        Assert.True(SupportedVersions.Range.Includes(SupportedVersions.Highest));
    }

    /// <summary>
    /// A range reads off two fields that are versions.
    /// </summary>
    [Theory]
    [InlineData("1", "1", 1, 1)]
    [InlineData("1", "3", 1, 3)]
    [InlineData("0", "9", 0, 9)]
    [InlineData("9999", "9999", 9999, 9999)]
    [InlineData("1", "9999", 1, 9999)]
    public void TwoVersionFieldsRead(string low, string high, int expectedLow, int expectedHigh)
    {
        Assert.True(VersionRange.TryParse(low, high, out var range));
        Assert.Equal(expectedLow, range.Low);
        Assert.Equal(expectedHigh, range.High);
    }

    /// <summary>
    /// Anything that is not two versions, or is not a range, is refused rather than repaired.
    /// </summary>
    /// <remarks>
    /// A low endpoint above the high one is in this list on purpose. Swapping the two would
    /// read as tolerance and would silently accept a range no peer meant to send, and the
    /// fields are covered by the signature, so a pair arriving in that order is a peer that
    /// signed them in that order.
    /// <para>
    /// The digit limit, the leading zero and the sign are the version field's own limits from
    /// <c>docs/protocol.md</c>, and they are asserted here as well as on the envelope because
    /// these two fields arrive in a body rather than in a header.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("3", "1")]
    [InlineData("2", "1")]
    [InlineData("10000", "10000")]
    [InlineData("1", "10000")]
    [InlineData("01", "3")]
    [InlineData("1", "03")]
    [InlineData("+1", "3")]
    [InlineData("-1", "3")]
    [InlineData("1", "3 ")]
    [InlineData(" 1", "3")]
    [InlineData("1.0", "3")]
    [InlineData("1", "0x3")]
    [InlineData("one", "three")]
    [InlineData("", "3")]
    [InlineData("1", "")]
    [InlineData(null, "3")]
    [InlineData("1", null)]
    public void AnythingThatIsNotARangeOfVersionsIsRefused(string? low, string? high)
    {
        Assert.False(VersionRange.TryParse(low, high, out var range));
        Assert.Equal(0, range.Low);
        Assert.Equal(0, range.High);
    }

    /// <summary>
    /// A range cannot be built the wrong way round in code either, so the rule holds for a
    /// caller that constructs one rather than parsing it.
    /// </summary>
    [Fact]
    public void ARangeCannotBeBuiltTheWrongWayRound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VersionRange(3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new VersionRange(-1, 3));
    }

    /// <summary>
    /// A range holds the versions between its endpoints and nothing outside them.
    /// </summary>
    [Theory]
    [InlineData(2, 5, 1, false)]
    [InlineData(2, 5, 2, true)]
    [InlineData(2, 5, 4, true)]
    [InlineData(2, 5, 5, true)]
    [InlineData(2, 5, 6, false)]
    public void ARangeHoldsWhatIsBetweenItsEndpoints(int low, int high, int version, bool held)
    {
        Assert.Equal(held, new VersionRange(low, high).Includes(version));
    }
}
