using System;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// What the pairing plane accepts as a credential, and everything of the right shape that it
/// does not.
/// </summary>
/// <remarks>
/// The constraint these cases assert is issue #11 and <c>docs/threat-model.md</c>: the
/// credential is this plugin's own, it is the key held for one pairing, and a Jellyfin API key
/// is never one. The interesting half is not the malformed value, which any length check
/// catches. It is the value that decodes to exactly the right number of bytes and is still
/// refused, because that is what an attacker gets right first and what a length check cannot
/// separate.
/// <para>
/// The lengths here are read out of the specification rather than out of the code. A signature
/// is base64 of an HMAC-SHA-256 tag, which <c>docs/crypto.md</c> fixes at 32 bytes. The host
/// credential these cases refuse is 32 hexadecimal characters, which is 32 bytes of US-ASCII,
/// so it reaches the same decoded length by a different route.
/// </para>
/// <para>
/// The spelling issue #11 greps for is kept out of every identifier and every case name in
/// this file, so that the grep its third condition names goes on returning nothing outside
/// comments citing it. Where the comments have to say what the value is, they say it here.
/// </para>
/// </remarks>
public class PairingCredentialTests
{
    /// <summary>
    /// The tag length a signature carries, from <c>docs/crypto.md</c>. Written here rather
    /// than read from the implementation so that a case expecting it is not a case expecting
    /// whatever the code does.
    /// </summary>
    private const int TagBytes = 32;

    private const string PairingId = "3b1f0c7d9e2a48561bd0f37ac5e6902f";
    private const string OtherPairingId = "8c4e17a0d5b96238fe0417cba2d3560e";
    private const string Nonce = "5d2f81c0ab34e769025fbc18d3a7e64b";
    private const string Version = "1";
    private const string Timestamp = "1786000000";

    /// <summary>
    /// A credential of the shape the host issues, which is the one issue #11 says must never
    /// authenticate anything here: a Jellyfin API key, 32 hexadecimal characters. This literal
    /// came from no server, opens nothing, and is here to be refused.
    /// </summary>
    private const string HostCredential = "0f1e2d3c4b5a69788796a5b4c3d2e1f0";

    /// <summary>
    /// A value that decodes to exactly the length of a signature, and is not the signature,
    /// is refused. Each case is a different way of arriving at the right length, and none of
    /// them is malformed, so none of them can be refused by a length check.
    /// </summary>
    /// <param name="shape">Which right-length value arrived, named as the case.</param>
    [Theory]
    [InlineData("a host credential as US-ASCII")]
    [InlineData("32 zero bytes")]
    [InlineData("32 bytes of 0xFF")]
    [InlineData("32 random bytes")]
    [InlineData("the real signature with its last byte flipped")]
    public void ARightLengthCredentialThatIsNotTheSignatureIsRefused(string shape)
    {
        var key = RandomNumberGenerator.GetBytes(TagBytes);
        var request = Exchange();
        var real = Convert.FromBase64String(RequestAuthenticator.Sign(request, key));

        var presented = shape switch
        {
            "a host credential as US-ASCII" => Encoding.ASCII.GetBytes(HostCredential),
            "32 zero bytes" => new byte[TagBytes],
            "32 bytes of 0xFF" => Filled(0xFF),
            "32 random bytes" => RandomNumberGenerator.GetBytes(TagBytes),
            "the real signature with its last byte flipped" => Flipped(real),
            _ => throw new InvalidOperationException($"The case '{shape}' names no value.")
        };

        Assert.Equal(TagBytes, presented.Length);
        Assert.NotEqual(Convert.ToBase64String(real), Convert.ToBase64String(presented));

        var receiver = new RequestAuthenticator(new OneLiveKey(PairingId, key));

        Assert.Equal(
            VerificationOutcome.Refused,
            receiver.Verify(request, Convert.ToBase64String(presented)));
    }

    /// <summary>
    /// The floor under the table above. The same receiver, the same request and the signature
    /// the key actually produces, so the refusals are refusals of the value rather than of
    /// something wrong with the case.
    /// </summary>
    [Fact]
    public void TheSignatureTheKeyProducesVerifiesWithTheSameReceiver()
    {
        var key = RandomNumberGenerator.GetBytes(TagBytes);
        var request = Exchange();

        var receiver = new RequestAuthenticator(new OneLiveKey(PairingId, key));

        Assert.Equal(
            VerificationOutcome.Verified,
            receiver.Verify(request, RequestAuthenticator.Sign(request, key)));
    }

    /// <summary>
    /// A host credential does not become a pairing credential by being used the other way
    /// round, as the key the request is signed with. The receiver verifies against what the
    /// key source holds for the pairing and against nothing a caller brings, so a correctly
    /// constructed signature over a key this plugin never issued is refused like any other
    /// wrong value.
    /// </summary>
    /// <param name="encoding">How the credential was turned into key bytes, named as the case.</param>
    [Theory]
    [InlineData("its characters as US-ASCII")]
    [InlineData("the 16 bytes it spells in hexadecimal")]
    public void AHostCredentialUsedAsTheSigningKeyIsRefused(string encoding)
    {
        var material = encoding switch
        {
            "its characters as US-ASCII" => Encoding.ASCII.GetBytes(HostCredential),
            "the 16 bytes it spells in hexadecimal" => Convert.FromHexString(HostCredential),
            _ => throw new InvalidOperationException($"The case '{encoding}' names no encoding.")
        };

        var request = Exchange();
        var wellFormedSignature = RequestAuthenticator.Sign(request, material);

        var receiver = new RequestAuthenticator(
            new OneLiveKey(PairingId, RandomNumberGenerator.GetBytes(TagBytes)));

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(request, wellFormedSignature));
    }

    /// <summary>
    /// The credential is per pairing. A signature made with the key of one pairing is refused
    /// on a request naming another, with the receiver holding both pairings, so the refusal is
    /// the key not matching rather than the identifier not being found.
    /// </summary>
    [Fact]
    public void AKeyIsAcceptedOnlyForThePairingItBelongsTo()
    {
        var mine = RandomNumberGenerator.GetBytes(TagBytes);
        var theirs = RandomNumberGenerator.GetBytes(TagBytes);

        var request = Exchange().With(pairingId: OtherPairingId);
        var signedWithTheWrongPairingsKey = RequestAuthenticator.Sign(request, mine);

        var knowsBoth = new RequestAuthenticator(new TwoLiveKeys(PairingId, mine, OtherPairingId, theirs));

        Assert.Equal(VerificationOutcome.Refused, knowsBoth.Verify(request, signedWithTheWrongPairingsKey));
        Assert.Equal(
            VerificationOutcome.Verified,
            knowsBoth.Verify(request, RequestAuthenticator.Sign(request, theirs)));
    }

    /// <summary>
    /// A key that stops being live stops verifying, on the very next request. The receiver
    /// holds no key of its own and asks the source every time, so a pairing whose key the
    /// source no longer answers with is refused without the receiver being rebuilt or told.
    /// </summary>
    /// <remarks>
    /// What makes a key stop being live is revocation and the end of a rotation overlap, and
    /// neither is absent from the tree in the way this remark used to say. The overlap closes
    /// in <c>KeyOverlap.CloseIfElapsed</c>, which landed with issue #23. Revocation is a
    /// transition the state machine already takes, and what is missing there is the half that
    /// destroys the key rather than the transition, which is issue #24 over the store issue
    /// #30 owes. What is asserted here needs neither: whatever ends a key's life, ending it in
    /// the source is enough, because nothing downstream of the source caches it.
    /// </remarks>
    [Fact]
    public void AKeyThatStopsBeingLiveStopsVerifyingOnTheNextRequest()
    {
        var key = RandomNumberGenerator.GetBytes(TagBytes);
        var request = Exchange();
        var signature = RequestAuthenticator.Sign(request, key);

        var source = new OneLiveKey(PairingId, key);
        var receiver = new RequestAuthenticator(source);

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(request, signature));

        source.TheKeyIsNoLongerLive();

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(request, signature));
        Assert.Equal(2, source.Lookups);
    }

    private static byte[] Filled(byte value)
    {
        var bytes = new byte[TagBytes];
        Array.Fill(bytes, value);

        return bytes;
    }

    private static byte[] Flipped(byte[] signature)
    {
        var copy = (byte[])signature.Clone();
        copy[^1] ^= 0x01;

        return copy;
    }

    private static PairingRequest Exchange()
        => new PairingRequest(
            "POST",
            "/ServerPairing/exchange",
            PairingId,
            Version,
            Timestamp,
            Nonce,
            Encoding.UTF8.GetBytes("{\"users\":1}"));

    /// <summary>
    /// A key source holding one pairing's key, which can be told the key is no longer live.
    /// An identifier it does not hold is answered with an empty memory, which is what the
    /// interface asks of a real store.
    /// </summary>
    private sealed class OneLiveKey : IPairingKeySource
    {
        private readonly string _pairingId;
        private readonly byte[] _material;
        private bool _live = true;

        public OneLiveKey(string pairingId, byte[] material)
        {
            _pairingId = pairingId;
            _material = material;
        }

        public int Lookups { get; private set; }

        public void TheKeyIsNoLongerLive() => _live = false;

        public ReadOnlyMemory<byte> ArrivingKey(string pairingId)
        {
            Lookups++;

            return _live && string.Equals(pairingId, _pairingId, StringComparison.Ordinal)
                ? _material
                : ReadOnlyMemory<byte>.Empty;
        }
    }

    /// <summary>
    /// A key source holding two pairings, so a case can present one pairing's key against the
    /// other's identifier without the refusal being an unknown identifier.
    /// </summary>
    private sealed class TwoLiveKeys : IPairingKeySource
    {
        private readonly string _first;
        private readonly byte[] _firstMaterial;
        private readonly string _second;
        private readonly byte[] _secondMaterial;

        public TwoLiveKeys(string first, byte[] firstMaterial, string second, byte[] secondMaterial)
        {
            _first = first;
            _firstMaterial = firstMaterial;
            _second = second;
            _secondMaterial = secondMaterial;
        }

        public ReadOnlyMemory<byte> ArrivingKey(string pairingId)
        {
            if (string.Equals(pairingId, _first, StringComparison.Ordinal))
            {
                return _firstMaterial;
            }

            return string.Equals(pairingId, _second, StringComparison.Ordinal)
                ? _secondMaterial
                : ReadOnlyMemory<byte>.Empty;
        }
    }
}
