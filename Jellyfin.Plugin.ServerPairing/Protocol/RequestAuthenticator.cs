using System;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Signs a request over the canonical form, and verifies one.
/// </summary>
/// <remarks>
/// Nothing here reads a clock, so freshness is not judged by this type. A signature that
/// verifies says the request was made by whoever holds the key and that none of the covered
/// fields moved on the way; whether it is fresh is the timestamp window and the nonce store,
/// which are issues #21 and #26. The instant this type does take is not a freshness judgement:
/// it is what the key source needs in order to say whether a superseded key is still one.
/// <para>
/// It is registered in <see cref="PluginServiceRegistrator"/> against the source that reads
/// this server's key store, so a pairing that has a key can be verified. What has never put a
/// key into that store is the enrolment, which is issue #18, so on a server today the store is
/// empty and every arriving request is refused for want of a key rather than for want of a
/// route.
/// </para>
/// </remarks>
public sealed class RequestAuthenticator
{
    /// <summary>
    /// The length of an HMAC-SHA-256 tag, which is what a signature is.
    /// </summary>
    public const int SignatureLength = 32;

    private readonly IPairingKeySource _keys;
    private readonly byte[] _keyForAnAbsentPairing;
    private readonly byte[] _keyForAnAbsentOverlap;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestAuthenticator"/> class.
    /// </summary>
    /// <param name="keys">Where the keys for a pairing come from.</param>
    public RequestAuthenticator(IPairingKeySource keys)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));

        // A request naming a pairing that does not exist has to cost what one naming a
        // pairing that does costs. So an absent pairing is answered with a key drawn here,
        // the same work is done against it, and the comparison fails. Without this, the
        // early return is a timing oracle for whether a pairing exists, which is the
        // question the refusal path exists to refuse.
        _keyForAnAbsentPairing = RandomNumberGenerator.GetBytes(SignatureLength);

        // The same argument one slot over. A pairing that is not mid-rotation has no
        // superseded key, and judging one candidate where a rotating pairing judges two would
        // say which of the two a pairing is to a caller who holds neither key. So the second
        // candidate is always computed, against this where there is nothing to compute it
        // against, and discarded.
        _keyForAnAbsentOverlap = RandomNumberGenerator.GetBytes(SignatureLength);
    }

    /// <summary>
    /// The signature for a request, as base64.
    /// </summary>
    /// <param name="request">The request to sign.</param>
    /// <param name="key">The key for the pairing and the direction this request travels.</param>
    /// <returns>The signature.</returns>
    /// <exception cref="ArgumentException">A covered field is missing or outside its limit.</exception>
    public static string Sign(PairingRequest request, ReadOnlySpan<byte> key)
    {
        if (!FieldShape.IsWellFormed(request))
        {
            throw new ArgumentException(
                "A covered field is missing or outside its limit, so there is nothing worth signing.",
                nameof(request));
        }

        return Convert.ToBase64String(HMACSHA256.HashData(key, CanonicalForm.ForRequest(request)));
    }

    /// <summary>
    /// Whether a request is authentic.
    /// </summary>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="presentedSignature">The value of the signature header, as base64.</param>
    /// <param name="at">
    /// The instant the key source is asked about, which decides whether a superseded key is
    /// still one.
    /// </param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// Both candidates are always computed, and where a slot is empty the candidate is computed
    /// against a key drawn once by this receiver and thrown away. A caller holding neither key
    /// learns nothing from how long the answer took, including whether the pairing exists and
    /// whether it is mid-rotation. That is the same shape <see cref="KeyOverlap.Verify"/> holds
    /// for a pairing kept in memory, and both reach <see cref="Matches"/> rather than computing
    /// their own tag, so there is one construction of the canonical bytes and one comparison.
    /// </remarks>
    public VerificationOutcome Verify(PairingRequest request, string? presentedSignature, DateTimeOffset at)
    {
        // Shape first, and before any key is fetched or any digest is taken. A request
        // missing a covered field cannot be signed over, and computing something over what
        // is left would be authenticating a different request from the one that arrived.
        if (request is null || !FieldShape.IsWellFormed(request) || presentedSignature is null)
        {
            return VerificationOutcome.Refused;
        }

        if (!TryDecodeSignature(presentedSignature, out var presented))
        {
            return VerificationOutcome.Refused;
        }

        var arriving = _keys.ArrivingKeys(request.PairingId, at);

        var overlapIsOpen = !arriving.Superseded.IsEmpty;
        var current = arriving.Current.IsEmpty ? _keyForAnAbsentPairing.AsSpan() : arriving.Current.Span;
        var superseded = overlapIsOpen ? arriving.Superseded.Span : _keyForAnAbsentOverlap.AsSpan();

        var onCurrent = Matches(request, presented, current);
        var onSuperseded = Matches(request, presented, superseded);

        return onCurrent || (overlapIsOpen && onSuperseded)
            ? VerificationOutcome.Verified
            : VerificationOutcome.Refused;
    }

    /// <summary>
    /// Whether a request carries this signature under this key.
    /// </summary>
    /// <param name="request">The request, already checked against its field limits.</param>
    /// <param name="presentedSignature">The decoded signature bytes.</param>
    /// <param name="key">The key to judge against.</param>
    /// <returns>True where the signature is the one this key produces over this request.</returns>
    /// <remarks>
    /// This is the one place a presented signature is compared, and it compares in fixed time.
    /// A pairing whose key is being rotated has two keys that can verify what arrives, and the
    /// overlap in <see cref="KeyOverlap"/> asks this about each of them rather than computing
    /// its own MAC, so there is one construction of the canonical bytes and one comparison
    /// rather than two of each drifting apart.
    /// </remarks>
    public static bool Matches(PairingRequest request, ReadOnlySpan<byte> presentedSignature, ReadOnlySpan<byte> key)
    {
        var computed = HMACSHA256.HashData(key, CanonicalForm.ForRequest(request));

        return CryptographicOperations.FixedTimeEquals(computed, presentedSignature);
    }

    /// <summary>
    /// Verifies a request and, only where it verified, hands the body to a reader.
    /// </summary>
    /// <typeparam name="T">What the reader produces.</typeparam>
    /// <param name="request">The request as it arrived.</param>
    /// <param name="presentedSignature">The value of the signature header.</param>
    /// <param name="at">The instant the key source is asked about.</param>
    /// <param name="readBody">The reader that turns the body into something richer than bytes.</param>
    /// <param name="value">What the reader produced, or the default where it was not called.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// The ordering is the point of this method rather than a detail of it. A request that
    /// fails verification never reaches the reader, so an unauthenticated caller never
    /// reaches the deserialiser and cannot use it as a surface.
    /// </remarks>
    public VerificationOutcome VerifyThenRead<T>(
        PairingRequest request,
        string? presentedSignature,
        DateTimeOffset at,
        Func<ReadOnlyMemory<byte>, T> readBody,
        out T? value)
    {
        ArgumentNullException.ThrowIfNull(readBody);

        var outcome = Verify(request, presentedSignature, at);

        value = outcome == VerificationOutcome.Verified ? readBody(request.Body) : default;

        return outcome;
    }

    /// <summary>
    /// Decodes a presented signature, refusing anything that is not base64 of the right
    /// length.
    /// </summary>
    /// <param name="presented">The header value.</param>
    /// <param name="bytes">The decoded bytes.</param>
    /// <returns>True where it decoded.</returns>
    public static bool TryDecodeSignature(string presented, out byte[] bytes)
    {
        bytes = new byte[SignatureLength];

        return Convert.TryFromBase64String(presented, bytes, out var written)
            && written == SignatureLength;
    }
}
