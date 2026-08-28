using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The five peer paths, and what a request arriving on one is told.
/// </summary>
/// <remarks>
/// Every path, every limit and every refusal asserted here is read out of
/// <c>docs/protocol.md</c> rather than out of the implementation. A case whose expectation can
/// only be justified by reading the code is a case testing the code against itself.
/// </remarks>
public class PeerPlaneTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Version = "1";
    private const string Timestamp = "1786000000";

    /// <summary>
    /// The instant every case hands the plane. Time only matters here through the arrival
    /// limit, and no case in this file sends enough to reach one, so one instant serves them
    /// all; <c>ArrivalLimitTests</c> is where time is moved.
    /// </summary>
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private static byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Every message this plane carries, so a case walks the five rather than naming one.
    /// </summary>
    /// <returns>The five messages.</returns>
    public static TheoryData<PairingMessage> EveryMessage()
    {
        var data = new TheoryData<PairingMessage>();

        foreach (var message in Enum.GetValues<PairingMessage>())
        {
            data.Add(message);
        }

        return data;
    }

    /// <summary>
    /// The five paths the specification fixes, spelled as that document spells them. This is
    /// the case that fails if a path is renamed on one side of the wire only.
    /// </summary>
    [Fact]
    public void TheFivePathsAreTheOnesTheSpecificationFixes()
    {
        Assert.Equal("/ServerPairing/hello", PeerPlane.PathFor(PairingMessage.Hello));
        Assert.Equal("/ServerPairing/confirm", PeerPlane.PathFor(PairingMessage.Confirm));
        Assert.Equal("/ServerPairing/rotate", PeerPlane.PathFor(PairingMessage.Rotate));
        Assert.Equal("/ServerPairing/revoke", PeerPlane.PathFor(PairingMessage.Revoke));
        Assert.Equal("/ServerPairing/exchange", PeerPlane.PathFor(PairingMessage.Exchange));
    }

    /// <summary>
    /// A path this plane owns is one the field shape accepts, so the two agree about what a
    /// path is rather than each carrying its own idea of one.
    /// </summary>
    /// <param name="message">The message whose path is judged.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void EveryPathIsOneTheFieldShapeAccepts(PairingMessage message)
    {
        Assert.True(FieldShape.IsPath(PeerPlane.PathFor(message)));
    }

    /// <summary>
    /// The four deviations the specification names, each refused rather than normalised: a
    /// trailing slash, a query string, a percent-encoded byte, and a different case.
    /// </summary>
    /// <param name="message">The message whose path is deviated from.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void EveryPathDeviationIsRefusedRatherThanNormalised(PairingMessage message)
    {
        var path = PeerPlane.PathFor(message);

        var deviations = new[]
        {
            path + "/",
            path + "?probe=1",
            path.Replace("/ServerPairing/", "/ServerPairing/%20", StringComparison.Ordinal),
            path.ToUpperInvariant(),
        };

        foreach (var deviation in deviations)
        {
            var outcome = Plane().Serve(message, Signed(message, target: deviation), At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
        }
    }

    /// <summary>
    /// A request whose target could not be read is refused rather than served from the routed
    /// path. That is the fail-closed direction: the routed path is the normalised one, and
    /// normalising is what this rule refuses.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ARequestWithNoReadableTargetIsRefused(PairingMessage message)
    {
        var outcome = Plane().Serve(message, Signed(message, target: null), At);

        Assert.Equal(RefusalCode.Refused, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
    }

    /// <summary>
    /// Every message on this plane is a POST. A request carrying a signature that would
    /// verify is still refused where it arrived as anything else, so the method is a rule
    /// rather than a consequence of routing.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void AMethodOtherThanPostIsRefused(PairingMessage message)
    {
        foreach (var method in new[] { "GET", "PUT", "DELETE", "post" })
        {
            var outcome = Plane().Serve(message, Signed(message, method: method), At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
        }
    }

    /// <summary>
    /// A request reaching a path without a verifying signature is refused, and its body is
    /// never handed past verification. The second half is the ordering the specification asks
    /// for: nothing richer than bytes exists for an unauthenticated caller to reach.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ARequestWithoutAVerifyingSignatureIsRefusedAndItsBodyIsNotHandedOn(PairingMessage message)
    {
        var signatures = new string?[]
        {
            null,
            "not base64 at all",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
        };

        foreach (var signature in signatures)
        {
            var outcome = Plane().Serve(message, Signed(message, signature: signature), At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
            Assert.True(outcome.VerifiedBody.IsEmpty);
        }
    }

    /// <summary>
    /// A request naming a pairing this server holds no key for is refused, and it is refused
    /// in the same shape as one naming the pairing it does hold and signing badly.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ARequestNamingAnUnknownPairingIsRefused(PairingMessage message)
    {
        var outcome = Plane().Serve(message, Signed(message, pairingId: "00000000000000000000000000000000"), At);

        Assert.Equal(RefusalCode.Refused, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
    }

    /// <summary>
    /// A missing header is a missing covered field, so there is nothing to compute a
    /// signature over and the request is refused. Each of the four covered header values is
    /// dropped in turn.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ARequestMissingACoveredHeaderIsRefused(PairingMessage message)
    {
        foreach (var without in new[] { "id", "version", "timestamp", "nonce" })
        {
            var outcome = Plane().Serve(message, Signed(message, drop: without), At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
        }
    }

    /// <summary>
    /// A body that verified is handed on whole. This is the other side of the ordering case
    /// above: without it, an implementation that never hands a body on at all would satisfy
    /// that case while serving nothing.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ABodyThatVerifiedIsHandedOnWhole(PairingMessage message)
    {
        var body = Encoding.ASCII.GetBytes("{\"probe\":\"body\"}");

        var outcome = Plane().Serve(message, Signed(message, body: body), At);

        Assert.True(outcome.BodyWasHandedOn);
        Assert.Equal(body, outcome.VerifiedBody.ToArray());
    }

    /// <summary>
    /// The limits the specification fixes: 1 MiB for exchange and 8 KiB for every other
    /// message.
    /// </summary>
    [Fact]
    public void TheBodyLimitsAreTheOnesTheSpecificationFixes()
    {
        Assert.Equal(1024 * 1024, PeerPlane.BodyLimitFor(PairingMessage.Exchange));

        var eightKibibyteMessages = new[]
        {
            PairingMessage.Hello,
            PairingMessage.Confirm,
            PairingMessage.Rotate,
            PairingMessage.Revoke,
        };

        foreach (var message in eightKibibyteMessages)
        {
            Assert.Equal(8 * 1024, PeerPlane.BodyLimitFor(message));
        }
    }

    /// <summary>
    /// A body over the limit for its message type is refused and is never handed past
    /// verification, so nothing parses it. The signature travelling with it is one that would
    /// verify, which is what makes this case about the limit rather than about the signature.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ABodyOverItsLimitIsRefusedAndNeverHandedOn(PairingMessage message)
    {
        var outcome = Plane().Serve(message, Signed(message, body: new byte[8], exceeded: true), At);

        Assert.Equal(RefusalCode.Refused, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
        Assert.True(outcome.VerifiedBody.IsEmpty);
    }

    /// <summary>
    /// Every refusal this plane produces carries the undistinguished code today, and that is
    /// the transition table rather than a decision of this type. No key store and no record
    /// store exist, so every pairing is Absent, and every cell of the Absent row is the
    /// undistinguished refusal.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void TheAbsentRowRefusesEveryMessage(PairingMessage message)
    {
        var transition = PairingStateMachine.Next(PairingState.Absent, message, OfferedKey.NotApplicable);

        Assert.Equal(TransitionOutcome.Refused, transition.Outcome);
        Assert.Equal(PairingState.Absent, transition.To);

        Assert.Equal(RefusalCode.Refused, Plane().Serve(message, Signed(message), At).Code);
    }

    /// <summary>
    /// Every cause of a refusal answers with the same bytes and the same status, which is what
    /// makes probing useless. A caller whose signature verified and one whose did not are
    /// among the causes, so the case covers the boundary the taxonomy is written around.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void EveryRefusalIsTheSameBytes(PairingMessage message)
    {
        var causes = new[]
        {
            Signed(message),
            Signed(message, target: PeerPlane.PathFor(message) + "/"),
            Signed(message, method: "GET"),
            Signed(message, signature: null),
            Signed(message, drop: "nonce"),
            Signed(message, body: new byte[8], exceeded: true),
            Signed(message, pairingId: "00000000000000000000000000000000"),
        };

        foreach (var outcome in causes.Select(cause => Plane().Serve(message, cause, At)))
        {
            Assert.Equal("{\"code\":\"refused\"}", Refusal.Body(outcome.Code));
        }

        Assert.Equal(403, Refusal.Status);
    }

    /// <summary>
    /// The taxonomy's spellings, every one of them, so a code renamed in the enumeration while
    /// the document stays still is caught here rather than by a peer.
    /// </summary>
    [Fact]
    public void EveryCodeCarriesTheSpellingTheTaxonomyGivesIt()
    {
        Assert.Equal("refused", Refusal.Wire(RefusalCode.Refused));
        Assert.Equal("clock", Refusal.Wire(RefusalCode.Clock));
        Assert.Equal("version", Refusal.Wire(RefusalCode.Version));
        Assert.Equal("state", Refusal.Wire(RefusalCode.State));
        Assert.Equal("malformed", Refusal.Wire(RefusalCode.Malformed));
        Assert.Equal("replay", Refusal.Wire(RefusalCode.Replay));
        Assert.Equal("busy", Refusal.Wire(RefusalCode.Busy));
    }

    /// <summary>
    /// Every refusal body is one JSON object with one member, whatever the code.
    /// </summary>
    [Fact]
    public void EveryRefusalBodyIsOneObjectWithOneMember()
    {
        foreach (var code in Enum.GetValues<RefusalCode>())
        {
            var body = Refusal.Body(code);

            Assert.Equal("{\"code\":\"" + Refusal.Wire(code) + "\"}", body);
            Assert.Equal(1, body.Count(c => c == ':'));
        }
    }

    /// <summary>
    /// A message outside the defined set is a caller error rather than a refusal, on both
    /// tables this type holds. Guessing a path or a limit for it would serve a sixth message
    /// this protocol does not have.
    /// </summary>
    [Fact]
    public void AMessageOutsideTheDefinedSetIsACallerError()
    {
        var undefined = (PairingMessage)99;

        Assert.Throws<ArgumentOutOfRangeException>(() => PeerPlane.PathFor(undefined));
        Assert.Throws<ArgumentOutOfRangeException>(() => PeerPlane.BodyLimitFor(undefined));
        Assert.Throws<ArgumentOutOfRangeException>(() => Refusal.Wire((RefusalCode)99));
    }

    /// <summary>
    /// An arrival past the limit is refused before it is verified. The requests before it
    /// carry the same signature and are handed on, so what separates the last one from them is
    /// the count and nothing else, and the body never reaching verification is what says the
    /// limit sits in front of the cryptography rather than behind it.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void AnArrivalPastTheLimitIsRefusedBeforeItIsVerified(PairingMessage message)
    {
        var plane = Plane();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            Assert.True(plane.Serve(message, Signed(message), At).BodyWasHandedOn);
        }

        var past = plane.Serve(message, Signed(message), At);

        Assert.False(past.BodyWasHandedOn);
        Assert.Equal(RefusalCode.Refused, past.Code);
        Assert.Equal("{\"code\":\"refused\"}", Refusal.Body(past.Code));
    }

    /// <summary>
    /// The allowance comes back a window later, so a peer that sent too fast is refused for a
    /// window rather than until the server is restarted. The instant is the only thing that
    /// differs between the refused arrival and the admitted one.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void TheAllowanceComesBackAWindowLater(PairingMessage message)
    {
        var plane = Plane();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            plane.Serve(message, Signed(message), At);
        }

        Assert.False(plane.Serve(message, Signed(message), At).BodyWasHandedOn);
        Assert.True(plane.Serve(message, Signed(message), At.AddSeconds(ArrivalLimit.WindowSeconds)).BodyWasHandedOn);
    }

    /// <summary>
    /// A flood claiming the enrolment identifier does not spend a pairing's allowance. The two
    /// are counted apart, which is the property that keeps a stranger who can reach this plane
    /// from ending a pairing's traffic by sending hellos at it.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void AFloodOnTheEnrolmentIdentifierLeavesAPairingsAllowanceAlone(PairingMessage message)
    {
        var plane = Plane();

        for (var i = 0; i < ArrivalLimit.ArrivalsPerEnrolment * 4; i++)
        {
            plane.Serve(message, Signed(message, pairingId: ArrivalLimit.EnrolmentPairingId), At);
        }

        Assert.True(plane.Serve(message, Signed(message), At).BodyWasHandedOn);
    }

    /// <summary>
    /// An arrival past the limit costs no key lookup and no signature computation. That is
    /// what the limit is for: a caller that has spent its allowance is refused before this
    /// server does the work, rather than after it has already done it.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void AnArrivalPastTheLimitCostsNoVerification(PairingMessage message)
    {
        var keys = new KnownKeys(PairingId, Key);
        var plane = new PeerPlane(new RequestAuthenticator(keys), new ArrivalLimit());

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            plane.Serve(message, Signed(message), At);
        }

        var asked = keys.Asked;

        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, asked);
        Assert.False(plane.Serve(message, Signed(message), At).BodyWasHandedOn);
        Assert.Equal(asked, keys.Asked);
    }

    private static PeerPlane Plane() => new PeerPlane(new RequestAuthenticator(new KnownKeys(PairingId, Key)), new ArrivalLimit());

    private static ArrivingRequest Signed(
        PairingMessage message,
        string? target = "",
        string? method = PeerPlane.Method,
        string? pairingId = PairingId,
        string? signature = "",
        string? drop = null,
        byte[]? body = null,
        bool exceeded = false)
    {
        var path = PeerPlane.PathFor(message);
        var bytes = body ?? Array.Empty<byte>();

        var id = string.Equals(drop, "id", StringComparison.Ordinal) ? null : pairingId;
        var version = string.Equals(drop, "version", StringComparison.Ordinal) ? null : Version;
        var timestamp = string.Equals(drop, "timestamp", StringComparison.Ordinal) ? null : Timestamp;
        var nonce = string.Equals(drop, "nonce", StringComparison.Ordinal) ? null : Nonce;

        // An empty signature argument means "sign this properly", so every case that is not
        // about the signature carries one that verifies and the request fails for its own
        // reason instead of for a signature nobody meant to break.
        var presented = signature;

        if (signature is { Length: 0 })
        {
            var signable = new PairingRequest(
                PeerPlane.Method,
                path,
                pairingId ?? string.Empty,
                Version,
                Timestamp,
                Nonce,
                bytes);

            presented = RequestAuthenticator.Sign(signable, Key);
        }

        return new ArrivingRequest(
            target is { Length: 0 } ? path : target,
            method,
            id,
            version,
            timestamp,
            nonce,
            presented,
            bytes,
            exceeded);
    }

    private sealed class KnownKeys : IPairingKeySource
    {
        private readonly string _known;
        private readonly byte[] _material;

        public KnownKeys(string known, byte[] material)
        {
            _known = known;
            _material = material;
        }

        /// <summary>
        /// Gets how many times a key has been asked for. A request the plane refuses before it
        /// verifies never reaches here, which is what makes the ordering observable rather
        /// than a claim about the source it is written in.
        /// </summary>
        public int Asked { get; private set; }

        public AcceptedKeys ArrivingKeys(string pairingId, DateTimeOffset at)
        {
            Asked++;

            return string.Equals(pairingId, _known, StringComparison.Ordinal)
                ? new AcceptedKeys(_material, default)
                : AcceptedKeys.None;
        }
    }
}
