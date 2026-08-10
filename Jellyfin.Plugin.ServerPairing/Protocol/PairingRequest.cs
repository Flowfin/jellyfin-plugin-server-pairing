using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// A request on the pairing plane, as the fields the signature covers plus the headers it
/// does not.
/// </summary>
/// <remarks>
/// This is a description of a request rather than a request. It carries no stream, no
/// connection and no parsed body, because everything that authenticates a request has to
/// happen before anything richer than bytes exists. The field list and the limits on each
/// field are <c>docs/protocol.md</c>, which is the authority for both.
/// </remarks>
public sealed class PairingRequest
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PairingRequest"/> class.
    /// </summary>
    /// <param name="method">The request method, uppercase.</param>
    /// <param name="path">The request path, exact, with no query string.</param>
    /// <param name="pairingId">The pairing identifier, 32 lowercase hex characters.</param>
    /// <param name="version">The protocol version, an unsigned decimal integer.</param>
    /// <param name="timestamp">Seconds since the Unix epoch, an unsigned decimal integer.</param>
    /// <param name="nonce">The nonce, 32 lowercase hex characters.</param>
    /// <param name="body">The request body bytes, empty where there is no body.</param>
    /// <param name="uncoveredHeaders">
    /// Headers that travel with the request and are not covered by the signature. They are
    /// held here so that a test can prove they are not covered, rather than proving it by
    /// their absence.
    /// </param>
    public PairingRequest(
        string method,
        string path,
        string pairingId,
        string version,
        string timestamp,
        string nonce,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string>? uncoveredHeaders = null)
    {
        Method = method;
        Path = path;
        PairingId = pairingId;
        Version = version;
        Timestamp = timestamp;
        Nonce = nonce;
        Body = body;
        UncoveredHeaders = uncoveredHeaders ?? new Dictionary<string, string>(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the request method, uppercase. Line 3 of the canonical form.
    /// </summary>
    public string Method { get; }

    /// <summary>
    /// Gets the request path. Line 4 of the canonical form.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the pairing identifier. Line 5 of the canonical form.
    /// </summary>
    public string PairingId { get; }

    /// <summary>
    /// Gets the protocol version. Line 2 of the canonical form.
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the timestamp. Line 6 of the canonical form.
    /// </summary>
    public string Timestamp { get; }

    /// <summary>
    /// Gets the nonce. Line 7 of the canonical form.
    /// </summary>
    public string Nonce { get; }

    /// <summary>
    /// Gets the body bytes. Line 8 of the canonical form covers them by digest.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>
    /// Gets the headers the signature does not cover.
    /// </summary>
    public IReadOnlyDictionary<string, string> UncoveredHeaders { get; }

    /// <summary>
    /// Returns this request with one field replaced, for a caller that needs to build a
    /// neighbour of it.
    /// </summary>
    /// <param name="method">The method, or null to keep this one.</param>
    /// <param name="path">The path, or null to keep this one.</param>
    /// <param name="pairingId">The pairing identifier, or null to keep this one.</param>
    /// <param name="version">The version, or null to keep this one.</param>
    /// <param name="timestamp">The timestamp, or null to keep this one.</param>
    /// <param name="nonce">The nonce, or null to keep this one.</param>
    /// <param name="body">The body, or null to keep this one.</param>
    /// <param name="uncoveredHeaders">The uncovered headers, or null to keep these.</param>
    /// <returns>A new request.</returns>
    public PairingRequest With(
        string? method = null,
        string? path = null,
        string? pairingId = null,
        string? version = null,
        string? timestamp = null,
        string? nonce = null,
        ReadOnlyMemory<byte>? body = null,
        IReadOnlyDictionary<string, string>? uncoveredHeaders = null)
        => new PairingRequest(
            method ?? Method,
            path ?? Path,
            pairingId ?? PairingId,
            version ?? Version,
            timestamp ?? Timestamp,
            nonce ?? Nonce,
            body ?? Body,
            uncoveredHeaders ?? UncoveredHeaders);
}
