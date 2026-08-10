using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The limit on each field the signature covers, checked before anything is computed.
/// </summary>
/// <remarks>
/// A request missing an authenticated field, or carrying one outside its limit, is refused
/// before a signature is computed over it, so a caller cannot make this server do work by
/// sending nonsense. The limits are <c>docs/protocol.md</c> and this type is that table
/// expressed in code.
/// </remarks>
public static class FieldShape
{
    /// <summary>
    /// The length of a pairing identifier and of a nonce, both 16 bytes as lowercase hex.
    /// </summary>
    public const int HexFieldLength = 32;

    /// <summary>
    /// The most digits a protocol version may carry.
    /// </summary>
    public const int VersionDigitLimit = 4;

    /// <summary>
    /// The most digits a timestamp may carry.
    /// </summary>
    public const int TimestampDigitLimit = 20;

    /// <summary>
    /// Whether every covered field of a request is present and inside its limit.
    /// </summary>
    /// <param name="request">The request to judge.</param>
    /// <returns>True where the request is worth computing a signature over.</returns>
    public static bool IsWellFormed(PairingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return IsMethod(request.Method)
            && IsPath(request.Path)
            && IsHexField(request.PairingId)
            && IsUnsignedInteger(request.Version, VersionDigitLimit)
            && IsUnsignedInteger(request.Timestamp, TimestampDigitLimit)
            && IsHexField(request.Nonce);
    }

    /// <summary>
    /// A method is uppercase ASCII letters and nothing else.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns>True where it is a method.</returns>
    public static bool IsMethod(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < 'A' || c > 'Z')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A path starts with a slash, carries no query string, no fragment, no percent-encoded
    /// byte and no trailing slash. A path outside that is refused rather than normalised,
    /// because two implementations that disagree about normalisation interoperate right up
    /// until they do not.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns>True where it is a path this protocol accepts.</returns>
    public static bool IsPath(string? value)
    {
        if (string.IsNullOrEmpty(value) || value[0] != '/' || value.Length == 1)
        {
            return false;
        }

        if (value[^1] == '/')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c == '?' || c == '#' || c == '%' || c <= ' ' || c > '~')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// A hex field is exactly 32 lowercase hex characters.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <returns>True where it is one.</returns>
    public static bool IsHexField(string? value)
    {
        if (value is null || value.Length != HexFieldLength)
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

    /// <summary>
    /// An unsigned decimal integer with no sign, no padding and no leading zero, inside a
    /// digit limit.
    /// </summary>
    /// <param name="value">The value to judge.</param>
    /// <param name="digitLimit">The most digits it may carry.</param>
    /// <returns>True where it is one.</returns>
    public static bool IsUnsignedInteger(string? value, int digitLimit)
    {
        if (string.IsNullOrEmpty(value) || value.Length > digitLimit)
        {
            return false;
        }

        if (value.Length > 1 && value[0] == '0')
        {
            return false;
        }

        foreach (var c in value)
        {
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }
}
