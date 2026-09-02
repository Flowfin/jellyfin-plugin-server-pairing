using System;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The five peer paths, and what happens to a request that arrives on one.
/// </summary>
/// <remarks>
/// This type holds the plane and the controller holds the host. Everything decidable about an
/// arriving request is decided here, over values rather than over an <c>HttpContext</c>, so
/// the suite judges the rules without a server and the controller carries only the reading of
/// the request and the writing of the answer.
/// <para>
/// The paths, the limits and the refusal shape are all <c>docs/protocol.md</c>. A difference
/// between that document and this file is a defect in this file.
/// </para>
/// </remarks>
public sealed class PeerPlane
{
    /// <summary>
    /// The prefix every peer path carries.
    /// </summary>
    public const string Prefix = "/ServerPairing";

    /// <summary>
    /// The method every message on this plane arrives as.
    /// </summary>
    public const string Method = "POST";

    /// <summary>
    /// The most bytes an <c>exchange</c> body may carry, which is 1 MiB.
    /// </summary>
    public const int ExchangeBodyLimit = 1024 * 1024;

    /// <summary>
    /// The most bytes the body of any other message may carry, which is 8 KiB.
    /// </summary>
    public const int BodyLimit = 8 * 1024;

    private readonly RequestAuthenticator _authenticator;

    private readonly ArrivalLimit _arrivals;

    private readonly FreshnessWindow _freshness;

    private readonly RefusalCounters _refusals;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerPlane"/> class.
    /// </summary>
    /// <param name="authenticator">What decides whether an arriving request is authentic.</param>
    /// <param name="arrivals">How much of this plane one claimed identifier may use.</param>
    /// <param name="freshness">
    /// The timestamp window and the nonce store a verified request is judged against. One per
    /// server rather than one per caller: what it holds is the nonces already seen, and a
    /// second instance would remember none of them, which is a replay window opened by
    /// construction.
    /// </param>
    /// <param name="refusals">
    /// Where a refusal is counted for this server's own administrator. A plane built without one
    /// gets a counter of its own that nothing reads, so counting can never change what a caller
    /// is told; the composition root hands in the one the diagnostics action renders.
    /// </param>
    public PeerPlane(
        RequestAuthenticator authenticator,
        ArrivalLimit arrivals,
        FreshnessWindow freshness,
        RefusalCounters? refusals = null)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        _arrivals = arrivals ?? throw new ArgumentNullException(nameof(arrivals));
        _freshness = freshness ?? throw new ArgumentNullException(nameof(freshness));
        _refusals = refusals ?? new RefusalCounters();
    }

    /// <summary>
    /// Gets what this plane has refused and why, since this instance was made.
    /// </summary>
    /// <remarks>
    /// A read of what is counted rather than a way to count: nothing outside this type records
    /// into it. What it is for is issue #51, and the numbers reach an administrator through the
    /// diagnostics action on the other plane and reach no caller here.
    /// </remarks>
    public RefusalCounters Refusals => _refusals;

    /// <summary>
    /// The exact path a message arrives on.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The path, with no trailing slash and nothing percent-encoded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The message is not one of the defined values.</exception>
    public static string PathFor(PairingMessage message) => message switch
    {
        PairingMessage.Hello => Prefix + "/hello",
        PairingMessage.Confirm => Prefix + "/confirm",
        PairingMessage.Rotate => Prefix + "/rotate",
        PairingMessage.Revoke => Prefix + "/revoke",
        PairingMessage.Exchange => Prefix + "/exchange",
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>
    /// The most bytes the body of a message may carry.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The limit in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The message is not one of the defined values.</exception>
    public static int BodyLimitFor(PairingMessage message) => message switch
    {
        PairingMessage.Exchange => ExchangeBodyLimit,
        PairingMessage.Hello or PairingMessage.Confirm or PairingMessage.Rotate or PairingMessage.Revoke => BodyLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>
    /// Serves one arriving request.
    /// </summary>
    /// <param name="message">The message the path this arrived on belongs to.</param>
    /// <param name="arrived">The request as it arrived.</param>
    /// <param name="at">This server's clock, which the arrival limit is judged against.</param>
    /// <returns>What the caller is told, and whether the body was handed past verification.</returns>
    /// <remarks>
    /// The order of the checks is the security property rather than a style. The path is
    /// compared before anything else because a request on the wrong path is not a request on
    /// this plane at all. A body over its limit is refused before a signature is computed, so
    /// a stranger cannot make this server do cryptographic work by sending a large body. The
    /// arrival limit sits in the same place and for the same reason: what it bounds is the work
    /// a caller can ask for, so it has to answer before the work is done rather than after.
    /// Verification runs before the body is handed on, so nothing richer than bytes exists for
    /// an unauthenticated caller to reach.
    /// <para>
    /// An arrival the limit refuses is answered with the undistinguished refusal, like every
    /// other cause. A caller learns from it exactly what it learns from any other refusal,
    /// which is nothing, and a stranger cannot use it to find out whether an identifier it
    /// guessed is one this server holds.
    /// </para>
    /// <para>
    /// EVERY REFUSAL IS COUNTED AND NOTHING ABOUT THE ANSWER MOVES FOR IT. The cause goes into
    /// <see cref="Refusals"/>, which no caller reaches: it is read behind the host's elevation
    /// policy by the diagnostics action on the administrative plane, which is issue #51.
    /// <see cref="RefusalCause"/> carries why a cause and a code are separate things. The count
    /// is taken at the site that refuses rather than from the code that comes back, because
    /// every site answers the same code and a count taken from the answer would be one number.
    /// </para>
    /// <para>
    /// FRESHNESS IS JUDGED AFTER VERIFICATION AND THAT POSITION IS THE ORACLE ARGUMENT RATHER
    /// THAN AN ORDERING CONVENIENCE. <c>docs/threat-model.md</c> keeps one distinction on this
    /// plane deliberately: a refusal caused by clock skew says clock rather than reading as a
    /// signature failure, which costs a caller one bit that the specification already gives
    /// them and saves an operator an evening on two home servers whose clocks disagree. It is
    /// affordable only because a caller that reaches it has already proved it holds the
    /// pairing's key, so judging freshness before verifying would hand that bit to a stranger
    /// and is the mistake this ordering exists against. The same holds for
    /// <see cref="RefusalCode.Replay"/> and <see cref="RefusalCode.Busy"/>.
    /// </para>
    /// <para>
    /// A request refused for freshness hands nothing on, even though its body verified.
    /// Verification says the bytes are authentic and freshness says they are not this request,
    /// and acting on a replayed body is exactly what the nonce store exists to stop.
    /// </para>
    /// <para>
    /// THIS PARAGRAPH SAID EVERY ANSWER TODAY IS <see cref="RefusalCode.Refused"/>. A verified
    /// request that is stale, replayed or arriving with no room left to remember its nonce is
    /// answered with its own code. What is unchanged is the answer to everything before
    /// verification, and the answer to a request that is verified and fresh: every pairing is
    /// <see cref="PairingState.Absent"/> here, and the <c>Absent</c> row of that table is the
    /// undistinguished refusal for all five messages.
    /// <c>PeerPlaneTests.TheAbsentRowRefusesEveryMessage</c> is the assertion that ties this
    /// answer to the table instead of to this sentence.
    /// <para>
    /// THAT SENTENCE SAID NO RECORD STORE EXISTS, AND ONE DOES.
    /// <see cref="Protocol.FilePairingRecordStore"/> ships and is registered in
    /// <see cref="PluginServiceRegistrator"/>. What this plane holds is the reason instead:
    /// nothing in this directory takes an <see cref="Protocol.IPairingRecordStore"/> or a
    /// <see cref="Protocol.PairingStateMachine"/>, so no request that arrives here can be on a
    /// pairing in any state but <c>Absent</c>. The answer is unmoved by the correction and the
    /// repair is not: a reader of the old sentence would look for a store to build, and what is
    /// missing is the join between this plane and the one that exists.
    /// </para>
    /// <para>
    /// THAT IS NOW TRUE OF THE ANSWER AND NOT OF THE VERIFICATION, which is a distinction a
    /// reader of this paragraph could previously not make. The key store is read on this path,
    /// so a request signed under a pairing's key reaches the second field of
    /// <see cref="PeerPlaneOutcome"/> as verified and is still refused by the row above. What
    /// no route puts a key into that store is the enrolment, which is issue #18, so on a server
    /// today nothing verifies for want of a key rather than for want of a lookup.
    /// </para>
    /// </para>
    /// </remarks>
    public PeerPlaneOutcome Serve(PairingMessage message, ArrivingRequest arrived, DateTimeOffset at)
    {
        var path = PathFor(message);

        if (arrived is null
            || !string.Equals(arrived.RawTarget, path, StringComparison.Ordinal)
            || !string.Equals(arrived.Method, Method, StringComparison.Ordinal))
        {
            return Refuse(RefusalCause.NotOnThisPlane);
        }

        // Split out of the comparison above rather than answered with it. The two are one
        // answer to the caller and two different things to an operator: a request on the wrong
        // path is somebody who is not talking to this plane at all, and an oversized body is a
        // peer that is, sending more than the specification allows. Nothing about what is
        // refused moves with this, and the order does not either: a body over its limit is
        // still refused before a signature is computed.
        if (arrived.BodyExceededItsLimit)
        {
            return Refuse(RefusalCause.BodyOverItsLimit);
        }

        var admitted = _arrivals.Admit(arrived.PairingId, at);

        if (admitted != ArrivalOutcome.Admitted)
        {
            return Refuse(admitted == ArrivalOutcome.NoRoomToCount
                ? RefusalCause.NoRoomToCountTheArrival
                : RefusalCause.ArrivalAllowanceSpent);
        }

        // The method and the path are the constants rather than what arrived, and the two are
        // the same bytes: the comparison above refused everything else. Passing the constants
        // is what makes that true to the compiler as well as to a reader.
        var request = new PairingRequest(
            Method,
            path,
            arrived.PairingId ?? string.Empty,
            arrived.Version ?? string.Empty,
            arrived.Timestamp ?? string.Empty,
            arrived.Nonce ?? string.Empty,
            arrived.Body);

        var outcome = _authenticator.VerifyThenRead(request, arrived.Signature, at, body => body, out var verified);

        if (outcome != VerificationOutcome.Verified)
        {
            return Refuse(RefusalCause.DidNotVerify);
        }

        // Only now, and never earlier. Everything below this line answers a caller that has
        // proved it holds the pairing's key, which is what lets these three refusals be told
        // apart from one another at all.
        var freshness = _freshness.Judge(request.PairingId, request.Nonce, request.Timestamp, at);

        if (freshness != FreshnessOutcome.Fresh)
        {
            return Refuse(CauseOf(freshness));
        }

        _refusals.Record(RefusalCause.NotAcceptedInThisState);

        return new PeerPlaneOutcome(RefusalCode.Refused, true, verified);
    }

    /// <summary>
    /// Counts a refusal and answers it.
    /// </summary>
    /// <param name="cause">Why the request is refused.</param>
    /// <returns>The refusal the cause carries, with no body handed on.</returns>
    /// <remarks>
    /// One place builds the answer so that counting cannot drift from refusing. THIS REMARK
    /// SAID THE CODE IS THE SAME CONSTANT WHATEVER IS PASSED IN. It is derived from the cause
    /// now, through <see cref="RefusalCounters.CodeFor(RefusalCause)"/>, which is the same
    /// method the diagnostics payload sums by. That is a stronger version of the property the
    /// sentence it replaced was about rather than a weaker one: a site cannot answer one code
    /// while counting a cause that maps to another, because it does not choose a code at all.
    /// <para>
    /// Which causes still collapse into <see cref="RefusalCode.Refused"/> is that method's to
    /// say, and all of the ones reached before verification do.
    /// </para>
    /// </remarks>
    private PeerPlaneOutcome Refuse(RefusalCause cause)
    {
        _refusals.Record(cause);

        return new PeerPlaneOutcome(RefusalCounters.CodeFor(cause), false, ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>
    /// The cause this server counts for a freshness judgement that is not fresh.
    /// </summary>
    /// <param name="freshness">What judging the request's freshness produced.</param>
    /// <returns>The cause.</returns>
    /// <remarks>
    /// <see cref="FreshnessOutcome.Malformed"/> is not reachable from this plane and is mapped
    /// rather than thrown on. <see cref="FieldShape.IsWellFormed(PairingRequest)"/> runs inside
    /// verification, before any key is fetched, and refuses exactly the two field shapes this
    /// outcome is about, so a request reaching the judgement has already passed them. It is
    /// answered as the undistinguished refusal because the alternative is an exception on a
    /// request path, and it is counted as <see cref="RefusalCause.DidNotVerify"/> rather than
    /// under a cause of its own because a cause no site can reach is a number an operator reads
    /// as a measurement and is not one.
    /// </remarks>
    private static RefusalCause CauseOf(FreshnessOutcome freshness) => freshness switch
    {
        FreshnessOutcome.OutsideTheWindow => RefusalCause.TimestampOutsideTheWindow,
        FreshnessOutcome.AlreadySeen => RefusalCause.NonceAlreadySeen,
        FreshnessOutcome.NoRoomToRemember => RefusalCause.NoRoomToRememberTheNonce,
        _ => RefusalCause.DidNotVerify,
    };
}
