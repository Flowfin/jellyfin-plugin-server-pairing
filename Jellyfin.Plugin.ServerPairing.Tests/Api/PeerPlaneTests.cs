using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The six peer paths, and what a request arriving on one is told.
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
    /// A version no build of this plugin speaks, derived from the declared set rather than
    /// written down, so it stays outside the range on the day the range moves.
    /// </summary>
    private static readonly string Unspoken =
        (SupportedVersions.Highest + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The instant every case hands the plane. Time only matters here through the arrival
    /// limit, and no case in this file sends enough to reach one, so one instant serves them
    /// all; <c>ArrivalLimitTests</c> is where time is moved.
    /// </summary>
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private static byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);

    /// <summary>
    /// Gets a public key member of the length <c>docs/crypto.md</c> measured for a P-256
    /// <c>SubjectPublicKeyInfo</c>. The bytes are not a key and nothing imports them: what the
    /// member table fixes for that member is base64 inside a length limit, and this is that.
    /// </summary>
    private static string OfferedPublicKey { get; } =
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(91));

    /// <summary>
    /// The body the member table fixes for a message, so a case that is not about a body sends
    /// one the plane reads rather than one it refuses.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The body bytes.</returns>
    /// <remarks>
    /// The two versions are read out of the declared set rather than written down, so a case
    /// carrying this body offers a range that overlaps whatever this build speaks on the day the
    /// set moves. <c>revoke</c> and <c>unpair</c> get nothing, because the table says they carry
    /// none. <c>rotate</c> and <c>exchange</c> get bytes nothing reads: no reader on this plane
    /// judges either, so what they carry is free, and carrying something is what lets a case
    /// about handing a body on say anything for them.
    /// </remarks>
    private static byte[] BodyFor(PairingMessage message) => message switch
    {
        PairingMessage.Hello => HelloOffering(SupportedVersions.Lowest, SupportedVersions.Highest),
        PairingMessage.Confirm => Encoding.ASCII.GetBytes(
            "{\"" + ConfirmRequestBody.DigestMember + "\":\""
            + new string('a', ConfirmRequestBody.DigestLength) + "\"}"),
        PairingMessage.Rotate or PairingMessage.Exchange =>
            Encoding.ASCII.GetBytes("{\"probe\":\"body\"}"),
        _ => Array.Empty<byte>(),
    };

    /// <summary>
    /// A <c>hello</c> body the member table admits, offering a range.
    /// </summary>
    /// <param name="low">The lowest version the sender offers.</param>
    /// <param name="high">The highest version the sender offers.</param>
    /// <returns>The body bytes.</returns>
    private static byte[] HelloOffering(int low, int high) => Encoding.ASCII.GetBytes(
        "{\"" + HelloRequestBody.KeyMember + "\":\"" + OfferedPublicKey
        + "\",\"" + HelloRequestBody.VersionLowMember + "\":" + low.ToString(CultureInfo.InvariantCulture)
        + ",\"" + HelloRequestBody.VersionHighMember + "\":" + high.ToString(CultureInfo.InvariantCulture)
        + ",\"" + HelloRequestBody.AddressMember + "\":\"https://peer.example.org\"}");

    /// <summary>
    /// Every message this plane carries, so a case walks the six rather than naming one.
    /// </summary>
    /// <returns>The six messages.</returns>
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
    /// The six paths the specification fixes, spelled as that document spells them. This is
    /// the case that fails if a path is renamed on one side of the wire only.
    /// </summary>
    [Fact]
    public void TheSixPathsAreTheOnesTheSpecificationFixes()
    {
        Assert.Equal("/ServerPairing/hello", PeerPlane.PathFor(PairingMessage.Hello));
        Assert.Equal("/ServerPairing/confirm", PeerPlane.PathFor(PairingMessage.Confirm));
        Assert.Equal("/ServerPairing/rotate", PeerPlane.PathFor(PairingMessage.Rotate));
        Assert.Equal("/ServerPairing/revoke", PeerPlane.PathFor(PairingMessage.Revoke));
        Assert.Equal("/ServerPairing/exchange", PeerPlane.PathFor(PairingMessage.Exchange));
        Assert.Equal("/ServerPairing/unpair", PeerPlane.PathFor(PairingMessage.Unpair));
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
    /// <remarks>
    /// The body is the one the member table fixes for the message rather than a probe, because
    /// the plane reads a body now and a probe would be refused as malformed on the two messages
    /// that have a reader. What that costs is that <c>revoke</c> and <c>unpair</c> carry no body,
    /// so for those two this asserts that an empty body is handed on as empty; the messages that
    /// carry bytes are the ones that make the whole-body comparison say anything.
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void ABodyThatVerifiedIsHandedOnWhole(PairingMessage message)
    {
        var body = BodyFor(message);

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
            PairingMessage.Unpair,
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
    /// Every refusal body is one JSON object with one member, whatever the code, with
    /// <see cref="RefusalCode.Version"/> as the one exception the taxonomy names.
    /// </summary>
    /// <remarks>
    /// THIS CASE ASSERTED ONE MEMBER FOR EVERY CODE AND NOW EXCLUDES ONE BY NAME. The exclusion
    /// is written as a list of one rather than as a condition, so a second code given a second
    /// member reddens here instead of joining an exception that grew a rule of its own.
    /// </remarks>
    [Fact]
    public void EveryRefusalBodyIsOneObjectWithOneMemberButTheVersionOne()
    {
        foreach (var code in Enum.GetValues<RefusalCode>().Where(code => code != RefusalCode.Version))
        {
            var body = Refusal.Body(code);

            Assert.Equal("{\"code\":\"" + Refusal.Wire(code) + "\"}", body);
            Assert.Equal(1, body.Count(c => c == ':'));
        }
    }

    /// <summary>
    /// The version refusal carries the range this build speaks, in the member names a
    /// <c>hello</c> request uses for the same two numbers.
    /// </summary>
    /// <remarks>
    /// The whole body is compared rather than searched, because member order and spacing are
    /// what a caller parses and what a change here would move. The expected numbers are read
    /// out of <see cref="SupportedVersions"/> rather than written down, which is what makes
    /// this an assertion about one list rather than a second copy of it.
    /// </remarks>
    [Fact]
    public void AVersionRefusalCarriesTheRangeThisBuildSpeaks()
    {
        var body = Refusal.Body(RefusalCode.Version);

        Assert.Equal(
            "{\"code\":\"version\",\"versionLow\":" + SupportedVersions.Lowest
                + ",\"versionHigh\":" + SupportedVersions.Highest + "}",
            body);

        Assert.Equal(3, body.Count(c => c == ':'));
    }

    /// <summary>
    /// The two members land the right way round, driven with a range whose ends differ.
    /// </summary>
    /// <remarks>
    /// This build's lowest and highest version are the same number, so the case above cannot
    /// tell a body that reads the range correctly from one that reads the high end into both
    /// members - both produce the same bytes. Watched: swapping the two reads in the shape
    /// function leaves every other case green and reddens this one alone.
    /// </remarks>
    [Fact]
    public void TheLowEndAndTheHighEndAreNotInterchangeable()
    {
        Assert.Equal(
            "{\"code\":\"version\",\"versionLow\":2,\"versionHigh\":7}",
            Refusal.VersionBody(new VersionRange(2, 7)));
    }

    /// <summary>
    /// The refusal every route produces is the shape function over this build's own range, so
    /// there is one version refusal for a build rather than one per caller.
    /// </summary>
    [Fact]
    public void TheRefusalARouteProducesNamesThisBuildsOwnRange()
    {
        Assert.Equal(Refusal.VersionBody(SupportedVersions.Range), Refusal.Body(RefusalCode.Version));
    }

    /// <summary>
    /// The range a version refusal names is the range the negotiation selects against, so a
    /// peer told what this server speaks is told something it can act on.
    /// </summary>
    /// <remarks>
    /// The two are driven rather than compared as constants: a range one below this build's
    /// lowest has no version in common, and the numbers the refusal hands that peer are the
    /// ones that would have let it choose. A build whose refusal and whose negotiation read
    /// different lists passes neither half.
    /// </remarks>
    [Fact]
    public void TheRangeInAVersionRefusalIsTheOneTheNegotiationSelectsAgainst()
    {
        var tooOld = new VersionRange(SupportedVersions.Lowest - 1, SupportedVersions.Lowest - 1);

        Assert.Equal(VersionOutcome.NoVersionInCommon, VersionNegotiation.Select(tooOld).Outcome);

        var body = Refusal.Body(RefusalCode.Version);

        Assert.Contains("\"versionLow\":" + SupportedVersions.Range.Low, body, StringComparison.Ordinal);
        Assert.Contains("\"versionHigh\":" + SupportedVersions.Range.High, body, StringComparison.Ordinal);
        Assert.Equal(VersionOutcome.Selected, VersionNegotiation.Select(SupportedVersions.Range).Outcome);
    }

    /// <summary>
    /// A request whose signature verified and whose declared version is not one this build
    /// speaks is refused for the version, on every message, and nothing of its body is handed
    /// on.
    /// </summary>
    /// <param name="message">The message the request arrives as.</param>
    /// <remarks>
    /// The version is a covered field, so the request is signed at the version it declares: a
    /// request signed at one version and presented at another is refused by verification, which
    /// is a different case and is <c>RequestAuthenticationTests</c>'s.
    /// <para>
    /// The bytes are asserted through <see cref="Refusal.Body(RefusalCode)"/> because that is
    /// what the controller writes for a code, so what a peer receives is held against the range
    /// rather than only the enumeration member being checked.
    /// </para>
    /// </remarks>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public void AVerifiedRequestAtAVersionThisBuildDoesNotSpeakIsRefusedForTheVersion(PairingMessage message)
    {
        var outcome = Plane().Serve(message, Signed(message, version: Unspoken), At);

        Assert.Equal(RefusalCode.Version, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
        Assert.Equal(Refusal.VersionBody(SupportedVersions.Range), Refusal.Body(outcome.Code));
    }

    /// <summary>
    /// A caller holding no verifying key learns nothing from an unknown version, because the
    /// version is judged after verification and never before it.
    /// </summary>
    /// <remarks>
    /// This is the case the ordering exists for. The <c>version</c> code carries the range this
    /// server speaks, which the taxonomy allows only for a caller that has proved it holds the
    /// key or is inside a window an administrator opened, so a version judged before the
    /// signature would hand that range to anybody who asked. The two answers are compared as
    /// bytes rather than as codes, because bytes are what a stranger can tell apart.
    /// </remarks>
    [Fact]
    public void AStrangerLearnsNothingFromAnUnknownVersionBecauseTheVersionIsJudgedAfterVerification()
    {
        var plane = Plane();

        var atAnUnknownVersion = plane.Serve(
            PairingMessage.Hello,
            Signed(PairingMessage.Hello, signature: "not-the-signature", version: Unspoken),
            At);

        var atOneThisBuildSpeaks = plane.Serve(
            PairingMessage.Hello,
            Signed(PairingMessage.Hello, signature: "not-the-signature", carries: FreshNonce()),
            At);

        Assert.Equal(RefusalCode.Refused, atAnUnknownVersion.Code);
        Assert.Equal(
            Refusal.Body(atOneThisBuildSpeaks.Code),
            Refusal.Body(atAnUnknownVersion.Code));
    }

    /// <summary>
    /// Every version inside the declared range gets past that refusal and reaches the state the
    /// pairing is in, so what the plane judges is membership of a range rather than equality
    /// with one number.
    /// </summary>
    [Fact]
    public void EveryVersionThisBuildSpeaksIsNotRefusedForItsVersion()
    {
        var plane = Plane();

        for (var version = SupportedVersions.Lowest; version <= SupportedVersions.Highest; version++)
        {
            var outcome = plane.Serve(
                PairingMessage.Hello,
                Signed(
                    PairingMessage.Hello,
                    version: version.ToString(CultureInfo.InvariantCulture),
                    carries: FreshNonce()),
                At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.True(outcome.BodyWasHandedOn);
        }
    }

    /// <summary>
    /// A version one below the range and one above it are both refused for the version, so the
    /// check is not one-sided.
    /// </summary>
    /// <remarks>
    /// The low side is the half a build with one version cannot otherwise show: with
    /// <see cref="SupportedVersions.Lowest"/> and <see cref="SupportedVersions.Highest"/> equal,
    /// a check comparing against the high end alone passes every case that only goes upwards.
    /// </remarks>
    [Fact]
    public void AVersionBelowTheRangeAndOneAboveItAreBothRefusedForTheVersion()
    {
        var plane = Plane();

        foreach (var version in new[] { SupportedVersions.Lowest - 1, SupportedVersions.Highest + 1 })
        {
            var outcome = plane.Serve(
                PairingMessage.Hello,
                Signed(
                    PairingMessage.Hello,
                    version: version.ToString(CultureInfo.InvariantCulture),
                    carries: FreshNonce()),
                At);

            Assert.Equal(RefusalCode.Version, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
        }
    }

    /// <summary>
    /// A <c>hello</c> whose range does not overlap this build's is refused for the version, and
    /// the refusal names the range this build speaks. That is the second of the two callers the
    /// taxonomy lets see that code, and it is the half of it a declared version cannot reach: a
    /// range is two body members, so nothing could answer it while nothing read a body.
    /// </summary>
    /// <param name="low">The lowest version the peer offers.</param>
    /// <param name="high">The highest version it offers.</param>
    /// <remarks>
    /// Both directions, above the range and below it, because a build whose lowest and highest
    /// version are the same number cannot otherwise show that the comparison is not one-sided. A
    /// range below is expressible while <see cref="SupportedVersions.Lowest"/> is above zero,
    /// which the case asserts rather than assumes, so it stops being written the day that stops
    /// being true instead of quietly testing the same direction twice.
    /// </remarks>
    [Theory]
    [InlineData(2, 3)]
    [InlineData(0, 0)]
    public void AHelloOfferingNoVersionInCommonIsRefusedForTheVersionWithTheRangeNamed(int low, int high)
    {
        Assert.True(low > SupportedVersions.Highest || high < SupportedVersions.Lowest);

        var outcome = Plane().Serve(
            PairingMessage.Hello,
            Signed(PairingMessage.Hello, body: HelloOffering(low, high)),
            At);

        Assert.Equal(RefusalCode.Version, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
        Assert.Equal(Refusal.VersionBody(SupportedVersions.Range), Refusal.Body(outcome.Code));
    }

    /// <summary>
    /// A <c>hello</c> whose range does overlap is not refused for its version, which is the floor
    /// under the case above: without it, a plane that refused every <c>hello</c> for its version
    /// would satisfy that case.
    /// </summary>
    [Fact]
    public void AHelloOfferingARangeThatOverlapsIsNotRefusedForItsVersion()
    {
        var outcome = Plane().Serve(
            PairingMessage.Hello,
            Signed(PairingMessage.Hello, body: HelloOffering(SupportedVersions.Lowest, SupportedVersions.Highest + 5)),
            At);

        Assert.NotEqual(RefusalCode.Version, outcome.Code);
        Assert.Equal(RefusalCode.Refused, outcome.Code);
    }

    /// <summary>
    /// A verified message missing a member its declared version requires is refused rather than
    /// completed, and so is one carrying a body where the table says it carries none. A default
    /// is a value neither side agreed on standing in for one they would have had to send, and
    /// that is the failure issue #25 is written against.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="body">The body it arrives with.</param>
    [Theory]
    [InlineData(PairingMessage.Hello, "{\"versionLow\":1,\"versionHigh\":1,\"address\":\"https://peer.example.org\"}")]
    [InlineData(PairingMessage.Confirm, "{}")]
    [InlineData(PairingMessage.Revoke, "{\"reason\":\"revoked\"}")]
    [InlineData(PairingMessage.Unpair, "{}")]
    public void AVerifiedMessageWhoseBodyIsNotTheOneTheTableFixesIsRefusedAsMalformed(
        PairingMessage message,
        string body)
    {
        var outcome = Plane().Serve(message, Signed(message, body: Encoding.ASCII.GetBytes(body)), At);

        Assert.Equal(RefusalCode.Malformed, outcome.Code);
        Assert.False(outcome.BodyWasHandedOn);
        Assert.True(outcome.VerifiedBody.IsEmpty);
        Assert.Equal("{\"code\":\"malformed\"}", Refusal.Body(outcome.Code));
    }

    /// <summary>
    /// A caller holding no verifying key learns nothing from either of the two answers this file
    /// adds, because both are judged after verification and never before it. The same bytes that
    /// produce <c>malformed</c> and <c>version</c> for a caller holding the key produce the
    /// undistinguished refusal for one that does not.
    /// </summary>
    /// <remarks>
    /// Compared as bytes rather than as codes, because bytes are what a stranger can tell apart.
    /// </remarks>
    [Fact]
    public void AStrangerLearnsNothingFromAMalformedBodyOrARangeWithNoOverlap()
    {
        var plane = Plane();

        var bodies = new[]
        {
            Encoding.ASCII.GetBytes("{}"),
            HelloOffering(SupportedVersions.Highest + 1, SupportedVersions.Highest + 2),
        };

        foreach (var body in bodies)
        {
            var outcome = plane.Serve(
                PairingMessage.Hello,
                Signed(PairingMessage.Hello, signature: "not-the-signature", body: body, carries: FreshNonce()),
                At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.Equal("{\"code\":\"refused\"}", Refusal.Body(outcome.Code));
            Assert.False(outcome.BodyWasHandedOn);
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
            Assert.True(plane.Serve(message, Signed(message, carries: FreshNonce()), At).BodyWasHandedOn);
        }

        var past = plane.Serve(message, Signed(message, carries: FreshNonce()), At);

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
            plane.Serve(message, Signed(message, carries: FreshNonce()), At);
        }

        Assert.False(plane.Serve(message, Signed(message, carries: FreshNonce()), At).BodyWasHandedOn);
        Assert.True(plane.Serve(message, Signed(message, carries: FreshNonce()), At.AddSeconds(ArrivalLimit.WindowSeconds)).BodyWasHandedOn);
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
        var plane = new PeerPlane(new RequestAuthenticator(keys), new ArrivalLimit(), new FreshnessWindow());

        for (var i = 0; i < ArrivalLimit.ArrivalsPerPairing; i++)
        {
            plane.Serve(message, Signed(message, carries: FreshNonce()), At);
        }

        var asked = keys.Asked;

        Assert.Equal(ArrivalLimit.ArrivalsPerPairing, asked);
        Assert.False(plane.Serve(message, Signed(message, carries: FreshNonce()), At).BodyWasHandedOn);
        Assert.Equal(asked, keys.Asked);
    }

    /// <summary>
    /// A peer whose clock is outside the tolerated skew is refused for the clock, and that is a
    /// different answer from the one a bad signature gets. This is the fourth done condition of
    /// issue #26, and the distinction is the one <c>docs/threat-model.md</c> keeps on this plane
    /// deliberately rather than collapsing into the undistinguished refusal.
    /// </summary>
    /// <param name="direction">Which side of this server's clock the peer is on.</param>
    /// <remarks>
    /// Both directions, because a request from the future is as suspicious as one from the past,
    /// and a window applied in one direction only would pass a case that drove the other.
    /// </remarks>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void ASkewedPeerIsRefusedForTheClockRatherThanForItsSignature(int direction)
    {
        var stamp = Stamp(Skewed(direction, FreshnessWindow.WindowSeconds + 1));

        var skewed = Plane().Serve(
            PairingMessage.Exchange,
            Signed(PairingMessage.Exchange, carries: FreshNonce(), stamp: stamp),
            At);

        Assert.Equal(RefusalCode.Clock, skewed.Code);
        Assert.Equal("{\"code\":\"clock\"}", Refusal.Body(skewed.Code));
        Assert.False(skewed.BodyWasHandedOn);
        Assert.True(skewed.VerifiedBody.IsEmpty);

        // The same skew, presented by somebody who does not hold the key. What comes back is the
        // undistinguished refusal, so an operator reading the two answers is told which of them
        // happened rather than being left to guess.
        var unsigned = Plane().Serve(
            PairingMessage.Exchange,
            Signed(PairingMessage.Exchange, signature: null, carries: FreshNonce(), stamp: stamp),
            At);

        Assert.Equal(RefusalCode.Refused, unsigned.Code);
        Assert.NotEqual(Refusal.Body(skewed.Code), Refusal.Body(unsigned.Code));
    }

    /// <summary>
    /// A caller holding no verifying key learns nothing from a skew. A request that is both
    /// stale and unsigned is answered exactly as one that is merely unsigned, because freshness
    /// is judged after verification and never before it.
    /// </summary>
    /// <remarks>
    /// This is the sentence <c>docs/threat-model.md</c> closes its oracle section with, made
    /// into a case. Without it the clock refusal would hand every stranger one bit about this
    /// server's window, and the argument for keeping the distinction at all rests on their not
    /// getting it.
    /// </remarks>
    [Fact]
    public void AStrangerLearnsNothingFromASkewBecauseFreshnessIsJudgedAfterVerification()
    {
        var stale = Plane().Serve(
            PairingMessage.Exchange,
            Signed(
                PairingMessage.Exchange,
                signature: null,
                carries: FreshNonce(),
                stamp: Stamp(Skewed(1, FreshnessWindow.WindowSeconds + 1))),
            At);

        var fresh = Plane().Serve(
            PairingMessage.Exchange,
            Signed(PairingMessage.Exchange, signature: null, carries: FreshNonce()),
            At);

        Assert.Equal(RefusalCode.Refused, stale.Code);
        Assert.Equal(Refusal.Body(fresh.Code), Refusal.Body(stale.Code));
        Assert.False(stale.BodyWasHandedOn);
    }

    /// <summary>
    /// The edge of the window is inside it and the second past it is not, in both directions. A
    /// window compared with the wrong operator passes every case that stays well away from its
    /// edge, so these sit on it.
    /// </summary>
    /// <param name="direction">Which side of this server's clock the peer is on.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void TheEdgeOfTheWindowIsInsideItAndTheSecondPastItIsNot(int direction)
    {
        var edge = Plane().Serve(
            PairingMessage.Exchange,
            Signed(
                PairingMessage.Exchange,
                carries: FreshNonce(),
                stamp: Stamp(Skewed(direction, FreshnessWindow.WindowSeconds))),
            At);

        var past = Plane().Serve(
            PairingMessage.Exchange,
            Signed(
                PairingMessage.Exchange,
                carries: FreshNonce(),
                stamp: Stamp(Skewed(direction, FreshnessWindow.WindowSeconds + 1))),
            At);

        // Inside the window, so it reaches the transition table and is refused by that instead.
        Assert.Equal(RefusalCode.Refused, edge.Code);
        Assert.True(edge.BodyWasHandedOn);

        Assert.Equal(RefusalCode.Clock, past.Code);
        Assert.False(past.BodyWasHandedOn);
    }

    /// <summary>
    /// The same request sent twice is refused the second time, and the answer says replay. A
    /// correctly signed request that is captured and sent again is still correctly signed, which
    /// is why no signature check refuses one and why the nonce store exists at all.
    /// </summary>
    [Fact]
    public void TheSameRequestSentTwiceIsRefusedAsAReplayTheSecondTime()
    {
        var plane = Plane();
        var once = Signed(PairingMessage.Exchange, carries: FreshNonce());

        var first = plane.Serve(PairingMessage.Exchange, once, At);
        var second = plane.Serve(PairingMessage.Exchange, once, At);

        Assert.Equal(RefusalCode.Refused, first.Code);
        Assert.True(first.BodyWasHandedOn);

        Assert.Equal(RefusalCode.Replay, second.Code);
        Assert.Equal("{\"code\":\"replay\"}", Refusal.Body(second.Code));
        Assert.False(second.BodyWasHandedOn);
        Assert.True(second.VerifiedBody.IsEmpty);
    }

    /// <summary>
    /// A nonce is remembered under the pairing it arrived on rather than for the whole server,
    /// so a nonce seen once does not refuse a second pairing that carries the same one.
    /// </summary>
    /// <remarks>
    /// Remembering across pairings would let one peer end another's traffic by sending the
    /// nonces it expects that peer to use, which is a denial the store would have created rather
    /// than refused. The second request here does not verify, and that is the point: it is
    /// refused before freshness is reached, so what this asserts is that it was not refused as a
    /// replay of the first.
    /// </remarks>
    [Fact]
    public void ANonceIsRememberedForThePairingItArrivedUnderAndNotForTheServer()
    {
        var plane = Plane();
        var carries = FreshNonce();
        var once = Signed(PairingMessage.Exchange, carries: carries);

        plane.Serve(PairingMessage.Exchange, once, At);

        Assert.Equal(RefusalCode.Replay, plane.Serve(PairingMessage.Exchange, once, At).Code);

        var elsewhere = plane.Serve(
            PairingMessage.Exchange,
            Signed(PairingMessage.Exchange, pairingId: "0011223344556677889900aabbccddee", carries: carries),
            At);

        Assert.Equal(RefusalCode.Refused, elsewhere.Code);
    }

    /// <summary>
    /// The instant on a peer's clock this many seconds to one side of this server's.
    /// </summary>
    /// <param name="direction">Which side, as 1 for the future and -1 for the past.</param>
    /// <param name="seconds">How far.</param>
    /// <returns>The instant.</returns>
    /// <remarks>
    /// The product is taken in <see cref="double"/> rather than in <see cref="int"/>. Both
    /// factors are small constants and neither could overflow, but an integer multiplication
    /// whose result is handed to a parameter taking a double is a shape the analysis refuses on
    /// sight, and writing it so that it cannot be wrong costs less than arguing that this
    /// instance is safe.
    /// </remarks>
    private static DateTimeOffset Skewed(int direction, int seconds) =>
        At.AddSeconds(direction * (double)seconds);

    /// <summary>
    /// The timestamp a peer whose clock reads this instant puts on a request.
    /// </summary>
    /// <param name="at">The peer's clock.</param>
    /// <returns>The timestamp, as it is spelled on the wire.</returns>
    private static string Stamp(DateTimeOffset at) =>
        at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    private static PeerPlane Plane() => new PeerPlane(new RequestAuthenticator(new KnownKeys(PairingId, Key)), new ArrivalLimit(), new FreshnessWindow());

    /// <summary>
    /// A nonce no other request in a case carries, of the shape the specification fixes.
    /// </summary>
    /// <remarks>
    /// A case that sends several requests to reach a limit is sending several REQUESTS, and the
    /// specification says two that differ in nothing else must differ here. Reusing one nonce
    /// makes every send after the first a replay, which is a different refusal from the one
    /// those cases are about, so they would pass or fail for the wrong reason.
    /// </remarks>
    /// <returns>The nonce.</returns>
    private static string FreshNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(FieldShape.HexFieldLength / 2)).ToLowerInvariant();

    private static ArrivingRequest Signed(
        PairingMessage message,
        string? target = "",
        string? method = PeerPlane.Method,
        string? pairingId = PairingId,
        string? signature = "",
        string? drop = null,
        byte[]? body = null,
        bool exceeded = false,
        string? carries = null,
        string stamp = Timestamp,
        string? version = null)
    {
        var path = PeerPlane.PathFor(message);

        // The body the member table fixes, unless a case named its own. A case about anything
        // other than a body sends one the plane reads, so it fails for its own reason instead of
        // for a body nobody meant to make malformed - which is the same argument the signature
        // argument below is written under.
        var bytes = body ?? BodyFor(message);
        var carried = carries ?? Nonce;
        var declared = version ?? Version;

        var id = string.Equals(drop, "id", StringComparison.Ordinal) ? null : pairingId;
        var sent = string.Equals(drop, "version", StringComparison.Ordinal) ? null : declared;
        var timestamp = string.Equals(drop, "timestamp", StringComparison.Ordinal) ? null : stamp;
        var nonce = string.Equals(drop, "nonce", StringComparison.Ordinal) ? null : carried;

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
                declared,
                stamp,
                carried,
                bytes);

            presented = RequestAuthenticator.Sign(signable, Key);
        }

        return new ArrivingRequest(
            target is { Length: 0 } ? path : target,
            method,
            id,
            sent,
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
