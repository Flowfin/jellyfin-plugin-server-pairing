using System;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// The harness itself, and the four things a case may do to a message crossing it.
/// </summary>
/// <remarks>
/// Every case here is about the harness rather than about the protocol. What each interception
/// point is worth is that a case can tell the interfered-with run from the ordinary one, so
/// each is written as a pair: the same send once untouched and once interfered with, compared
/// against each other rather than against a literal. A point that changed nothing would pass a
/// one-sided case and fail every pair here.
/// <para>
/// The instrument each pair reads is the receiving side's own refusal counters.
/// <see cref="RefusalCause.NotAcceptedInThisState"/> is recorded only after a signature
/// verified and <see cref="RefusalCause.DidNotVerify"/> only when one did not, and every answer
/// on this plane is the same bytes, so nothing here could read verification off the reply.
/// </para>
/// <para>
/// WHAT NONE OF THIS PROVES. No pairing here was enrolled: the key is put into both stores by
/// the harness, which is #18's work skipped rather than done. Nothing is rotated and nothing is
/// revoked, so the run from enrolment through revocation that #29's first condition asks for is
/// not among these cases.
/// </para>
/// </remarks>
public class PairedInstancesTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    /// <summary>
    /// The instant both sides start at. Nothing here reads a real clock, so the value only has
    /// to be a plausible one.
    /// </summary>
    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// A body small enough for every message's limit, whose bytes are recognisable in an
    /// assertion.
    /// </summary>
    private static byte[] Body => Encoding.ASCII.GetBytes("{\"probe\":\"harness\"}");

    /// <summary>
    /// A message signed on one side reaches the other side's endpoint and verifies there. This
    /// is the case the rest of the file is built on: until it passes, an interception point
    /// changing an outcome proves nothing, because every outcome would already be a refusal for
    /// want of a key.
    /// </summary>
    /// <remarks>
    /// It is also what pins the five header names. The controller reads them as literals and no
    /// reflection reaches them, so a name spelled differently in the harness arrives as an
    /// absent field, fails the field shape and is refused - which is this case going red.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AMessageSignedOnOneSideVerifiesOnTheOther()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        var reply = await both.Left
            .SendAsync(PairingMessage.Exchange, PairingId, Body)
            .ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Answered, reply.Outcome);
        Assert.Equal(Refusal.Status, reply.StatusCode);

        var arrived = Assert.Single(both.Right.Delivered);

        Assert.Equal(PairingMessage.Exchange, arrived.Message);
        Assert.Equal(1L, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));
        Assert.Equal(0L, both.Right.Refusals.Counted(RefusalCause.DidNotVerify));

        // Nothing crossed the other way, so the two sides are separate rather than one object
        // answering both names.
        Assert.Empty(both.Left.Delivered);
    }

    /// <summary>
    /// The two sides hold their own stores on their own paths, and the state of one is not the
    /// state of the other. A harness that shared a store would pass every case above and prove
    /// nothing about two servers.
    /// </summary>
    [Fact]
    public void EachSideHoldsItsOwnStoreAndItsOwnClock()
    {
        using var both = new PairedInstances(Start);

        Assert.NotEqual(both.Left.KeyStoreFile, both.Right.KeyStoreFile);

        both.Left.Keys.Add(PairingId, KeyMaterial.Fresh());

        Assert.Single(both.Left.Keys.Pairings());
        Assert.Empty(both.Right.Keys.Pairings());

        both.Left.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Equal(Start + TimeSpan.FromHours(1), both.Left.Clock.Now);
        Assert.Equal(Start, both.Right.Clock.Now);
    }

    /// <summary>
    /// A dropped message never reaches the far side, and the sender is told what it is told
    /// when a peer cannot be reached. The pair is the point: the same send arrives when it is
    /// not dropped.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ADroppedMessageDoesNotArriveAndTheSenderIsToldThePeerIsUnreachable()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        both.TowardsRight.DropTheNext();

        var dropped = await both.Left
            .SendAsync(PairingMessage.Exchange, PairingId, Body)
            .ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Unreachable, dropped.Outcome);
        Assert.Empty(both.Right.Delivered);
        Assert.Equal(0L, both.Right.Refusals.CountedFor(RefusalCode.Refused));

        var carried = await both.Left
            .SendAsync(PairingMessage.Exchange, PairingId, Body)
            .ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Answered, carried.Outcome);
        Assert.Single(both.Right.Delivered);
    }

    /// <summary>
    /// A delay moves the receiving side's clock rather than waiting, and the movement is
    /// visible in what the plugin does with it: an arrival that would have spent the last of an
    /// allowance is admitted where it arrives after the window it was counted in has elapsed.
    /// </summary>
    /// <remarks>
    /// The allowance is small so the case reaches the limit in three sends rather than sixty.
    /// What is asserted is the plugin's own arrival counting, not a value the harness kept: the
    /// delayed arrival is admitted and the undelayed one is refused, from the same position in
    /// the same sequence.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ADelayedMessageIsJudgedAtTheLaterInstant()
    {
        var window = TimeSpan.FromSeconds(ArrivalLimit.WindowSeconds);

        using var refused = new PairedInstances(Start, arrivalsPerPairing: 2);

        refused.PairBothSides(PairingId);

        await Spend(refused, 2).ConfigureAwait(true);

        await refused.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.Equal(1L, refused.Right.Refusals.Counted(RefusalCause.ArrivalAllowanceSpent));

        using var admitted = new PairedInstances(Start, arrivalsPerPairing: 2);

        admitted.PairBothSides(PairingId);

        await Spend(admitted, 2).ConfigureAwait(true);

        admitted.TowardsRight.DelayTheNextBy(window);

        await admitted.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.Equal(0L, admitted.Right.Refusals.Counted(RefusalCause.ArrivalAllowanceSpent));
        Assert.Equal(3L, admitted.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));

        // The instant the third message was judged at is the delayed one, so the movement
        // reached the side that serves rather than only the side that sends.
        Assert.Equal(Start + window, admitted.Right.Delivered[^1].ServedAt);
        Assert.Equal(Start, admitted.Left.Clock.Now);
    }

    /// <summary>
    /// A duplicated message arrives twice, and both copies verify. The second half is the state
    /// of this protocol today rather than something this case endorses: nothing refuses a
    /// replay, and this is the point in the harness where that refusal will be proved when it
    /// exists. THIS REMARK NAMED ISSUE #21 FOR IT AND THAT WAS THE WRONG ISSUE. The window and
    /// the nonce store that would judge a replay are landed and are #21's; what no route does
    /// is consult them on this plane, and a refusal on this plane that names the clock and is
    /// told apart from a signature failure is the fourth done condition of issue #26.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ADuplicatedMessageArrivesTwiceAndNothingRefusesTheSecondCopy()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        both.TowardsRight.DuplicateTheNext();

        var reply = await both.Left
            .SendAsync(PairingMessage.Exchange, PairingId, Body)
            .ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Answered, reply.Outcome);
        Assert.Equal(2, both.Right.Delivered.Count);

        // The same bytes both times, which is what makes it a duplicate rather than a second
        // send: a second send would carry a fresh nonce and a later timestamp.
        Assert.Equal(2L, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));
        Assert.Equal(0L, both.Right.Refusals.Counted(RefusalCause.DidNotVerify));
    }

    /// <summary>
    /// A message changed on the way does not verify, and the same message unchanged does. Both
    /// halves are asserted, because a case that only watched the corrupted one would pass on a
    /// harness whose messages never verify at all.
    /// </summary>
    /// <param name="what">What the corruption is called in the failure message.</param>
    /// <returns>The running case.</returns>
    [Theory]
    [InlineData("body")]
    [InlineData("nonce")]
    [InlineData("timestamp")]
    [InlineData("target")]
    public async Task ACorruptedMessageDoesNotVerifyAndTheSameMessageUncorruptedDoes(string what)
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        both.TowardsRight.CorruptTheNext(flight => Corrupt(what, flight));

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.Equal(0L, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));

        // A corrupted target is refused before a signature is ever computed, because a request
        // on a target this plane does not own is not a request on this plane. The other three
        // reach verification and fail it. Both are the message not being accepted, and the
        // cause is where they differ, so the case asserts the cause rather than the code.
        var expected = string.Equals(what, "target", StringComparison.Ordinal)
            ? RefusalCause.NotOnThisPlane
            : RefusalCause.DidNotVerify;

        Assert.Equal(1L, both.Right.Refusals.Counted(expected));

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.Equal(1L, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));
        Assert.Equal(1L, both.Right.Refusals.Counted(expected));
    }

    /// <summary>
    /// Each interception point applies to one message and then disarms itself, so a case can
    /// send an ordinary message straight after an interfered-with one without building a second
    /// harness. Every pair above rests on this.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task AnArmedInterceptionAppliesToOneMessageOnly()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        Assert.False(both.TowardsRight.Armed);

        both.TowardsRight.DropTheNext();

        Assert.True(both.TowardsRight.Armed);

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.False(both.TowardsRight.Armed);
    }

    /// <summary>
    /// The two directions are interfered with separately, so a case can drop one side's
    /// messages while the other's still arrive.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheTwoDirectionsAreInterferedWithSeparately()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        both.TowardsRight.DropTheNext();

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);
        await both.Right.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);

        Assert.Empty(both.Right.Delivered);
        Assert.Single(both.Left.Delivered);
    }

    /// <summary>
    /// A body over the limit for its message is refused by the reading at the edge rather than
    /// by anything the harness does, which is what says the controller's bounded read is on the
    /// path a harnessed message takes.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ABodyOverTheLimitIsRefusedByTheEdge()
    {
        using var both = new PairedInstances(Start);

        both.PairBothSides(PairingId);

        await both.Left
            .SendAsync(PairingMessage.Hello, PairingId, new byte[PeerPlane.BodyLimit + 1])
            .ConfigureAwait(true);

        Assert.Equal(1L, both.Right.Refusals.Counted(RefusalCause.BodyOverItsLimit));
        Assert.Equal(0L, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));
    }

    /// <summary>
    /// Changes one part of a message on the way.
    /// </summary>
    /// <param name="what">Which part.</param>
    /// <param name="flight">The message.</param>
    /// <returns>The message as it then arrives.</returns>
    private static InFlight Corrupt(string what, InFlight flight)
    {
        switch (what)
        {
            case "body":
                var bytes = flight.Body.ToArray();
                bytes[0] ^= 0x01;
                return flight.With(body: bytes);

            case "nonce":
                return flight.WithHeader(
                    PairedInstance.NonceHeader,
                    new string('a', FieldShape.HexFieldLength));

            case "timestamp":
                return flight.WithHeader(
                    PairedInstance.TimestampHeader,
                    (Start.ToUnixTimeSeconds() + 1).ToString(CultureInfo.InvariantCulture));

            case "target":
                return flight.With(path: flight.Path + "/");

            default:
                throw new ArgumentOutOfRangeException(nameof(what), what, "Nothing here corrupts that.");
        }
    }

    /// <summary>
    /// Sends until an allowance is used up, asserting on the way that every one of them was
    /// admitted, so a case that follows cannot rest on a sequence that was already being
    /// refused.
    /// </summary>
    /// <param name="both">The harness.</param>
    /// <param name="count">How many to send.</param>
    /// <returns>The running work.</returns>
    private static async Task Spend(PairedInstances both, int count)
    {
        for (var sent = 0; sent < count; sent++)
        {
            await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body).ConfigureAwait(true);
        }

        Assert.Equal((long)count, both.Right.Refusals.Counted(RefusalCause.NotAcceptedInThisState));
        Assert.Equal(0L, both.Right.Refusals.Counted(RefusalCause.ArrivalAllowanceSpent));
    }
}
