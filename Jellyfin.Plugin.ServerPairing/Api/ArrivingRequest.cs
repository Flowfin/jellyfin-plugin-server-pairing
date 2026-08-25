using System;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// A request as it arrived on the pairing plane, before anything has judged it.
/// </summary>
/// <remarks>
/// Every member is nullable or already bounded, because this type holds what a stranger sent
/// rather than what this server would accept. The path is the raw target as it was written on
/// the request line, not the routed path: routing normalises a trailing slash, decodes a
/// percent-encoded byte and matches without regard to case, and each of those is a difference
/// this protocol refuses rather than absorbs.
/// </remarks>
public sealed class ArrivingRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ArrivingRequest"/> class.
    /// </summary>
    /// <param name="rawTarget">The request target exactly as it was sent, or null where the server could not supply it.</param>
    /// <param name="method">The request method as it was sent.</param>
    /// <param name="pairingId">The value of <c>X-Pairing-Id</c>.</param>
    /// <param name="version">The value of <c>X-Pairing-Version</c>.</param>
    /// <param name="timestamp">The value of <c>X-Pairing-Timestamp</c>.</param>
    /// <param name="nonce">The value of <c>X-Pairing-Nonce</c>.</param>
    /// <param name="signature">The value of <c>X-Pairing-Signature</c>.</param>
    /// <param name="body">The body bytes that were read, which is never more than the limit for the message plus one.</param>
    /// <param name="bodyExceededItsLimit">Whether the reader stopped because the body was longer than the limit for its message type.</param>
    public ArrivingRequest(
        string? rawTarget,
        string? method,
        string? pairingId,
        string? version,
        string? timestamp,
        string? nonce,
        string? signature,
        ReadOnlyMemory<byte> body,
        bool bodyExceededItsLimit)
    {
        RawTarget = rawTarget;
        Method = method;
        PairingId = pairingId;
        Version = version;
        Timestamp = timestamp;
        Nonce = nonce;
        Signature = signature;
        Body = body;
        BodyExceededItsLimit = bodyExceededItsLimit;
    }

    /// <summary>
    /// Gets the request target exactly as it was sent.
    /// </summary>
    public string? RawTarget { get; }

    /// <summary>
    /// Gets the request method as it was sent.
    /// </summary>
    public string? Method { get; }

    /// <summary>
    /// Gets the value of <c>X-Pairing-Id</c>.
    /// </summary>
    public string? PairingId { get; }

    /// <summary>
    /// Gets the value of <c>X-Pairing-Version</c>.
    /// </summary>
    public string? Version { get; }

    /// <summary>
    /// Gets the value of <c>X-Pairing-Timestamp</c>.
    /// </summary>
    public string? Timestamp { get; }

    /// <summary>
    /// Gets the value of <c>X-Pairing-Nonce</c>.
    /// </summary>
    public string? Nonce { get; }

    /// <summary>
    /// Gets the value of <c>X-Pairing-Signature</c>.
    /// </summary>
    public string? Signature { get; }

    /// <summary>
    /// Gets the body bytes that were read.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>
    /// Gets a value indicating whether the body was longer than the limit for its message type.
    /// </summary>
    public bool BodyExceededItsLimit { get; }
}
