using System;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The two values two public keys derive, judged against the construction
/// <c>docs/protocol.md</c> writes down rather than against the code that produces them.
/// </summary>
/// <remarks>
/// The expectation in every case below is either built here from the documented construction,
/// or is a relation between two calls that the construction forces. Nothing is a value copied
/// out of a run, because a case whose expectation can only be justified by reading the
/// implementation is a case testing the code against itself.
/// <para>
/// The keys are real <c>ECDiffieHellman</c> P-256 public keys in their DER
/// <c>SubjectPublicKeyInfo</c> encoding, which is the encoding the document names. Nothing in
/// the plugin generates one; these are the test's own, and what they buy over arbitrary bytes
/// is that the sort and the digest meet the lengths and the byte patterns real keys have.
/// </para>
/// </remarks>
public class PairingIdentityTests
{
    /// <summary>
    /// Both sides derive the same identifier from the same two keys, whichever order they hold
    /// them in. That is the property the whole design rests on: neither side chooses the value
    /// and neither side has to be told it.
    /// </summary>
    [Fact]
    public void TheIdentifierIsTheSameWhicheverOrderTheKeysAreHeldIn()
    {
        var one = PublicKey();
        var other = PublicKey();

        Assert.Equal(
            PairingIdentity.IdentifierFor(one, other),
            PairingIdentity.IdentifierFor(other, one));
    }

    /// <summary>
    /// The same, for the fingerprint. Two operators reading two screens are reading one value,
    /// and a fingerprint that depended on which server computed it would be two values that a
    /// person would be asked to accept as one.
    /// </summary>
    [Fact]
    public void TheFingerprintIsTheSameWhicheverOrderTheKeysAreHeldIn()
    {
        var one = PublicKey();
        var other = PublicKey();

        Assert.Equal(
            PairingIdentity.FingerprintDigestFor(one, other),
            PairingIdentity.FingerprintDigestFor(other, one));
    }

    /// <summary>
    /// The identifier is what the document says it is: the lowercase hex of the first 16 bytes
    /// of SHA-256 over the label, a zero byte, and the two encodings sorted ascending and
    /// concatenated. The expectation is built here from that sentence.
    /// </summary>
    [Fact]
    public void TheIdentifierIsTheDocumentedDigestOfTheDocumentedMaterial()
    {
        var one = PublicKey();
        var other = PublicKey();

        var expected = Hex(
            Digest(PairingIdentity.IdentifierLabel, one, other)
                .AsSpan(0, PairingIdentity.BytesAnOperatorCompares));

        Assert.Equal(expected, PairingIdentity.IdentifierFor(one, other));
    }

    /// <summary>
    /// The fingerprint digest is the whole SHA-256 under the other label, which is 64 lowercase
    /// hex characters, and that is what the field table fixes for the value a <c>confirm</c>
    /// carries.
    /// </summary>
    [Fact]
    public void TheFingerprintDigestIsTheWholeDigestUnderTheOtherLabel()
    {
        var one = PublicKey();
        var other = PublicKey();

        var digest = PairingIdentity.FingerprintDigestFor(one, other);

        Assert.Equal(Hex(Digest(PairingIdentity.FingerprintLabel, one, other)), digest);
        Assert.Equal(SHA256.HashSizeInBytes * 2, digest.Length);
    }

    /// <summary>
    /// The identifier is 32 lowercase hex characters, which is the shape every other reader of
    /// a pairing identifier in this protocol already refuses anything else against.
    /// </summary>
    [Fact]
    public void TheIdentifierIsTheHexFieldShapeTheProtocolFixes()
    {
        var identifier = PairingIdentity.IdentifierFor(PublicKey(), PublicKey());

        Assert.True(FieldShape.IsHexField(identifier));
    }

    /// <summary>
    /// The two values over one pair of keys are different, and neither is a prefix of the
    /// other. The material is identical, so the label is the only thing separating them, which
    /// is exactly what the labels are for: a value produced for one purpose cannot be presented
    /// as the value for the other.
    /// </summary>
    [Fact]
    public void TheTwoLabelsKeepTheTwoValuesApartOverIdenticalMaterial()
    {
        var one = PublicKey();
        var other = PublicKey();

        var identifier = PairingIdentity.IdentifierFor(one, other);
        var fingerprint = PairingIdentity.FingerprintDigestFor(one, other);

        Assert.NotEqual(identifier, fingerprint);
        Assert.False(fingerprint.StartsWith(identifier, StringComparison.Ordinal));
        Assert.NotEqual(identifier, PairingIdentity.ShownToAnOperator(fingerprint));
    }

    /// <summary>
    /// The zero byte after the label is in the digest. Digesting the same label and material
    /// without it produces a different value, so the separator is a byte that is there rather
    /// than a sentence about one.
    /// </summary>
    [Fact]
    public void TheZeroByteAfterTheLabelIsInTheDigest()
    {
        var one = PublicKey();
        var other = PublicKey();

        using var withoutTheZeroByte = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        withoutTheZeroByte.AppendData(Encoding.ASCII.GetBytes(PairingIdentity.IdentifierLabel));
        AppendSorted(withoutTheZeroByte, one, other);

        var wrong = Hex(withoutTheZeroByte.GetHashAndReset()
            .AsSpan(0, PairingIdentity.BytesAnOperatorCompares));

        Assert.NotEqual(wrong, PairingIdentity.IdentifierFor(one, other));
    }

    /// <summary>
    /// Changing one of the two keys changes both values. A pairing is the two keys, so a peer
    /// offering a different key is a different pairing with a different fingerprint, and that
    /// is the property an operator comparing by eye is relying on.
    /// </summary>
    [Fact]
    public void ADifferentKeyOnOneSideMovesBothValues()
    {
        var mine = PublicKey();
        var yours = PublicKey();
        var somebodyElses = PublicKey();

        Assert.NotEqual(
            PairingIdentity.IdentifierFor(mine, yours),
            PairingIdentity.IdentifierFor(mine, somebodyElses));

        Assert.NotEqual(
            PairingIdentity.FingerprintDigestFor(mine, yours),
            PairingIdentity.FingerprintDigestFor(mine, somebodyElses));
    }

    /// <summary>
    /// What an operator reads is the leading 128 bits of the digest, which is its first 32
    /// characters, and it is lowercase hex like everything else here.
    /// </summary>
    [Fact]
    public void AnOperatorReadsTheLeading128BitsOfTheDigest()
    {
        var digest = PairingIdentity.FingerprintDigestFor(PublicKey(), PublicKey());

        var shown = PairingIdentity.ShownToAnOperator(digest);

        Assert.Equal(PairingIdentity.BytesAnOperatorCompares * 2, shown.Length);
        Assert.Equal(digest.Substring(0, PairingIdentity.BytesAnOperatorCompares * 2), shown);
        Assert.True(FieldShape.IsHexField(shown));
    }

    /// <summary>
    /// Anything that is not a whole digest is refused rather than truncated. The failure this
    /// is against is the one an operator cannot see: a value already shortened once, shortened
    /// again, still looks like a fingerprint on a screen.
    /// </summary>
    /// <param name="notADigest">A value that is not a fingerprint digest.</param>
    [Theory]
    [InlineData("")]
    [InlineData("9f8c1d2b3a4e5f60718293a4b5c6d7e8")]
    [InlineData("9F8C1D2B3A4E5F60718293A4B5C6D7E89F8C1D2B3A4E5F60718293A4B5C6D7E8")]
    [InlineData("9f8c1d2b3a4e5f60718293a4b5c6d7e89f8c1d2b3a4e5f60718293a4b5c6d7e")]
    [InlineData("9f8c1d2b3a4e5f60718293a4b5c6d7e89f8c1d2b3a4e5f60718293a4b5c6d7g8")]
    public void AValueThatIsNotAWholeDigestIsRefusedRatherThanTruncated(string notADigest)
    {
        Assert.Throws<ArgumentException>(() => PairingIdentity.ShownToAnOperator(notADigest));
    }

    /// <summary>
    /// A fresh P-256 public key in the encoding the document names.
    /// </summary>
    /// <returns>The DER <c>SubjectPublicKeyInfo</c> bytes.</returns>
    private static byte[] PublicKey()
    {
        using var pair = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        return pair.PublicKey.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The documented construction, written out here so the expectation comes from the document
    /// rather than from the type under test.
    /// </summary>
    /// <param name="label">The context label.</param>
    /// <param name="one">One public key encoding.</param>
    /// <param name="other">The other.</param>
    /// <returns>The whole SHA-256 digest.</returns>
    private static byte[] Digest(string label, byte[] one, byte[] other)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        digest.AppendData(Encoding.ASCII.GetBytes(label));
        digest.AppendData(new byte[] { 0x00 });
        AppendSorted(digest, one, other);

        return digest.GetHashAndReset();
    }

    /// <summary>
    /// The two encodings in ascending byte order, concatenated with nothing between them.
    /// </summary>
    /// <param name="digest">The digest to append to.</param>
    /// <param name="one">One public key encoding.</param>
    /// <param name="other">The other.</param>
    private static void AppendSorted(IncrementalHash digest, byte[] one, byte[] other)
    {
        var oneSortsFirst = one.AsSpan().SequenceCompareTo(other) <= 0;

        digest.AppendData(oneSortsFirst ? one : other);
        digest.AppendData(oneSortsFirst ? other : one);
    }

    private static string Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(bytes).ToLowerInvariant();
}
