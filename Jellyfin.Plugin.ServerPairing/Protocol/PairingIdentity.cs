using System;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The two values two public keys derive: the identifier a pairing is held under, and the
/// fingerprint the two operators compare.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> fixes both constructions and is the authority; this type is that
/// section expressed in code, and a difference between the two is a defect in this file.
/// <para>
/// NEITHER SIDE CHOOSES EITHER VALUE, and that is the whole of why they are derived rather
/// than negotiated. The material is the two <c>SubjectPublicKeyInfo</c> encodings sorted as
/// byte strings and concatenated, so both servers reach the same two values without either of
/// them sending one and without either of them being able to steer the other's.
/// </para>
/// <para>
/// The two labels are what stops one value standing in for the other over identical material,
/// and the zero byte after each label is what stops a label running into the material it
/// prefixes. Both are in the digest rather than in a comment: remove either and the values
/// this type produces move.
/// </para>
/// <para>
/// WHAT THIS TYPE DOES NOT DO IS READ A KEY. It hashes the bytes it is handed, so bytes that
/// are not a public key produce a value just as readily as bytes that are. Whether the
/// material is a key is the caller's question, and no caller exists yet: the ceremony is issue
/// #19.
/// </para>
/// </remarks>
public static class PairingIdentity
{
    /// <summary>
    /// The context label the pairing identifier is derived under.
    /// </summary>
    public const string IdentifierLabel = "jellyfin-server-pairing/id";

    /// <summary>
    /// The context label the fingerprint is derived under.
    /// </summary>
    public const string FingerprintLabel = "jellyfin-server-pairing/fingerprint";

    /// <summary>
    /// How many bytes of the fingerprint digest an operator is shown, which is 128 bits.
    /// </summary>
    /// <remarks>
    /// Why this many is argued in <c>docs/crypto.md</c> in both directions, and neither
    /// argument is that the digest is weak: fewer invites a second preimage, and more invites
    /// an operator who compares the first group and the last and stops.
    /// </remarks>
    public const int BytesAnOperatorCompares = 16;

    /// <summary>
    /// The byte a label is separated from the material by.
    /// </summary>
    /// <remarks>
    /// A label is a fixed ASCII string and the material is a key encoding, so without this
    /// byte a label ending where material begins would be one run of bytes and two different
    /// splits of it would digest identically. Neither label in use today can produce such a
    /// pair; the byte is here because the construction <c>docs/protocol.md</c> fixes carries
    /// it, and the case that needs it is a label added later.
    /// </remarks>
    private const byte LabelTerminator = 0x00;

    /// <summary>
    /// The identifier the two keys derive, which is the value a pairing is held under.
    /// </summary>
    /// <param name="aPublicKey">One side's public key, in its DER <c>SubjectPublicKeyInfo</c> encoding.</param>
    /// <param name="anotherPublicKey">The other side's, in the same encoding.</param>
    /// <returns>32 lowercase hex characters, which is the shape the field table fixes.</returns>
    public static string IdentifierFor(ReadOnlySpan<byte> aPublicKey, ReadOnlySpan<byte> anotherPublicKey)
        => Hex(Digest(IdentifierLabel, aPublicKey, anotherPublicKey).AsSpan(0, BytesAnOperatorCompares));

    /// <summary>
    /// The fingerprint digest over the same material, under the other label.
    /// </summary>
    /// <param name="aPublicKey">One side's public key, in its DER <c>SubjectPublicKeyInfo</c> encoding.</param>
    /// <param name="anotherPublicKey">The other side's, in the same encoding.</param>
    /// <returns>64 lowercase hex characters, which is the whole digest.</returns>
    /// <remarks>
    /// THE WHOLE DIGEST RATHER THAN THE PART AN OPERATOR READS, and the two are different
    /// values with different readers. The field table fixes a fingerprint digest at exactly 64
    /// lowercase hex characters, and that is what a <c>confirm</c> request carries between two
    /// servers. <see cref="ShownToAnOperator"/> is what a person compares by eye.
    /// </remarks>
    public static string FingerprintDigestFor(ReadOnlySpan<byte> aPublicKey, ReadOnlySpan<byte> anotherPublicKey)
        => Hex(Digest(FingerprintLabel, aPublicKey, anotherPublicKey));

    /// <summary>
    /// The leading 128 bits of a fingerprint digest, which is what an operator reads.
    /// </summary>
    /// <param name="fingerprintDigest">A digest from <see cref="FingerprintDigestFor"/>.</param>
    /// <returns>32 lowercase hex characters.</returns>
    /// <exception cref="ArgumentNullException">The digest is null.</exception>
    /// <exception cref="ArgumentException">The digest is not 64 lowercase hex characters.</exception>
    /// <remarks>
    /// It refuses anything that is not a whole digest rather than taking the first 32
    /// characters of whatever it is handed. A truncation is the one operation here whose wrong
    /// answer still looks exactly like a right one, and an operator comparing two values by eye
    /// has no way to tell a short digest from a correct one.
    /// <para>
    /// WHAT IS NOT HERE IS THE GROUPING. <c>docs/crypto.md</c> says an operator reads these
    /// characters in groups of four and that the grouping is part of the pinned construction,
    /// and no document fixes what separates one group from the next. Choosing one here would be
    /// taking a decision inside a type that owes none, so this returns the characters, and the
    /// gap is written into issue #19 rather than closed by a guess.
    /// </para>
    /// </remarks>
    public static string ShownToAnOperator(string fingerprintDigest)
    {
        ArgumentNullException.ThrowIfNull(fingerprintDigest);

        if (fingerprintDigest.Length != (SHA256.HashSizeInBytes * 2) || !IsLowercaseHex(fingerprintDigest))
        {
            throw new ArgumentException(
                "A fingerprint digest is 64 lowercase hex characters.",
                nameof(fingerprintDigest));
        }

        return fingerprintDigest.Substring(0, BytesAnOperatorCompares * 2);
    }

    /// <summary>
    /// The digest of one label over the material the two keys make.
    /// </summary>
    /// <param name="label">The context label.</param>
    /// <param name="aPublicKey">One key.</param>
    /// <param name="anotherPublicKey">The other.</param>
    /// <returns>The whole SHA-256 digest.</returns>
    private static byte[] Digest(string label, ReadOnlySpan<byte> aPublicKey, ReadOnlySpan<byte> anotherPublicKey)
    {
        // Ascending byte order, so the two servers agree on the material without negotiating it
        // and without either of them choosing. Equal keys sort to themselves and produce the
        // material twice over, which is what the construction says rather than a case.
        var aSortsFirst = aPublicKey.SequenceCompareTo(anotherPublicKey) <= 0;
        var first = aSortsFirst ? aPublicKey : anotherPublicKey;
        var second = aSortsFirst ? anotherPublicKey : aPublicKey;

        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        digest.AppendData(Encoding.ASCII.GetBytes(label));
        digest.AppendData(new[] { LabelTerminator });
        digest.AppendData(first);
        digest.AppendData(second);

        return digest.GetHashAndReset();
    }

    /// <summary>
    /// Lowercase hex, which is the one rendering every value in this protocol uses.
    /// </summary>
    /// <param name="bytes">The bytes to render.</param>
    /// <returns>Two characters per byte.</returns>
    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    /// <summary>
    /// Whether every character is a lowercase hex digit.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns>True where it is.</returns>
    private static bool IsLowercaseHex(string value)
    {
        foreach (var c in value)
        {
            var isDigit = c >= '0' && c <= '9';
            var isLowerHexLetter = c >= 'a' && c <= 'f';

            if (!isDigit && !isLowerHexLetter)
            {
                return false;
            }
        }

        return true;
    }
}
