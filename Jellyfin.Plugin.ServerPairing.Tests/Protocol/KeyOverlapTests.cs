using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Rotation with an overlap, driven with both sides in one process.
/// </summary>
/// <remarks>
/// The two sides here are two objects rather than two servers. One holds the keys that verify
/// what arrives and the other holds the key it signs with, which is the whole of what a
/// direction is at this layer. What that leaves out is the network, the serialiser and the
/// routing, and the harness that would add them is issue #29.
/// <para>
/// Nothing in these cases waits. Every instant is a value handed to the type, so an overlap
/// that has run out is a different argument rather than a different day.
/// </para>
/// </remarks>
public class KeyOverlapTests
{
    private const string PairingId = "3b1f0c7d9e2a48561bd0f37ac5e6902f";
    private const string Version = "1";
    private const string Timestamp = "1786000000";

    /// <summary>
    /// The instant a rotation starts in these cases. It is a fixed value rather than the
    /// machine's clock, so a case that passes here passes on a machine whose clock is wrong.
    /// </summary>
    private static readonly DateTimeOffset RotatedAt = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// A peer that was switched off when the rotation started goes on being understood for the
    /// whole overlap, and is seen for what it is rather than merely accepted.
    /// </summary>
    /// <remarks>
    /// This is the reason the overlap exists. The side that rotates cannot know whether the
    /// peer received the replacement, and refusing it until it does would end the traffic at
    /// the moment of the rotation, which is the outage rotation is supposed to avoid.
    /// </remarks>
    [Fact]
    public void TrafficKeepsFlowingAcrossTheWholeOverlap()
    {
        var oldKey = Key();
        var replacement = Key();
        var endsAt = RotatedAt.AddSeconds(KeyOverlap.MaximumOverlapSeconds);

        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(RotationOutcome.Rotated, receiver.Rotate(replacement, RotatedAt, endsAt));

        // The peer is offline and goes on signing with the key it last knew about.
        foreach (var offset in new[] { 0, 1, 60, 3600, KeyOverlap.MaximumOverlapSeconds - 1 })
        {
            var at = RotatedAt.AddSeconds(offset);
            var request = Exchange(offset);

            Assert.Equal(
                KeyInUse.Superseded,
                receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), at));
        }

        // The peer comes back, learns the replacement and uses it.
        var caughtUp = Exchange(99);

        Assert.Equal(
            KeyInUse.Current,
            receiver.Verify(caughtUp, RequestAuthenticator.Sign(caughtUp, replacement), RotatedAt.AddSeconds(120)));
    }

    /// <summary>
    /// Once the overlap ends the superseded key stops verifying, and it stops on the instant
    /// rather than on the one after it.
    /// </summary>
    /// <remarks>
    /// The instant itself is the case worth having. An implementation that compares with the
    /// wrong one of two operators keeps the key alive for exactly one instant longer, which is
    /// the mistake nobody notices and nothing else here would catch.
    /// </remarks>
    [Theory]
    [InlineData(-1, KeyInUse.Superseded)]
    [InlineData(0, KeyInUse.None)]
    [InlineData(1, KeyInUse.None)]
    [InlineData(86400, KeyInUse.None)]
    public void TheSupersededKeyStopsVerifyingWhenTheOverlapEnds(int secondsFromTheEnd, KeyInUse expected)
    {
        var oldKey = Key();
        var endsAt = RotatedAt.AddSeconds(600);

        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(RotationOutcome.Rotated, receiver.Rotate(Key(), RotatedAt, endsAt));

        var request = Exchange(1);

        Assert.Equal(
            expected,
            receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), endsAt.AddSeconds(secondsFromTheEnd)));
    }

    /// <summary>
    /// A rotation given up halfway leaves both sides on the key they were both already using,
    /// and both can still talk.
    /// </summary>
    /// <remarks>
    /// The replacement is what has to go. It is the key only one side is known to hold, so
    /// ending on it is the state that strands a peer, and ending on no key at all is worse
    /// again.
    /// </remarks>
    [Fact]
    public void ARotationAbandonedHalfwayLeavesBothSidesOnTheOldKey()
    {
        var oldKey = Key();
        var replacement = Key();

        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(
            RotationOutcome.Rotated,
            receiver.Rotate(replacement, RotatedAt, RotatedAt.AddSeconds(600)));
        Assert.True(receiver.Abandon());
        Assert.False(receiver.IsRotating);
        Assert.Equal(1, receiver.LiveKeys(RotatedAt));

        var request = Exchange(2);
        var later = RotatedAt.AddSeconds(60);

        Assert.Equal(KeyInUse.Current, receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), later));
        Assert.Equal(KeyInUse.None, receiver.Verify(request, RequestAuthenticator.Sign(request, replacement), later));

        // The side that gave up signs with the old key again, so the peer that never heard
        // about the replacement can still verify what arrives.
        Assert.Equal(RequestAuthenticator.Sign(request, oldKey), receiver.Sign(request));
    }

    /// <summary>
    /// Two keys is the ceiling, and it holds across everything a rotation can do.
    /// </summary>
    /// <remarks>
    /// The walk matters more than the number. A count asserted once, straight after a
    /// rotation, says nothing about the case that produces a third key, which is a second
    /// rotation arriving while the first is still open.
    /// </remarks>
    [Fact]
    public void TheLiveKeyCountNeverExceedsTwo()
    {
        var overlap = new KeyOverlap(Key());
        var endsAt = RotatedAt.AddSeconds(600);

        Assert.Equal(1, overlap.LiveKeys(RotatedAt));

        Assert.Equal(RotationOutcome.Rotated, overlap.Rotate(Key(), RotatedAt, endsAt));
        Assert.Equal(2, overlap.LiveKeys(RotatedAt));

        Assert.Equal(
            RotationOutcome.AlreadyRotating,
            overlap.Rotate(Key(), RotatedAt.AddSeconds(1), endsAt.AddSeconds(600)));
        Assert.Equal(2, overlap.LiveKeys(RotatedAt.AddSeconds(1)));
        Assert.Equal(2, overlap.LiveKeys(endsAt.AddSeconds(-1)));

        Assert.Equal(1, overlap.LiveKeys(endsAt));
        Assert.True(overlap.CloseIfElapsed(endsAt));
        Assert.Equal(1, overlap.LiveKeys(endsAt));

        Assert.Equal(RotationOutcome.Rotated, overlap.Rotate(Key(), endsAt, endsAt.AddSeconds(600)));
        Assert.Equal(2, overlap.LiveKeys(endsAt));

        Assert.True(overlap.Abandon());
        Assert.Equal(1, overlap.LiveKeys(endsAt));
    }

    /// <summary>
    /// A second rotation inside an open overlap is refused, and refusing it changes nothing.
    /// </summary>
    /// <remarks>
    /// Accepting it has to do one of two things, and both are the failure. Either it keeps
    /// three keys live, or it drops a key the peer may still be using, which ends the traffic
    /// the overlap exists to keep flowing.
    /// </remarks>
    [Fact]
    public void ASecondRotationInsideAnOpenOverlapIsRefusedAndMovesNothing()
    {
        var oldKey = Key();
        var replacement = Key();
        var third = Key();
        var endsAt = RotatedAt.AddSeconds(600);

        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(RotationOutcome.Rotated, receiver.Rotate(replacement, RotatedAt, endsAt));
        Assert.Equal(
            RotationOutcome.AlreadyRotating,
            receiver.Rotate(third, RotatedAt.AddSeconds(1), endsAt.AddSeconds(600)));

        Assert.Equal(endsAt, receiver.OverlapEndsAt);

        var request = Exchange(3);
        var at = RotatedAt.AddSeconds(2);

        Assert.Equal(KeyInUse.Superseded, receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), at));
        Assert.Equal(KeyInUse.None, receiver.Verify(request, RequestAuthenticator.Sign(request, third), at));
    }

    /// <summary>
    /// A rotation asking for an overlap outside the maximum fails the rotation, and the
    /// pairing is left on the key it was using.
    /// </summary>
    /// <param name="seconds">How long the rotation asked the superseded key to go on verifying.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(86401)]
    [InlineData(604800)]
    public void AnOverlapOutsideTheMaximumFailsTheRotationRatherThanBeingShortened(int seconds)
    {
        var oldKey = Key();
        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(
            RotationOutcome.OutsideTheMaximum,
            receiver.Rotate(Key(), RotatedAt, RotatedAt.AddSeconds(seconds)));

        Assert.False(receiver.IsRotating);
        Assert.Equal(1, receiver.LiveKeys(RotatedAt));

        var request = Exchange(4);

        Assert.Equal(KeyInUse.Current, receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), RotatedAt));
    }

    /// <summary>
    /// The maximum itself is allowed. It is the boundary the refusal above is written against,
    /// and a comparison one step out turns the documented maximum into one second less than it.
    /// </summary>
    [Fact]
    public void AnOverlapOfExactlyTheMaximumIsAccepted()
    {
        var receiver = new KeyOverlap(Key());

        Assert.Equal(
            RotationOutcome.Rotated,
            receiver.Rotate(Key(), RotatedAt, RotatedAt.AddSeconds(KeyOverlap.MaximumOverlapSeconds)));
    }

    /// <summary>
    /// The overlap closes as soon as the peer proves it holds the replacement, which is
    /// earlier than the timer and is the other half of the rule the overlap carries.
    /// </summary>
    [Fact]
    public void TheOverlapClosesWhenBothSidesHaveUsedTheReplacement()
    {
        var oldKey = Key();
        var replacement = Key();
        var endsAt = RotatedAt.AddSeconds(KeyOverlap.MaximumOverlapSeconds);

        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(RotationOutcome.Rotated, receiver.Rotate(replacement, RotatedAt, endsAt));

        var caughtUp = Exchange(5);
        var at = RotatedAt.AddSeconds(30);

        Assert.Equal(KeyInUse.Current, receiver.Verify(caughtUp, RequestAuthenticator.Sign(caughtUp, replacement), at));
        Assert.False(receiver.IsRotating);
        Assert.Equal(1, receiver.LiveKeys(at));

        var late = Exchange(6);

        Assert.Equal(KeyInUse.None, receiver.Verify(late, RequestAuthenticator.Sign(late, oldKey), at));
    }

    /// <summary>
    /// Two refusals that leave the pairing exactly where it was.
    /// </summary>
    /// <param name="shape">Which replacement was proposed, named as the case.</param>
    /// <param name="expected">What proposing it produces.</param>
    [Theory]
    [InlineData("something shorter than a key", RotationOutcome.Malformed)]
    [InlineData("something longer than a key", RotationOutcome.Malformed)]
    [InlineData("nothing at all", RotationOutcome.Malformed)]
    [InlineData("the key already in use", RotationOutcome.NotAReplacement)]
    public void AReplacementThatIsNotOneIsRefused(string shape, RotationOutcome expected)
    {
        var oldKey = Key();
        var receiver = new KeyOverlap(oldKey);

        var proposed = shape switch
        {
            "something shorter than a key" => new byte[KeyOverlap.KeyLength - 1],
            "something longer than a key" => new byte[KeyOverlap.KeyLength + 1],
            "nothing at all" => Array.Empty<byte>(),
            "the key already in use" => oldKey,
            _ => throw new InvalidOperationException($"The case '{shape}' names no value."),
        };

        Assert.Equal(expected, receiver.Rotate(proposed, RotatedAt, RotatedAt.AddSeconds(600)));
        Assert.False(receiver.IsRotating);

        var request = Exchange(7);

        Assert.Equal(KeyInUse.Current, receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), RotatedAt));
    }

    /// <summary>
    /// The side that rotates signs with the replacement from the moment it accepts it.
    /// </summary>
    /// <remarks>
    /// The overlap is one-directional on purpose. It says which keys verify what arrives, and
    /// says nothing about what this side sends, because a side that went on signing with the
    /// superseded key would be unverifiable to the peer that had already caught up.
    /// </remarks>
    [Fact]
    public void TheReplacementSignsWhatThisSideSendsFromTheMomentItRotates()
    {
        var oldKey = Key();
        var replacement = Key();

        var sender = new KeyOverlap(oldKey);
        var request = Exchange(8);

        Assert.Equal(RequestAuthenticator.Sign(request, oldKey), sender.Sign(request));

        Assert.Equal(
            RotationOutcome.Rotated,
            sender.Rotate(replacement, RotatedAt, RotatedAt.AddSeconds(600)));

        Assert.Equal(RequestAuthenticator.Sign(request, replacement), sender.Sign(request));
    }

    /// <summary>
    /// Nothing verifies under a key neither side holds, whether or not an overlap is open.
    /// </summary>
    /// <param name="rotating">Whether a rotation is open when the stranger's request arrives.</param>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AKeyNeitherSideHoldsVerifiesNothing(bool rotating)
    {
        var receiver = new KeyOverlap(Key());

        if (rotating)
        {
            Assert.Equal(
                RotationOutcome.Rotated,
                receiver.Rotate(Key(), RotatedAt, RotatedAt.AddSeconds(600)));
        }

        var request = Exchange(9);

        Assert.Equal(
            KeyInUse.None,
            receiver.Verify(request, RequestAuthenticator.Sign(request, Key()), RotatedAt.AddSeconds(1)));
    }

    /// <summary>
    /// A request whose covered fields are outside their limits is refused before any key is
    /// consulted, so a rotation does not widen what reaches the MAC.
    /// </summary>
    /// <param name="malformed">Which field was made malformed, named as the case.</param>
    [Theory]
    [InlineData("the pairing identifier")]
    [InlineData("the nonce")]
    [InlineData("the version")]
    [InlineData("the path")]
    public void AMalformedRequestVerifiesUnderNeitherKey(string malformed)
    {
        var oldKey = Key();
        var receiver = new KeyOverlap(oldKey);

        Assert.Equal(RotationOutcome.Rotated, receiver.Rotate(Key(), RotatedAt, RotatedAt.AddSeconds(600)));

        var request = Exchange(10);
        var signature = RequestAuthenticator.Sign(request, oldKey);

        var broken = malformed switch
        {
            "the pairing identifier" => request.With(pairingId: "not-a-pairing-identifier"),
            "the nonce" => request.With(nonce: "00"),
            "the version" => request.With(version: "01"),
            "the path" => request.With(path: "/ServerPairing/exchange?x=1"),
            _ => throw new InvalidOperationException($"The case '{malformed}' names no field."),
        };

        Assert.Equal(KeyInUse.None, receiver.Verify(broken, signature, RotatedAt.AddSeconds(1)));
    }

    /// <summary>
    /// The overlap closes on the timer, and closing it is what the local event that takes a
    /// pairing from <c>Rotating</c> back to <c>Active</c> is driven by.
    /// </summary>
    [Fact]
    public void TheOverlapClosesOnTheTimerAndTheStateFollowsIt()
    {
        var overlap = new KeyOverlap(Key());
        var endsAt = RotatedAt.AddSeconds(600);

        Assert.Equal(RotationOutcome.Rotated, overlap.Rotate(Key(), RotatedAt, endsAt));

        Assert.False(overlap.CloseIfElapsed(endsAt.AddSeconds(-1)));
        Assert.True(overlap.IsRotating);

        Assert.True(overlap.CloseIfElapsed(endsAt));
        Assert.False(overlap.IsRotating);
        Assert.False(overlap.CloseIfElapsed(endsAt.AddSeconds(1)));

        Assert.Equal(
            new PairingTransition(PairingState.Active, TransitionOutcome.Answered),
            PairingStateMachine.Next(PairingState.Rotating, LocalEvent.RotationOverlapClosed));
    }

    /// <summary>
    /// Rotating never widens what a key reaches. What a pairing answers is its state, and the
    /// only state a rotation moves is one that already answered.
    /// </summary>
    /// <param name="from">The state the rotate request reaches.</param>
    [Theory]
    [InlineData(PairingState.Absent)]
    [InlineData(PairingState.Offered)]
    [InlineData(PairingState.Pending)]
    [InlineData(PairingState.ConfirmedHere)]
    [InlineData(PairingState.ConfirmedByPeer)]
    [InlineData(PairingState.Revoked)]
    public void RotatingDoesNotWidenWhatAKeyReaches(PairingState from)
    {
        var transition = PairingStateMachine.Next(from, PairingMessage.Rotate, OfferedKey.NotApplicable);

        Assert.Equal(from, transition.To);
        Assert.NotEqual(TransitionOutcome.Answered, transition.Outcome);
    }

    /// <summary>
    /// A pairing cannot start on something that is not a key, because the alternative is a
    /// pairing holding a key length nothing else in the tree expects.
    /// </summary>
    [Fact]
    public void APairingCannotStartOnSomethingThatIsNotAKey()
        => Assert.Throws<ArgumentException>(() => new KeyOverlap(new byte[KeyOverlap.KeyLength - 1]));

    /// <summary>
    /// Giving up when there is nothing to give up moves nothing and says so.
    /// </summary>
    [Fact]
    public void AbandoningWithNoRotationOpenMovesNothing()
    {
        var oldKey = Key();
        var receiver = new KeyOverlap(oldKey);

        Assert.False(receiver.Abandon());

        var request = Exchange(11);

        Assert.Equal(KeyInUse.Current, receiver.Verify(request, RequestAuthenticator.Sign(request, oldKey), RotatedAt));
    }

    private static byte[] Key() => RandomNumberGenerator.GetBytes(KeyOverlap.KeyLength);

    /// <summary>
    /// An exchange request, with a nonce that differs per case so that two requests in one
    /// case are two requests.
    /// </summary>
    /// <param name="ordinal">What makes this request's nonce its own.</param>
    /// <returns>The request.</returns>
    private static PairingRequest Exchange(int ordinal)
        => new PairingRequest(
            "POST",
            "/ServerPairing/exchange",
            PairingId,
            Version,
            Timestamp,
            ordinal.ToString("x32", CultureInfo.InvariantCulture),
            Encoding.UTF8.GetBytes("{\"users\":1}"));
}
