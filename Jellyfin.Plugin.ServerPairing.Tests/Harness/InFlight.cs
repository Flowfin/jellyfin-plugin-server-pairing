using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// A message between the two sides, at the one point a case can reach it.
/// </summary>
/// <remarks>
/// This is the wire and nothing richer. The path, the headers and the body bytes are what one
/// side put on the request and what the other side will be given, so a case that changes
/// something here has changed what arrived rather than what either side believes about it.
/// </remarks>
internal sealed class InFlight
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InFlight"/> class.
    /// </summary>
    /// <param name="path">The request target, exactly as it goes on the request line.</param>
    /// <param name="headers">The headers, by name.</param>
    /// <param name="body">The body bytes.</param>
    public InFlight(string path, IReadOnlyDictionary<string, string> headers, ReadOnlyMemory<byte> body)
    {
        Path = path;
        Headers = headers;
        Body = body;
    }

    /// <summary>
    /// Gets the request target, exactly as it goes on the request line.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the headers, by name.
    /// </summary>
    public IReadOnlyDictionary<string, string> Headers { get; }

    /// <summary>
    /// Gets the body bytes.
    /// </summary>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>
    /// The same message with one part replaced.
    /// </summary>
    /// <param name="path">The target, or null to keep this one.</param>
    /// <param name="headers">The headers, or null to keep these.</param>
    /// <param name="body">The body, or null to keep this one.</param>
    /// <returns>The message as it would then arrive.</returns>
    public InFlight With(
        string? path = null,
        IReadOnlyDictionary<string, string>? headers = null,
        ReadOnlyMemory<byte>? body = null)
        => new InFlight(path ?? Path, headers ?? Headers, body ?? Body);

    /// <summary>
    /// The same message with one header value replaced.
    /// </summary>
    /// <param name="name">The header to replace.</param>
    /// <param name="value">The value it arrives with.</param>
    /// <returns>The message as it would then arrive.</returns>
    public InFlight WithHeader(string name, string value)
    {
        var headers = new Dictionary<string, string>(Headers, StringComparer.Ordinal)
        {
            [name] = value,
        };

        return With(headers: headers);
    }
}
