using System;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Api;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The bound on how much of the peer plane one claimed identifier may use.
/// </summary>
/// <remarks>
/// Every case moves time by handing in a later instant rather than by waiting for one, which
/// is the rule issue #26 owns and the reason nothing here sleeps.
/// </remarks>
public class ArrivalLimitTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string OtherPairingId = "1122334455667788990011223344aabb";

    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// A pairing may spend its allowance and is refused on the next arrival. The refusal is
    /// the whole point of the bound, and the arrival before it is what says the bound is not
    /// simply refusing everything.
    /// </summary>
    [Fact]
    public void APairingIsAdmittedUpToItsAllowanceAndRefusedAfterIt()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start));
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start));
        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, limit.Counted(PairingId));
    }

    /// <summary>
    /// The allowance comes back when the window has run out, and not one second before it.
    /// Both edges are asserted, because a bound that recovers early is no bound and one that
    /// never recovers ends a pairing for a burst.
    /// </summary>
    [Fact]
    public void TheAllowanceComesBackWhenTheWindowHasRunOut()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            limit.Admit(PairingId, Start);
        }

        var justBefore = Start.AddSeconds(ArrivalLimit.WindowSeconds - 1);
        var atTheEnd = Start.AddSeconds(ArrivalLimit.WindowSeconds);

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, justBefore));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, atTheEnd));
        Assert.Equal(1, limit.Counted(PairingId));
    }

    /// <summary>
    /// A caller that keeps sending after it has been refused does not push its own window
    /// further out. Counting a refused arrival would let a flood hold a pairing's allowance
    /// shut for as long as it kept sending, which is a bound that punishes the pairing rather
    /// than the flood.
    /// </summary>
    [Fact]
    public void ARefusedArrivalDoesNotPushTheWindowFurtherOut()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            limit.Admit(PairingId, Start);
        }

        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start.AddSeconds(30)));
        }

        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, limit.Counted(PairingId));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start.AddSeconds(ArrivalLimit.WindowSeconds)));
    }

    /// <summary>
    /// One pairing spending its allowance leaves another pairing's untouched. This is the
    /// property the whole per-identifier shape exists for: a flood is confined to the
    /// identifier it claims instead of starving every pairing on the plane.
    /// </summary>
    [Fact]
    public void OnePairingSpendingItsAllowanceLeavesAnothersAlone()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            limit.Admit(PairingId, Start);
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(OtherPairingId, Start));
        Assert.Equal(1, limit.Counted(OtherPairingId));
    }

    /// <summary>
    /// The enrolment identifier is held to the harder allowance, and it is harder rather than
    /// merely different: it is the one a stranger reaches without knowing anything.
    /// </summary>
    [Fact]
    public void TheEnrolmentIdentifierIsHeldToTheHarderAllowance()
    {
        var limit = new ArrivalLimit();

        Assert.True(ArrivalLimit.ArrivalsPerEnrolment < ArrivalLimit.ArrivalsPerPairing);

        for (var i = 0; i < ArrivalLimit.ArrivalsPerEnrolment; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(ArrivalLimit.EnrolmentPairingId, Start));
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(ArrivalLimit.EnrolmentPairingId, Start));
        Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, limit.AllowanceFor(ArrivalLimit.EnrolmentPairingId));
    }

    /// <summary>
    /// The identifier every hello carries is the one the specification fixes. A limit written
    /// against a different spelling of it would hold every enrolment to the ordinary
    /// allowance and nothing would say so.
    /// </summary>
    [Fact]
    public void TheEnrolmentIdentifierIsTheOneTheSpecificationFixes()
    {
        Assert.Equal(new string('0', 32), ArrivalLimit.EnrolmentPairingId);
    }

    /// <summary>
    /// Everything this protocol cannot read an identifier out of is counted together and
    /// under the harder allowance. Counting each spelling on its own would hand a stranger a
    /// fresh allowance per spelling and this table an entry per spelling, and none of them
    /// could ever verify.
    /// </summary>
    [Fact]
    public void EverythingWithoutAReadableIdentifierSharesOneAllowance()
    {
        var limit = new ArrivalLimit();
        var unreadable = new string?[] { null, string.Empty, "not-an-identifier", "9F8C1D2B3A4E5F60718293A4B5C6D7E8", "9f8c1d2b3a4e5f60718293a4b5c6d7e" };

        foreach (var claimed in unreadable)
        {
            Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, limit.AllowanceFor(claimed));
        }

        for (var i = 0; i < ArrivalLimit.ArrivalsPerEnrolment; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(unreadable[i % unreadable.Length], Start));
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(null, Start));
        Assert.Equal(1, limit.Counting());
    }

    /// <summary>
    /// A stranger claiming a fresh identifier per request fills the table and is then refused
    /// for want of room, rather than growing it without end. What is asserted with it is that
    /// no counted identifier was displaced to make that room: a pairing already inside the
    /// table keeps the count it had, so the flood cannot hand anybody's allowance back.
    /// </summary>
    [Fact]
    public void AFloodOfFreshIdentifiersFillsTheTableAndDisplacesNobody()
    {
        var limit = new ArrivalLimit();

        limit.Admit(PairingId, Start);

        for (var i = 0; i < ArrivalLimit.PairingsCounted - 1; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(Fresh(i), Start));
        }

        Assert.Equal(ArrivalLimit.PairingsCounted, limit.Counting());
        Assert.Equal(ArrivalOutcome.NoRoomToCount, limit.Admit(Fresh(ArrivalLimit.PairingsCounted), Start));
        Assert.Equal(1, limit.Counted(PairingId));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start));
    }

    /// <summary>
    /// Room comes back when the windows in the table have run out, so a table filled by a
    /// flood is a refusal for one window rather than for the life of the process.
    /// </summary>
    [Fact]
    public void RoomComesBackWhenTheWindowsInTheTableHaveRunOut()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.PairingsCounted; i++)
        {
            limit.Admit(Fresh(i), Start);
        }

        Assert.Equal(ArrivalOutcome.NoRoomToCount, limit.Admit(PairingId, Start));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start.AddSeconds(ArrivalLimit.WindowSeconds)));
        Assert.Equal(1, limit.Counting());
    }

    /// <summary>
    /// A clock that moves backwards does not hand an allowance back. Of the two directions
    /// that is the safe one: a window kept longer than it needs to be refuses a peer that is
    /// inside its rate, and a window ended early is a bound an attacker turns off by making a
    /// server's clock move.
    /// </summary>
    [Fact]
    public void AClockThatMovesBackwardsDoesNotHandTheAllowanceBack()
    {
        var limit = new ArrivalLimit();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            limit.Admit(PairingId, Start);
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start.AddSeconds(-3600)));
        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, limit.Counted(PairingId));
    }

    /// <summary>
    /// The third done condition of issue #28, in its own words: the limit refuses after its
    /// CONFIGURED count and recovers after the fake clock advances. The numbers here are none
    /// of the defaults, so a limit that ignored what it was built with and ran on the constants
    /// would refuse in the wrong place and this would redden.
    /// </summary>
    [Fact]
    public void TheLimitRefusesAfterItsConfiguredCountAndRecoversWhenTheClockAdvances()
    {
        var limit = new ArrivalLimit(10, 3, 2);

        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start));
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start));
        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(PairingId, Start.AddSeconds(9)));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start.AddSeconds(10)));
    }

    /// <summary>
    /// The configured enrolment allowance is the one the enrolment identifier is held to, and
    /// it is spent separately from the pairing allowance the same way the defaults are.
    /// </summary>
    [Fact]
    public void TheConfiguredEnrolmentAllowanceIsTheHarderOne()
    {
        var limit = new ArrivalLimit(10, 3, 2);

        Assert.Equal(2, limit.AllowanceFor(ArrivalLimit.EnrolmentPairingId));
        Assert.Equal(3, limit.AllowanceFor(PairingId));

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(ArrivalLimit.EnrolmentPairingId, Start));
        }

        Assert.Equal(ArrivalOutcome.TooMany, limit.Admit(ArrivalLimit.EnrolmentPairingId, Start));
        Assert.Equal(ArrivalOutcome.Admitted, limit.Admit(PairingId, Start));
    }

    /// <summary>
    /// A value outside its bounds is refused where the limit is built rather than clamped, so
    /// the bound holds for every caller and not only for the one that read a configuration
    /// file.
    /// </summary>
    [Theory]
    [InlineData(0, 60, 6)]
    [InlineData(-1, 60, 6)]
    [InlineData(ArrivalLimit.MaximumWindowSeconds + 1, 60, 6)]
    [InlineData(60, 0, 6)]
    [InlineData(60, ArrivalLimit.MaximumArrivals + 1, 6)]
    [InlineData(60, 60, 0)]
    [InlineData(60, 60, ArrivalLimit.MaximumArrivals + 1)]
    public void AnAllowanceOutsideItsBoundsIsRefusedRatherThanClamped(int seconds, int perPairing, int perEnrolment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ArrivalLimit(seconds, perPairing, perEnrolment));
    }

    /// <summary>
    /// The enrolment allowance is never the softer of the two. It is the one a stranger
    /// reaches without knowing anything, and a limit built the other way round would leave the
    /// argument for the harder allowance in the comments and nowhere else.
    /// </summary>
    [Fact]
    public void TheEnrolmentAllowanceIsNeverLargerThanThePairingAllowance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArrivalLimit(60, 10, 11));

        var flat = new ArrivalLimit(60, 10, 10);

        Assert.Equal(10, flat.PerEnrolment);
    }

    /// <summary>
    /// The limit a caller who chooses nothing gets is the one the constants argue, so the
    /// defaults and the constants cannot drift apart.
    /// </summary>
    [Fact]
    public void TheLimitBuiltWithNoArgumentsCarriesTheDefaults()
    {
        var limit = new ArrivalLimit();

        Assert.Equal(ArrivalLimit.WindowSeconds, limit.CountedOverSeconds);
        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, limit.PerPairing);
        Assert.Equal(ArrivalLimit.ArrivalsPerEnrolment, limit.PerEnrolment);
    }

    /// <summary>
    /// The numbers themselves, so that one moved in the source without its reason being
    /// rewritten is a red suite rather than a silently different bound. They are not restated
    /// from the type: each is compared against what this protocol needs it to be.
    /// </summary>
    [Fact]
    public void TheBoundsAreTheOnesTheDocumentArgues()
    {
        Assert.Equal(60, ArrivalLimit.WindowSeconds);
        Assert.Equal(60, ArrivalLimit.ArrivalsPerPairing);
        Assert.Equal(6, ArrivalLimit.ArrivalsPerEnrolment);
        Assert.Equal(4096, ArrivalLimit.PairingsCounted);
        Assert.Equal(3600, ArrivalLimit.MaximumWindowSeconds);
        Assert.Equal(3600, ArrivalLimit.MaximumArrivals);
    }

    /// <summary>
    /// Nothing is counted for an identifier that has never arrived, and nothing is counted
    /// before the first arrival. This is the floor under every case above: they compare
    /// counts, and a count that is always zero would let several of them pass over a type
    /// that counts nothing at all.
    /// </summary>
    [Fact]
    public void NothingIsCountedBeforeAnythingArrives()
    {
        var limit = new ArrivalLimit();

        Assert.Equal(0, limit.Counting());
        Assert.Equal(0, limit.Counted(PairingId));

        limit.Admit(PairingId, Start);

        Assert.Equal(1, limit.Counting());
        Assert.Equal(1, limit.Counted(PairingId));
        Assert.Equal(0, limit.Counted(OtherPairingId));
    }

    /// <summary>
    /// A distinct well-formed identifier, built so a case can produce as many as the table
    /// holds without writing them out.
    /// </summary>
    /// <param name="ordinal">Which one.</param>
    /// <returns>32 lowercase hex characters.</returns>
    /// <remarks>
    /// It counts from one rather than from zero, because zero written in 32 hex characters is
    /// the enrolment identifier, which carries the harder allowance and would make a case
    /// about the ordinary one quietly about both.
    /// </remarks>
    private static string Fresh(int ordinal)
        => (ordinal + 1).ToString("x32", CultureInfo.InvariantCulture);
}
