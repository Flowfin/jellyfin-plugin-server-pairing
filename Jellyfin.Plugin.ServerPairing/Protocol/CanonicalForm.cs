using System;
using System.Security.Cryptography;
using System.Text;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The exact bytes a signature covers.
/// </summary>
/// <remarks>
/// Eight lines for a request, US-ASCII, each ending in one line feed and none containing a
/// carriage return. The order and the contents are <c>docs/protocol.md</c>, which is the
/// authority; this type is that document expressed in code and a difference between the two
/// is a defect in this file.
/// <para>
/// Nothing about the request as it appears on the wire is covered. Header case, header order,
/// header folding and whitespace are all things an intermediary is allowed to change, so the
/// values travel in headers and the signature covers them as written into the lines below.
/// </para>
/// </remarks>
public static class CanonicalForm
{
    /// <summary>
    /// Line 1 of a request's canonical form. It separates a signature made here from every
    /// other use of the same key, so one cannot be replayed into another construction.
    /// </summary>
    public const string RequestLabel = "jellyfin-server-pairing/request";

    /// <summary>
    /// Line 1 of a response's canonical form.
    /// </summary>
    public const string ResponseLabel = "jellyfin-server-pairing/response";

    /// <summary>
    /// The line separator. One line feed, and never a carriage return.
    /// </summary>
    private const char Separator = '\n';

    /// <summary>
    /// Builds the canonical bytes for a request.
    /// </summary>
    /// <param name="request">The request to build them for.</param>
    /// <returns>The bytes a signature over this request covers.</returns>
    /// <exception cref="ArgumentNullException">The request is null.</exception>
    public static byte[] ForRequest(PairingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var text = new StringBuilder()
            .Append(RequestLabel).Append(Separator)
            .Append(request.Version).Append(Separator)
            .Append(request.Method).Append(Separator)
            .Append(request.Path).Append(Separator)
            .Append(request.PairingId).Append(Separator)
            .Append(request.Timestamp).Append(Separator)
            .Append(request.Nonce).Append(Separator)
            .Append(DigestOf(request.Body.Span)).Append(Separator)
            .ToString();

        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>
    /// Builds the canonical bytes for a response.
    /// </summary>
    /// <param name="version">The protocol version of the pairing.</param>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="requestNonce">The nonce of the request being answered.</param>
    /// <param name="timestamp">The timestamp on the response.</param>
    /// <param name="body">The response body bytes.</param>
    /// <returns>The bytes a signature over this response covers.</returns>
    public static byte[] ForResponse(
        string version,
        string pairingId,
        string requestNonce,
        string timestamp,
        ReadOnlyMemory<byte> body)
    {
        var text = new StringBuilder()
            .Append(ResponseLabel).Append(Separator)
            .Append(version).Append(Separator)
            .Append(pairingId).Append(Separator)
            .Append(requestNonce).Append(Separator)
            .Append(timestamp).Append(Separator)
            .Append(DigestOf(body.Span)).Append(Separator)
            .ToString();

        return Encoding.ASCII.GetBytes(text);
    }

    /// <summary>
    /// The lowercase hex digest of a body, which is what the last line covers. A body is
    /// covered by its digest rather than by inclusion, so the signed material has a fixed
    /// length whatever the body is, and an empty body has the digest of the empty string
    /// rather than a special case.
    /// </summary>
    /// <param name="body">The bytes to digest.</param>
    /// <returns>64 lowercase hex characters.</returns>
    private static string DigestOf(ReadOnlySpan<byte> body)
        => Convert.ToHexString(SHA256.HashData(body)).ToLowerInvariant();
}
