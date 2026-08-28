using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Signing and verifying across the canonical form, from both ends in one process.
/// </summary>
/// <remarks>
/// The field list and every limit asserted here are read out of <c>docs/protocol.md</c>
/// rather than out of the implementation. A case whose expectation can only be justified by
/// reading the code is a case testing the code against itself.
/// </remarks>
public class RequestAuthenticationTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Version = "1";
    private const string Timestamp = "1786000000";

    /// <summary>
    /// The instant these cases judge at. Nothing here is about an overlap running out, so one
    /// value is handed to every verification: what the instant decides is which of a pairing's
    /// keys the source answers with, and the sources in this file hold one key each.
    /// </summary>
    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// One side signs and the other verifies, over a request carrying every covered field
    /// and a body.
    /// </summary>
    [Fact]
    public void OneSideSignsAndTheOtherVerifies()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Exchange();

        var signature = RequestAuthenticator.Sign(request, key);
        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(request, signature, At));
    }

    /// <summary>
    /// The same, over a request with no body. An empty body is covered by the digest of the
    /// empty string rather than by a branch, so it is a case rather than an exception.
    /// </summary>
    [Fact]
    public void ARequestWithNoBodyVerifiesTheSameWay()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Revoke();

        var signature = RequestAuthenticator.Sign(request, key);
        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(request, signature, At));
    }

    /// <summary>
    /// Every field the canonical form covers, mutated one at a time. Each mutation is a
    /// different request and the signature is over the one that was signed, so each has to
    /// be refused.
    /// </summary>
    /// <param name="field">The covered field being mutated, named as the case.</param>
    [Theory]
    [InlineData("version")]
    [InlineData("method")]
    [InlineData("path")]
    [InlineData("pairing id")]
    [InlineData("timestamp")]
    [InlineData("nonce")]
    [InlineData("body")]
    public void EveryCoveredFieldIsRefusedWhenItMoves(string field)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var signed = Exchange();
        var signature = RequestAuthenticator.Sign(signed, key);

        var arrived = field switch
        {
            "version" => signed.With(version: "2"),
            "method" => signed.With(method: "PUT"),
            "path" => signed.With(path: "/ServerPairing/revoke"),
            "pairing id" => signed.With(pairingId: "9f8c1d2b3a4e5f60718293a4b5c6d7e9"),
            "timestamp" => signed.With(timestamp: "1786000001"),
            "nonce" => signed.With(nonce: "0123456789abcdef0123456789abcdee"),
            "body" => signed.With(body: Bytes("{\"users\":2}")),
            _ => throw new InvalidOperationException($"The case '{field}' names no covered field.")
        };

        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(arrived, signature, At));
    }

    /// <summary>
    /// The mutation table above proves nothing unless the unmutated request verifies with
    /// the same receiver, which is the floor under it.
    /// </summary>
    [Fact]
    public void TheUnmutatedRequestVerifiesWithTheSameReceiver()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var signed = Exchange();

        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(signed, RequestAuthenticator.Sign(signed, key), At));
    }

    /// <summary>
    /// A header the canonical form does not name can be added, removed or rewritten without
    /// breaking verification. An intermediary is allowed to do all three, so a signature that
    /// covered them would be a signature that fails for reasons nobody controls.
    /// </summary>
    [Fact]
    public void AnUncoveredHeaderCanBeChangedWithoutBreakingVerification()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var signed = Exchange();
        var signature = RequestAuthenticator.Sign(signed, key);

        var rewritten = signed.With(uncoveredHeaders: new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["User-Agent"] = "something else entirely",
            ["X-Forwarded-For"] = "203.0.113.7",
            ["Accept-Encoding"] = "gzip",
        });

        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(rewritten, signature, At));
    }

    /// <summary>
    /// The body is not deserialised on a request that fails verification. The reader counts
    /// its own calls, so this asserts the ordering rather than an absence.
    /// </summary>
    [Fact]
    public void TheBodyIsNeverReadOnARequestThatFailsVerification()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Exchange();
        var reader = new CountingReader();

        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));
        var outcome = receiver.VerifyThenRead(
            request,
            Convert.ToBase64String(new byte[32]),
            At,
            reader.Read,
            out var value);

        Assert.Equal(VerificationOutcome.Refused, outcome);
        Assert.Equal(0, reader.Calls);
        Assert.Null(value);
    }

    /// <summary>
    /// The counting reader is only worth anything if it counts, so the same call on a request
    /// that does verify reaches it exactly once.
    /// </summary>
    [Fact]
    public void TheBodyIsReadOnceOnARequestThatVerifies()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Exchange();
        var reader = new CountingReader();

        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, key));
        var outcome = receiver.VerifyThenRead(
            request,
            RequestAuthenticator.Sign(request, key),
            At,
            reader.Read,
            out var value);

        Assert.Equal(VerificationOutcome.Verified, outcome);
        Assert.Equal(1, reader.Calls);
        Assert.Equal("{\"users\":1}", value);
    }

    /// <summary>
    /// A request naming a pairing nobody knows is refused with the same outcome as one naming
    /// a pairing that exists and signing it badly. The two are one value, so no caller can
    /// separate them by reading an answer.
    /// </summary>
    [Fact]
    public void AnUnknownPairingAndABadSignatureAreOneOutcome()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var request = Exchange();
        var somebodyElsesSignature = RequestAuthenticator.Sign(request, RandomNumberGenerator.GetBytes(32));

        var knowsNothing = new RequestAuthenticator(new KnownKeys("00000000000000000000000000000000", key));
        var knowsThePairing = new RequestAuthenticator(new KnownKeys(PairingId, key));

        Assert.Equal(VerificationOutcome.Refused, knowsNothing.Verify(request, somebodyElsesSignature, At));
        Assert.Equal(VerificationOutcome.Refused, knowsThePairing.Verify(request, somebodyElsesSignature, At));
    }

    /// <summary>
    /// A pairing the key source does not hold is judged against a key nobody outside this
    /// process has, so a request signed under the empty key is refused.
    /// </summary>
    /// <remarks>
    /// The neighbouring case above asserts that an unknown pairing and a bad signature are one
    /// outcome, and it holds whether the absent pairing is answered with a drawn key or with
    /// the empty span that arrived. This one does not. An empty key makes the tag a value any
    /// caller can compute for a request of their choosing, so the absent-pairing branch would
    /// be one a stranger can satisfy rather than one that costs the same as a present pairing.
    /// </remarks>
    [Fact]
    public void AnAbsentPairingIsNotVerifiableUnderTheEmptyKey()
    {
        var request = Exchange();
        var signedUnderNothing = RequestAuthenticator.Sign(request, ReadOnlySpan<byte>.Empty);

        var knowsAnotherPairing = new RequestAuthenticator(
            new KnownKeys("00000000000000000000000000000000", RandomNumberGenerator.GetBytes(32)));

        Assert.Equal(VerificationOutcome.Refused, knowsAnotherPairing.Verify(request, signedUnderNothing, At));
    }

    /// <summary>
    /// A signature that is well-formed base64 of the wrong length does not decode, asserted on
    /// the decoder rather than through a verification.
    /// </summary>
    /// <remarks>
    /// Reached through <see cref="RequestAuthenticator.Verify"/> the outcome is
    /// <see cref="VerificationOutcome.Refused"/> whether the length is checked or not, because
    /// a short tag fails the comparison anyway. The direct call is what separates the two, and
    /// the length half of this method's summary is unproved without it.
    /// </remarks>
    /// <param name="presented">Base64 that decodes to fewer than the tag length.</param>
    [Theory]
    [InlineData("AAAA")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA==")]
    public void BaseSixtyFourOfTheWrongLengthDoesNotDecode(string presented)
    {
        Assert.False(RequestAuthenticator.TryDecodeSignature(presented, out _));
    }

    /// <summary>
    /// The same decoder accepts base64 of exactly the tag length, which is the floor under the
    /// two refusals above: without it they would pass against a decoder that refuses
    /// everything.
    /// </summary>
    [Fact]
    public void BaseSixtyFourOfTheTagLengthDecodes()
    {
        var tag = RandomNumberGenerator.GetBytes(RequestAuthenticator.SignatureLength);

        Assert.True(RequestAuthenticator.TryDecodeSignature(Convert.ToBase64String(tag), out var decoded));
        Assert.Equal(tag, decoded);
    }

    /// <summary>
    /// Signing a request that fails its field limits throws rather than producing a tag over
    /// what is left of it.
    /// </summary>
    /// <remarks>
    /// The same table the verifier refuses is signed here, so the two sides of this type agree
    /// about which requests exist. The message is asserted because it is the whole of what a
    /// caller gets: the parameter name says which argument was wrong and nothing about why.
    /// </remarks>
    /// <param name="malformed">A request no signature can rescue.</param>
    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public void SigningAMalformedRequestThrowsRatherThanSigningWhatIsLeft(PairingRequest malformed)
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var thrown = Assert.Throws<ArgumentException>(() => RequestAuthenticator.Sign(malformed, key.AsSpan()));

        Assert.Equal("request", thrown.ParamName);
        Assert.Contains("covered field", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A request missing a covered field is refused before a signature is computed over it,
    /// so no key is ever fetched for it. The key source counts its own calls.
    /// </summary>
    /// <param name="malformed">A request that no signature can rescue.</param>
    [Theory]
    [MemberData(nameof(MalformedRequests))]
    public void AMalformedRequestIsRefusedBeforeAKeyIsFetched(PairingRequest malformed)
    {
        var keys = new KnownKeys(PairingId, RandomNumberGenerator.GetBytes(32));
        var receiver = new RequestAuthenticator(keys);

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(malformed, Convert.ToBase64String(new byte[32]), At));
        Assert.Equal(0, keys.Lookups);
    }

    /// <summary>
    /// Gets the requests the shape check has to refuse. Each one is a rule in
    /// <c>docs/protocol.md</c>, and the trailing slash and the query string are the two that
    /// document says implementations disagree about.
    /// </summary>
    public static TheoryData<PairingRequest> MalformedRequests()
    {
        return new TheoryData<PairingRequest>
        {
            Exchange().With(path: "/ServerPairing/exchange/"),
            Exchange().With(path: "/ServerPairing/exchange?pairing=1"),
            Exchange().With(path: "/ServerPairing/exchange%2F"),
            Exchange().With(method: "post"),
            Exchange().With(method: string.Empty),
            Exchange().With(pairingId: "9F8C1D2B3A4E5F60718293A4B5C6D7E8"),
            Exchange().With(pairingId: "9f8c1d2b"),
            Exchange().With(nonce: string.Empty),
            Exchange().With(version: "01"),
            Exchange().With(version: "12345"),
            Exchange().With(timestamp: string.Empty),
            Exchange().With(timestamp: "-1786000000"),
        };
    }

    /// <summary>
    /// A signature that is not base64, or is base64 of the wrong length, is refused rather
    /// than throwing.
    /// </summary>
    /// <param name="presented">The header value that arrived.</param>
    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all")]
    [InlineData("AAAA")]
    public void ASignatureThatIsNotOneIsRefusedRatherThanThrown(string presented)
    {
        var receiver = new RequestAuthenticator(new KnownKeys(PairingId, RandomNumberGenerator.GetBytes(32)));

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(Exchange(), presented, At));
    }

    /// <summary>
    /// The canonical form is eight lines, each ending in one line feed, with no carriage
    /// return anywhere. That is the shape <c>docs/protocol.md</c> fixes, and it is asserted
    /// here rather than left to be inferred from a signature matching.
    /// </summary>
    [Fact]
    public void TheCanonicalFormIsEightLinesEndingInLineFeeds()
    {
        var canonical = Encoding.ASCII.GetString(CanonicalForm.ForRequest(Exchange()));

        Assert.DoesNotContain("\r", canonical, StringComparison.Ordinal);
        Assert.EndsWith("\n", canonical, StringComparison.Ordinal);
        Assert.Equal(8, canonical.Split('\n').Length - 1);
        Assert.StartsWith(CanonicalForm.RequestLabel + "\n", canonical, StringComparison.Ordinal);
    }

    /// <summary>
    /// A response's canonical form carries the nonce of the request it answers, which is what
    /// stops a captured response being replayed against a different request.
    /// </summary>
    [Fact]
    public void AResponseIsBoundToTheNonceOfItsRequest()
    {
        var one = CanonicalForm.ForResponse(Version, PairingId, Nonce, Timestamp, ReadOnlyMemory<byte>.Empty);
        var other = CanonicalForm.ForResponse(
            Version,
            PairingId,
            "0123456789abcdef0123456789abcdee",
            Timestamp,
            ReadOnlyMemory<byte>.Empty);

        Assert.NotEqual(Encoding.ASCII.GetString(one), Encoding.ASCII.GetString(other));
        Assert.StartsWith(CanonicalForm.ResponseLabel + "\n", Encoding.ASCII.GetString(one), StringComparison.Ordinal);
    }

    private static PairingRequest Exchange()
        => new PairingRequest(
            "POST",
            "/ServerPairing/exchange",
            PairingId,
            Version,
            Timestamp,
            Nonce,
            Bytes("{\"users\":1}"));

    private static PairingRequest Revoke()
        => new PairingRequest(
            "POST",
            "/ServerPairing/revoke",
            PairingId,
            Version,
            Timestamp,
            Nonce,
            ReadOnlyMemory<byte>.Empty);

    private static ReadOnlyMemory<byte> Bytes(string text)
        => Encoding.UTF8.GetBytes(text);

    /// <summary>
    /// A key source that knows one pairing, and counts how often it was asked, so a test can
    /// assert that a malformed request never reached it.
    /// </summary>
    private sealed class KnownKeys : IPairingKeySource
    {
        private readonly string _known;
        private readonly byte[] _material;

        public KnownKeys(string known, byte[] material)
        {
            _known = known;
            _material = material;
        }

        public int Lookups { get; private set; }

        public AcceptedKeys ArrivingKeys(string pairingId, DateTimeOffset at)
        {
            Lookups++;

            return string.Equals(pairingId, _known, StringComparison.Ordinal)
                ? new AcceptedKeys(_material, default)
                : AcceptedKeys.None;
        }
    }

    /// <summary>
    /// A body reader that counts its own calls, which is what turns "the deserialiser was not
    /// reached" from an absence into an assertion.
    /// </summary>
    private sealed class CountingReader
    {
        public int Calls { get; private set; }

        public string Read(ReadOnlyMemory<byte> body)
        {
            Calls++;

            return Encoding.UTF8.GetString(body.Span);
        }
    }
}
