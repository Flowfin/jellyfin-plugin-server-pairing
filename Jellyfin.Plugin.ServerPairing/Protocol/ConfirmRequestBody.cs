using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The body of a <c>confirm</c> request, read from the bytes that arrived.
/// </summary>
/// <remarks>
/// One member, which is the fingerprint digest the operator on the sending side compared. The
/// member and its limit are <c>docs/protocol.md</c>, which is the authority for both, and the
/// limit is exact rather than an upper bound: a fingerprint digest is 64 lowercase hex
/// characters, so a value of any other length is not one.
/// <para>
/// WHAT THE DIGEST IS COMPARED AGAINST DOES NOT LIVE HERE. This type answers whether a body is a
/// <c>confirm</c> body; whether the digest is the one this side computed is the ceremony, which
/// is issue #19, and that comparison is a fixed-time one on a value <c>docs/crypto.md</c> names
/// as secret material rather than the ordinary comparison this type performs on a shape.
/// </para>
/// </remarks>
public sealed class ConfirmRequestBody
{
    /// <summary>
    /// The member carrying the fingerprint digest.
    /// </summary>
    public const string DigestMember = "digest";

    /// <summary>
    /// How many characters a fingerprint digest carries, which is a SHA-256 digest as lowercase
    /// hex.
    /// </summary>
    public const int DigestLength = 64;

    /// <summary>
    /// How many members this body has.
    /// </summary>
    private const int MemberCount = 1;

    private ConfirmRequestBody(string digest)
    {
        Digest = digest;
    }

    /// <summary>
    /// Gets the fingerprint digest, as it was written.
    /// </summary>
    public string Digest { get; }

    /// <summary>
    /// Reads a <c>confirm</c> request body.
    /// </summary>
    /// <param name="body">The body bytes, exactly as they arrived.</param>
    /// <param name="confirm">The body read, where this returns true.</param>
    /// <returns>True where the bytes are a <c>confirm</c> request body.</returns>
    public static bool TryRead(ReadOnlySpan<byte> body, out ConfirmRequestBody? confirm)
    {
        confirm = null;

        if (!BodyObject.TryRead(body, out var members)
            || members.Count != MemberCount
            || !members.TryText(DigestMember, out var digest)
            || !IsDigest(digest))
        {
            return false;
        }

        confirm = new ConfirmRequestBody(digest);
        return true;
    }

    /// <summary>
    /// Whether a value is a fingerprint digest as the field table describes one.
    /// </summary>
    /// <param name="value">The value, as it was written.</param>
    /// <returns>True where it is exactly the digest length in lowercase hex.</returns>
    private static bool IsDigest(string value)
    {
        if (value.Length != DigestLength)
        {
            return false;
        }

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
