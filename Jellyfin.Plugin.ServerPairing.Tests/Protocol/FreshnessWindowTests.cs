using System;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Replay, skew and the bound on the nonce store.
/// </summary>
/// <remarks>
/// Every number asserted here is read out of the freshness section of
/// <c>docs/protocol.md</c>: 300 seconds either side, and a nonce remembered for 600, which is
/// the window taken in both directions and is the widest gap between the first arrival of a
/// request and the last instant a copy of it would still be inside the window. The clock is an
/// argument in every case, so nothing here waits for real time.
/// </remarks>
public class FreshnessWindowTests
{
    private const string PairingId = "3b1f0c7d9e2a48561bd0f37ac5e6902f";
    private const string OtherPairingId = "8c4e17a0d5b96238fe0417cba2d3560e";
    private const string Nonce = "5d2f81c0ab34e769025fbc18d3a7e64b";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// A request inside the window carrying a nonce nobody has seen is fresh, and the store
    /// remembers exactly the one nonce.
    /// </summary>
    /// <summary>
    /// A skew outside its bounds is refused where the window is built rather than clamped, so
    /// the bound holds for every caller and not only for the one that read a configuration
    /// file.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(FreshnessWindow.MaximumWindowSeconds + 1)]
    public void ASkewOutsideItsBoundsIsRefusedAtConstruction(int seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new FreshnessWindow(seconds));
    }

    /// <summary>
    /// How long a nonce is remembered follows the skew rather than being chosen beside it. Two
    /// numbers set apart are two numbers an operator can put into a state where the store
    /// forgets a replay it exists to refuse, so this is the one relation the type does not let
    /// anybody break.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(FreshnessWindow.WindowSeconds)]
    [InlineData(FreshnessWindow.MaximumWindowSeconds)]
    public void WhatIsRememberedIsTheSkewTakenInBothDirections(int seconds)
    {
        Assert.Equal(seconds * 2, new FreshnessWindow(seconds).RememberedSeconds);
    }

    /// <summary>
    /// The window a caller who chooses nothing gets is the one the constant argues.
    /// </summary>
    [Fact]
    public void TheWindowBuiltWithNoArgumentsCarriesTheDefault()
    {
        Assert.Equal(FreshnessWindow.WindowSeconds, new FreshnessWindow().AcceptedSkewSeconds);
        Assert.Equal(300, FreshnessWindow.WindowSeconds);
        Assert.Equal(900, FreshnessWindow.MaximumWindowSeconds);
    }

    [Fact]
    public void ARequestNobodyHasSeenIsFresh()
    {
        var window = new FreshnessWindow();

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, Stamp(Now), Now));
        Assert.Equal(1, window.Remembered(PairingId));
    }

    /// <summary>
    /// A valid request replayed inside the window is refused, and refused for the nonce rather
    /// than for the timestamp.
    /// </summary>
    [Fact]
    public void AReplayInsideTheWindowIsRefusedForTheNonce()
    {
        var window = new FreshnessWindow();
        var stamp = Stamp(Now);

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, stamp, Now));

        Assert.Equal(FreshnessOutcome.AlreadySeen, window.Judge(PairingId, Nonce, stamp, Now));
        Assert.Equal(
            FreshnessOutcome.AlreadySeen,
            window.Judge(PairingId, Nonce, stamp, Now.AddSeconds(FreshnessWindow.WindowSeconds)));
    }

    /// <summary>
    /// The same request replayed once the window has passed is refused for the timestamp
    /// rather than for the nonce. The two reasons are distinguishable, and this is the case
    /// that says which one wins when both apply.
    /// </summary>
    [Fact]
    public void AReplayOutsideTheWindowIsRefusedForTheTimestamp()
    {
        var window = new FreshnessWindow();
        var stamp = Stamp(Now);

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, stamp, Now));

        var later = Now.AddSeconds(FreshnessWindow.WindowSeconds + 1);

        Assert.Equal(FreshnessOutcome.OutsideTheWindow, window.Judge(PairingId, Nonce, stamp, later));
    }

    /// <summary>
    /// A request skewed forward beyond the window is refused. A request from the future is as
    /// suspicious as one from the past, so the window is checked in both directions.
    /// </summary>
    /// <param name="skewSeconds">How far the timestamp is from this server's clock.</param>
    /// <param name="expected">What the document's window gives for that distance.</param>
    [Theory]
    [InlineData(0, FreshnessOutcome.Fresh)]
    [InlineData(300, FreshnessOutcome.Fresh)]
    [InlineData(-300, FreshnessOutcome.Fresh)]
    [InlineData(301, FreshnessOutcome.OutsideTheWindow)]
    [InlineData(-301, FreshnessOutcome.OutsideTheWindow)]
    [InlineData(86400, FreshnessOutcome.OutsideTheWindow)]
    [InlineData(-86400, FreshnessOutcome.OutsideTheWindow)]
    public void TheWindowIsThreeHundredSecondsInEitherDirection(int skewSeconds, FreshnessOutcome expected)
    {
        var window = new FreshnessWindow();

        Assert.Equal(expected, window.Judge(PairingId, Nonce, Stamp(Now.AddSeconds(skewSeconds)), Now));
    }

    /// <summary>
    /// A request refused for its timestamp is not remembered. A request nothing will accept
    /// must not be able to take a place in the store, or filling it costs an attacker nothing
    /// but a wrong clock.
    /// </summary>
    [Fact]
    public void ARequestOutsideTheWindowIsNotRemembered()
    {
        var window = new FreshnessWindow();

        Assert.Equal(
            FreshnessOutcome.OutsideTheWindow,
            window.Judge(PairingId, Nonce, Stamp(Now.AddSeconds(-3600)), Now));
        Assert.Equal(0, window.Remembered(PairingId));
        Assert.Equal(0, window.PairingsRemembered());
    }

    /// <summary>
    /// The store is filled past its bound. Memory stays bounded, nothing inside the window is
    /// forgotten to make room, and the request that finds no room is refused rather than
    /// remembered in place of something else.
    /// </summary>
    [Fact]
    public void FillingTheStorePastItsBoundForgetsNothingInsideTheWindow()
    {
        var window = new FreshnessWindow();
        var stamp = Stamp(Now);

        for (var i = 0; i < FreshnessWindow.NoncesPerPairing; i++)
        {
            Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, NonceNumber(i), stamp, Now));
        }

        Assert.Equal(FreshnessWindow.NoncesPerPairing, window.Remembered(PairingId));

        for (var i = 0; i < 200; i++)
        {
            Assert.Equal(
                FreshnessOutcome.NoRoomToRemember,
                window.Judge(PairingId, NonceNumber(FreshnessWindow.NoncesPerPairing + i), stamp, Now));
        }

        Assert.Equal(FreshnessWindow.NoncesPerPairing, window.Remembered(PairingId));

        // Every nonce accepted before the store filled is still remembered, so none of them
        // was dropped to make room for one of the two hundred that were refused.
        for (var i = 0; i < FreshnessWindow.NoncesPerPairing; i++)
        {
            Assert.Equal(FreshnessOutcome.AlreadySeen, window.Judge(PairingId, NonceNumber(i), stamp, Now));
        }
    }

    /// <summary>
    /// A nonce is forgotten once it is older than the remembered span, and not before. The
    /// span is exactly the window taken in both directions, which is the widest gap between
    /// the first arrival of a request and the last instant a copy of it would still be inside
    /// the window, so a nonce cannot age out while one carrying it would still be accepted.
    /// The two cases below sit either side of that boundary, and the margin is nothing.
    /// </summary>
    [Fact]
    public void ANonceIsForgottenByAgeAndNotBefore()
    {
        var window = new FreshnessWindow();

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, Stamp(Now), Now));

        var justInside = Now.AddSeconds(window.RememberedSeconds);
        var justOutside = Now.AddSeconds(window.RememberedSeconds + 1);

        // The replay's own timestamp has to be inside the window at the later instant, or the
        // timestamp reason would answer before the nonce reason is reached.
        Assert.Equal(
            FreshnessOutcome.AlreadySeen,
            window.Judge(PairingId, Nonce, Stamp(justInside), justInside));

        Assert.Equal(
            FreshnessOutcome.Fresh,
            window.Judge(PairingId, Nonce, Stamp(justOutside), justOutside));
    }

    /// <summary>
    /// The store fills again once its entries age out, so a pairing that hit the bound is not
    /// refused for ever.
    /// </summary>
    [Fact]
    public void TheStoreRecoversOnceItsEntriesAgeOut()
    {
        var window = new FreshnessWindow();

        for (var i = 0; i < FreshnessWindow.NoncesPerPairing; i++)
        {
            window.Judge(PairingId, NonceNumber(i), Stamp(Now), Now);
        }

        var later = Now.AddSeconds(window.RememberedSeconds + 1);

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, Stamp(later), later));
        Assert.Equal(1, window.Remembered(PairingId));
    }

    /// <summary>
    /// The store is per pairing. One pairing's nonce says nothing about another's, and one
    /// pairing filling its store does not refuse another's traffic.
    /// </summary>
    [Fact]
    public void TheStoreIsPerPairing()
    {
        var window = new FreshnessWindow();
        var stamp = Stamp(Now);

        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(PairingId, Nonce, stamp, Now));
        Assert.Equal(FreshnessOutcome.Fresh, window.Judge(OtherPairingId, Nonce, stamp, Now));

        for (var i = 0; i < FreshnessWindow.NoncesPerPairing; i++)
        {
            window.Judge(PairingId, NonceNumber(i), stamp, Now);
        }

        Assert.Equal(
            FreshnessOutcome.NoRoomToRemember,
            window.Judge(PairingId, NonceNumber(999999), stamp, Now));
        Assert.Equal(
            FreshnessOutcome.Fresh,
            window.Judge(OtherPairingId, NonceNumber(999999), stamp, Now));
    }

    /// <summary>
    /// A pairing that ends takes its remembered nonces with it, because a pairing with no
    /// future request has nothing left to replay.
    /// </summary>
    [Fact]
    public void APairingThatIsForgottenTakesItsNoncesWithIt()
    {
        var window = new FreshnessWindow();

        window.Judge(PairingId, Nonce, Stamp(Now), Now);
        window.Judge(OtherPairingId, Nonce, Stamp(Now), Now);

        Assert.Equal(2, window.PairingsRemembered());

        window.Forget(PairingId);

        Assert.Equal(0, window.Remembered(PairingId));
        Assert.Equal(1, window.Remembered(OtherPairingId));
        Assert.Equal(1, window.PairingsRemembered());
    }

    /// <summary>
    /// A timestamp or a nonce outside its shape is refused as malformed rather than parsed
    /// leniently. The shape check refuses these before a signature is computed, and this is
    /// the second refusal behind it.
    /// </summary>
    /// <param name="nonce">The nonce that arrived.</param>
    /// <param name="timestamp">The timestamp that arrived.</param>
    [Theory]
    [InlineData(Nonce, "")]
    [InlineData(Nonce, "-1786000000")]
    [InlineData(Nonce, "01786000000")]
    [InlineData(Nonce, "1786000000.5")]
    [InlineData(Nonce, "999999999999999999999")]
    [InlineData("", "1786000000")]
    [InlineData("5D2F81C0AB34E769025FBC18D3A7E64B", "1786000000")]
    [InlineData("5d2f81c0", "1786000000")]
    public void AValueOutsideItsShapeIsMalformed(string nonce, string timestamp)
    {
        var window = new FreshnessWindow();

        Assert.Equal(FreshnessOutcome.Malformed, window.Judge(PairingId, nonce, timestamp, Now));
        Assert.Equal(0, window.Remembered(PairingId));
    }

    /// <summary>
    /// The restart behaviour the document states, asserted as the document states it. A new
    /// store is what a restarted process has, and a request replayed into one inside the
    /// window is accepted.
    /// </summary>
    /// <remarks>
    /// This asserts a gap rather than a protection, and it is not the gap being accepted.
    /// <c>docs/protocol.md</c> names it and leaves it to issue #21 to close or accept with a
    /// reason. That issue's own rule is that losing the store on a restart is acceptable only
    /// where the window is narrower than a restart takes, and 300 seconds is not, so by that
    /// rule the store is persisted. Nothing here persists anything, because what it would
    /// persist into is the store M4 owns. The case exists so that the behaviour in the tree is
    /// stated rather than assumed while that is outstanding.
    /// </remarks>
    [Fact]
    public void ARestartForgetsTheStoreAndAReplayInsideTheWindowIsAccepted()
    {
        var before = new FreshnessWindow();
        var stamp = Stamp(Now);

        Assert.Equal(FreshnessOutcome.Fresh, before.Judge(PairingId, Nonce, stamp, Now));
        Assert.Equal(FreshnessOutcome.AlreadySeen, before.Judge(PairingId, Nonce, stamp, Now));

        var afterTheRestart = new FreshnessWindow();

        Assert.Equal(FreshnessOutcome.Fresh, afterTheRestart.Judge(PairingId, Nonce, stamp, Now));
    }

    private static string Stamp(DateTimeOffset at)
        => at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static string NonceNumber(int index)
        => index.ToString("x8", CultureInfo.InvariantCulture) + "0123456789abcdef01234567";
}
