using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// What the pairing plane refuses, counted per cause and reported per code, and the payload the
/// administrative plane answers with.
/// </summary>
/// <remarks>
/// Two obligations are asserted here and they differ in kind. That the payload covers the whole
/// refusal taxonomy is a property of the shape, and is issue #51's third condition. That each
/// cause is really produced by the site it names is a property of behaviour, and every case
/// below that asserts one drives a request through <see cref="PeerPlane"/> rather than calling
/// <see cref="RefusalCounters.Record"/>. A counter that only its own case increments proves
/// nothing about a server.
/// <para>
/// WHAT IS NOT ASSERTED HERE is that the payload holds no secret a lifecycle created, which is
/// issue #51's second condition. There is no enrolment, no rotation and no revocation to drive,
/// so a case asserting that absence would be asserting it over a payload no secret has ever
/// been near, would pass, and would go on passing after the first one was. What stands in its
/// place is narrower and is written as such: every member of the payload is a number, and
/// <see cref="EveryMemberOfThePayloadIsANumber"/> is what refuses a member that is not.
/// </para>
/// </remarks>
public class RefusalCountersTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Version = "1";
    private const string Timestamp = "1786000000";
    private const string NotASignature = "not-the-signature";

    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private static byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Every cause, so a case walks them rather than naming them.
    /// </summary>
    /// <returns>The causes.</returns>
    public static TheoryData<RefusalCause> EveryCause()
    {
        var data = new TheoryData<RefusalCause>();

        foreach (var cause in RefusalCounters.Causes())
        {
            data.Add(cause);
        }

        return data;
    }

    /// <summary>
    /// Issue #51's third condition: each refusal reason in the taxonomy appears as its own
    /// counter. The taxonomy is <c>docs/protocol.md</c>'s and its expression in code is
    /// <see cref="RefusalCode"/>, so the payload is compared against every member of that
    /// enumeration under the spelling the wire uses. A code the payload leaves out fails, and
    /// so does a key the taxonomy does not have.
    /// </summary>
    [Fact]
    public void EveryCodeInTheTaxonomyHasItsOwnCounter()
    {
        var answered = DiagnosticsAnswer.Of(new RefusalCounters(), new ArrivalLimit()).RefusalsByCode;

        Assert.Equal(
            Enum.GetValues<RefusalCode>().Select(Refusal.Wire).OrderBy(name => name, StringComparer.Ordinal),
            answered.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// The same in the other direction, over the causes. This is the half that is not in the
    /// issue's wording and is what the decision recorded on <see cref="RefusalCause"/> adds:
    /// numbers an operator can act on beside the one bucket the wire collapses them into.
    /// </summary>
    [Fact]
    public void EveryCauseHasItsOwnCounter()
    {
        var answered = DiagnosticsAnswer.Of(new RefusalCounters(), new ArrivalLimit()).RefusalsByCause;

        Assert.Equal(
            RefusalCounters.Causes().Select(RefusalCounters.Name).OrderBy(name => name, StringComparer.Ordinal),
            answered.Keys.OrderBy(name => name, StringComparer.Ordinal));
    }

    /// <summary>
    /// A code's number is the sum of the causes that map to it. That is the property which lets
    /// a cause be split later without an operator reading a different measurement under the
    /// same name, and each cause is counted a different number of times so a sum cannot be
    /// right by accident.
    /// </summary>
    [Fact]
    public void ACodesNumberIsTheSumOfTheCausesBehindIt()
    {
        var counters = new RefusalCounters();
        var expected = new Dictionary<RefusalCode, long>();
        var times = 1L;

        foreach (var cause in RefusalCounters.Causes())
        {
            for (var i = 0; i < times; i++)
            {
                counters.Record(cause);
            }

            var code = RefusalCounters.CodeFor(cause);

            expected[code] = (expected.TryGetValue(code, out var held) ? held : 0L) + times;
            times++;
        }

        foreach (var code in RefusalCounters.Codes())
        {
            Assert.Equal(expected.TryGetValue(code, out var total) ? total : 0L, counters.CountedFor(code));
        }
    }

    /// <summary>
    /// A cause outside the enumeration is refused rather than counted into a slot no reader can
    /// place. The array behind the counters is as long as the enumeration, so an unchecked
    /// write would either land in another cause's number or leave the process.
    /// </summary>
    [Fact]
    public void ACauseOutsideTheEnumerationIsRefused()
    {
        var counters = new RefusalCounters();

        Assert.Throws<ArgumentOutOfRangeException>(() => counters.Record((RefusalCause)(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = counters.Counted((RefusalCause)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = RefusalCounters.Name((RefusalCause)99));
    }

    /// <summary>
    /// Each cause is produced by the site it names, driven through the plane. The counters are
    /// read once after whatever setup the cause needs and once after the single request the
    /// case is about, so what is asserted is the movement that request caused: exactly one
    /// cause, by exactly one. A site counting the wrong cause fails here rather than being read
    /// out of the source.
    /// </summary>
    /// <param name="cause">The cause the request is built to produce.</param>
    [Theory]
    [MemberData(nameof(EveryCause))]
    public void EachCauseIsCountedByTheSiteThatRefusesIt(RefusalCause cause)
    {
        var counters = new RefusalCounters();
        var plane = PlaneFor(counters, cause);

        Setup(plane, cause);

        var before = RefusalCounters.Causes().ToDictionary(each => each, counters.Counted);

        Final(plane, cause);

        foreach (var each in RefusalCounters.Causes())
        {
            Assert.Equal(before[each] + (each == cause ? 1 : 0), counters.Counted(each));
        }
    }

    /// <summary>
    /// What a caller is told is the code the cause maps to, at every site, driven through the
    /// plane rather than read off the mapping.
    /// </summary>
    /// <param name="cause">The cause the request is built to produce.</param>
    /// <remarks>
    /// THIS CASE ASSERTED THAT EVERY CAUSE IS ANSWERED WITH THE SAME CODE. Three are not: the
    /// plane judges freshness after verification, and the taxonomy in <c>docs/protocol.md</c>
    /// allows a distinguishable code to a caller that has proved it holds the key. The property
    /// that mattered is not weakened by the narrowing, it is moved to
    /// <see cref="EveryRefusalACallerWithoutAKeyCanReachIsTheSameBytes"/>, which asserts it over
    /// exactly the callers it was ever about.
    /// <para>
    /// Asserting against <see cref="RefusalCounters.CodeFor(RefusalCause)"/> rather than against
    /// a literal is what makes this a case about the site: the mapping is one method, and a site
    /// that answered a code while counting a cause carrying another would fail here.
    /// </para>
    /// <para>
    /// The body is handed on for one of them, and that is the transition table refusing a caller
    /// it authenticated rather than anything this file adds. A freshness refusal hands nothing
    /// on, though its body verified, which the assertion says rather than leaving out.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryCause))]
    public void TheAnswerIsTheCodeTheCauseMapsTo(RefusalCause cause)
    {
        var plane = PlaneFor(new RefusalCounters(), cause);

        Setup(plane, cause);

        var outcome = Final(plane, cause);

        Assert.Equal(RefusalCounters.CodeFor(cause), outcome.Code);
        Assert.Equal(cause == RefusalCause.NotAcceptedInThisState, outcome.BodyWasHandedOn);
    }

    /// <summary>
    /// Every refusal a caller holding no verifying key can reach is the same bytes. That is the
    /// oracle property, stated over the callers it is about rather than over every cause.
    /// </summary>
    /// <remarks>
    /// The five below are the causes reachable before verification has succeeded, so they are
    /// what a stranger walking this server can produce. A caller that reaches any of them learns
    /// which of the five it met only if the bytes differ, and they do not.
    /// <para>
    /// The list is written out rather than derived from the code each cause maps to. Deriving it
    /// would ask the mapping whether the mapping is right, and this case exists to hold the
    /// mapping to something: a change putting a distinguishable code on a cause a stranger can
    /// reach fails here rather than passing because it moved both sides at once.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryRefusalACallerWithoutAKeyCanReachIsTheSameBytes()
    {
        RefusalCause[] withoutAKey =
        [
            RefusalCause.NotOnThisPlane,
            RefusalCause.BodyOverItsLimit,
            RefusalCause.ArrivalAllowanceSpent,
            RefusalCause.NoRoomToCountTheArrival,
            RefusalCause.DidNotVerify,
        ];

        var answers = new List<string>();

        foreach (var cause in withoutAKey)
        {
            var plane = PlaneFor(new RefusalCounters(), cause);

            Setup(plane, cause);

            var outcome = Final(plane, cause);

            Assert.False(outcome.BodyWasHandedOn);

            answers.Add(Refusal.Body(outcome.Code));
        }

        Assert.Equal(withoutAKey.Length, answers.Count);
        Assert.Single(answers.Distinct());
        Assert.Equal("{\"code\":\"refused\"}", answers[0]);
    }

    /// <summary>
    /// The plane counts into the object it was handed, which is what makes the number the
    /// diagnostics action renders the same number the plane wrote. A plane built without one
    /// counts into its own, so nothing here can be satisfied by a counter nobody wired up.
    /// </summary>
    [Fact]
    public void ThePlaneCountsIntoTheCounterItWasHanded()
    {
        var counters = new RefusalCounters();
        var plane = PlaneFor(counters, RefusalCause.NotOnThisPlane);

        Final(plane, RefusalCause.NotOnThisPlane);

        Assert.Same(counters, plane.Refusals);
        Assert.Equal(1, counters.Counted(RefusalCause.NotOnThisPlane));
        Assert.Equal(0, new PeerPlane(new RequestAuthenticator(new KnownKeys()), new ArrivalLimit(), new FreshnessWindow())
            .Refusals.Counted(RefusalCause.NotOnThisPlane));
    }

    /// <summary>
    /// What the payload reports is what the plane counted, read through the answer rather than
    /// through the counters, so both maps and the derivation between them are exercised the way
    /// an administrator meets them.
    /// </summary>
    /// <remarks>
    /// The identifier count is the assertion worth reading twice. Three of the four requests are
    /// refused before the arrival limit is consulted, so they leave it holding nothing, and the
    /// fourth is the only one that reaches it. That is the ordering <see cref="PeerPlane.Serve"/>
    /// fixes, read from the outside.
    /// </remarks>
    [Fact]
    public void ThePayloadReportsWhatThePlaneRefused()
    {
        var counters = new RefusalCounters();
        var arrivals = new ArrivalLimit();
        var plane = new PeerPlane(new RequestAuthenticator(new KnownKeys()), arrivals, new FreshnessWindow(), counters);

        plane.Serve(PairingMessage.Hello, Arriving(target: "/ServerPairing/elsewhere"), At);
        plane.Serve(PairingMessage.Hello, Arriving(target: "/ServerPairing/elsewhere"), At);
        plane.Serve(PairingMessage.Hello, Arriving(exceeded: true), At);

        Assert.Equal(0, DiagnosticsAnswer.Of(counters, arrivals).IdentifiersBeingCounted);

        plane.Serve(PairingMessage.Hello, Arriving(), At);

        var answer = DiagnosticsAnswer.Of(counters, arrivals);

        Assert.Equal(2L, answer.RefusalsByCause[RefusalCounters.Name(RefusalCause.NotOnThisPlane)]);
        Assert.Equal(1L, answer.RefusalsByCause[RefusalCounters.Name(RefusalCause.BodyOverItsLimit)]);
        Assert.Equal(1L, answer.RefusalsByCause[RefusalCounters.Name(RefusalCause.DidNotVerify)]);
        Assert.Equal(4L, answer.RefusalsByCode[Refusal.Wire(RefusalCode.Refused)]);
        Assert.Equal(0L, answer.RefusalsByCode[Refusal.Wire(RefusalCode.Clock)]);
        Assert.Equal(1, answer.IdentifiersBeingCounted);
    }

    /// <summary>
    /// Every member of the payload is a number or a map of numbers. This is the narrower thing
    /// that stands in for issue #51's second condition while there is no lifecycle to drive: it
    /// cannot say the payload holds no secret, and it does say that a member able to carry one
    /// would have to be added to the type first, in a diff somebody reads.
    /// </summary>
    [Fact]
    public void EveryMemberOfThePayloadIsANumber()
    {
        var answered = JsonSerializer.Serialize(DiagnosticsAnswer.Of(new RefusalCounters(), new ArrivalLimit()));

        using var document = JsonDocument.Parse(answered);

        Assert.NotEmpty(document.RootElement.EnumerateObject());

        foreach (var member in document.RootElement.EnumerateObject())
        {
            if (member.Value.ValueKind == JsonValueKind.Number)
            {
                continue;
            }

            Assert.Equal(JsonValueKind.Object, member.Value.ValueKind);
            Assert.NotEmpty(member.Value.EnumerateObject());

            foreach (var counted in member.Value.EnumerateObject())
            {
                Assert.Equal(JsonValueKind.Number, counted.Value.ValueKind);
            }
        }
    }

    /// <summary>
    /// A plane sized for the cause being driven.
    /// </summary>
    /// <param name="counters">Where a refusal is counted.</param>
    /// <param name="cause">The cause the plane is being built for.</param>
    /// <returns>The plane.</returns>
    private static PeerPlane PlaneFor(RefusalCounters counters, RefusalCause cause)
    {
        // An allowance of one is what makes the second arrival the refusal, rather than sending
        // a full allowance and counting every admission on the way to it.
        //
        // Filling the nonce store needs the opposite. The store holds more nonces for one
        // pairing than the default allowance admits requests, so the arrival limit would refuse
        // long before the store filled and the case would assert the wrong cause. The allowance
        // is widened for that one cause rather than the store being made smaller, because the
        // store's bound is the constant the specification names.
        var arrivals = cause switch
        {
            RefusalCause.ArrivalAllowanceSpent => new ArrivalLimit(ArrivalLimit.WindowSeconds, 1, 1),
            RefusalCause.NoRoomToRememberTheNonce => new ArrivalLimit(
                ArrivalLimit.WindowSeconds,
                ArrivalLimit.MaximumArrivals,
                ArrivalLimit.ArrivalsPerEnrolment),
            _ => new ArrivalLimit(),
        };

        return new PeerPlane(new RequestAuthenticator(new KnownKeys()), arrivals, new FreshnessWindow(), counters);
    }

    /// <summary>
    /// Whatever has to have happened before the request the case is about.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="cause">The cause to produce.</param>
    private static void Setup(PeerPlane plane, RefusalCause cause)
    {
        switch (cause)
        {
            case RefusalCause.ArrivalAllowanceSpent:
                plane.Serve(PairingMessage.Hello, Arriving(), At);
                break;

            case RefusalCause.NoRoomToCountTheArrival:
                for (var i = 0; i < ArrivalLimit.PairingsCounted; i++)
                {
                    plane.Serve(PairingMessage.Hello, Arriving(pairingId: Fresh(i)), At);
                }

                break;

            case RefusalCause.NonceAlreadySeen:
                // Seen once, so the request the case is about is the second copy of it. Signed,
                // because a nonce is only ever remembered for a request that verified.
                plane.Serve(PairingMessage.Hello, Arriving(sign: true), At);
                break;

            case RefusalCause.NoRoomToRememberTheNonce:
                // One distinct nonce per request, all of them verifying, until the store holds
                // as many as it will for one pairing.
                for (var i = 0; i < FreshnessWindow.NoncesPerPairing; i++)
                {
                    plane.Serve(PairingMessage.Hello, Arriving(sign: true, carries: FreshNonce()), Filling(i));
                }

                break;

            default:
                break;
        }
    }

    /// <summary>
    /// The one request that reaches the site the cause names.
    /// </summary>
    /// <param name="plane">The plane.</param>
    /// <param name="cause">The cause to produce.</param>
    /// <returns>What the plane answered it with.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not one of the defined values.</exception>
    private static PeerPlaneOutcome Final(PeerPlane plane, RefusalCause cause) => cause switch
    {
        RefusalCause.NotOnThisPlane =>
            plane.Serve(PairingMessage.Hello, Arriving(target: "/ServerPairing/elsewhere"), At),
        RefusalCause.BodyOverItsLimit =>
            plane.Serve(PairingMessage.Hello, Arriving(exceeded: true), At),
        RefusalCause.ArrivalAllowanceSpent =>
            plane.Serve(PairingMessage.Hello, Arriving(), At),
        RefusalCause.NoRoomToCountTheArrival =>
            plane.Serve(PairingMessage.Hello, Arriving(pairingId: Fresh(-1)), At),
        RefusalCause.DidNotVerify =>
            plane.Serve(PairingMessage.Hello, Arriving(), At),
        RefusalCause.NotAcceptedInThisState =>
            plane.Serve(PairingMessage.Hello, Arriving(sign: true), At),
        RefusalCause.TimestampOutsideTheWindow =>
            plane.Serve(
                PairingMessage.Hello,
                Arriving(sign: true, stamp: Skewed(FreshnessWindow.WindowSeconds + 1)),
                At),
        RefusalCause.NonceAlreadySeen =>
            plane.Serve(PairingMessage.Hello, Arriving(sign: true), At),
        RefusalCause.NoRoomToRememberTheNonce =>
            plane.Serve(
                PairingMessage.Hello,
                Arriving(sign: true, carries: FreshNonce()),
                Filling(FreshnessWindow.NoncesPerPairing)),
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    /// <summary>
    /// The instant the request at this position in the fill is served at.
    /// </summary>
    /// <param name="which">Which request of the fill.</param>
    /// <returns>The instant.</returns>
    /// <remarks>
    /// The store holds more nonces for one pairing than the widest arrival allowance this server
    /// accepts admits requests in one window, so a fill cannot happen at a single instant: the
    /// allowance is spent and comes back a window later. The clock moves rather than the
    /// allowance being raised past its own maximum, because a case that had to exceed a bound
    /// the plugin refuses would be proving something no server can be in.
    /// <para>
    /// The whole fill spans one arrival window, which is well inside how long a nonce is
    /// remembered, so nothing put in the store at the start has aged out of it by the end. It is
    /// also well inside the tolerated skew, so every request carries the same timestamp and none
    /// of them is refused for the clock instead.
    /// </para>
    /// </remarks>
    private static DateTimeOffset Filling(int which) =>
        At.AddSeconds((which / ArrivalLimit.MaximumArrivals) * ArrivalLimit.WindowSeconds);

    /// <summary>
    /// A timestamp this many seconds later than the instant every case judges at.
    /// </summary>
    /// <param name="seconds">How far ahead.</param>
    /// <returns>The timestamp, as it is spelled on the wire.</returns>
    private static string Skewed(int seconds) =>
        (At.ToUnixTimeSeconds() + seconds).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// An identifier of the right shape that no other case uses.
    /// </summary>
    /// <param name="which">Which one.</param>
    /// <returns>The identifier.</returns>
    private static string Fresh(int which) =>
        (which < 0 ? "f" : "a")
        + Math.Abs(which).ToString(CultureInfo.InvariantCulture).PadLeft(31, '0');

    /// <summary>
    /// A request as it arrives, correct in every way the arguments do not change. The signature
    /// is one that does not verify unless the caller asks for one that does, so a case about
    /// anything else reaches its own site and is refused there.
    /// </summary>
    /// <param name="target">The path it arrived on.</param>
    /// <param name="pairingId">The identifier it claims.</param>
    /// <param name="sign">Whether to sign it with the key the plane holds.</param>
    /// <param name="exceeded">Whether the body was over its limit.</param>
    /// <returns>The request.</returns>
    private static ArrivingRequest Arriving(
        string? target = null,
        string pairingId = PairingId,
        bool sign = false,
        bool exceeded = false,
        string? carries = null,
        string stamp = Timestamp)
    {
        var path = PeerPlane.PathFor(PairingMessage.Hello);
        var body = Array.Empty<byte>();
        var carried = carries ?? Nonce;

        var presented = sign
            ? RequestAuthenticator.Sign(
                new PairingRequest(PeerPlane.Method, path, pairingId, Version, stamp, carried, body),
                Key)
            : NotASignature;

        return new ArrivingRequest(
            target ?? path,
            PeerPlane.Method,
            pairingId,
            Version,
            stamp,
            carried,
            presented,
            body,
            exceeded);
    }

    /// <summary>
    /// A nonce no other request in a case carries, of the shape the specification fixes.
    /// </summary>
    /// <returns>The nonce.</returns>
    private static string FreshNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(FieldShape.HexFieldLength / 2)).ToLowerInvariant();

    /// <summary>
    /// A key source holding the one key this file signs with.
    /// </summary>
    private sealed class KnownKeys : IPairingKeySource
    {
        public AcceptedKeys ArrivingKeys(string pairingId, DateTimeOffset at) =>
            string.Equals(pairingId, PairingId, StringComparison.Ordinal)
                ? new AcceptedKeys(Key, default)
                : AcceptedKeys.None;
    }
}
