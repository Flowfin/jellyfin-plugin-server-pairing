using System;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The one shape every refusal on the pairing plane takes.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> fixes it: HTTP 403 with a body of exactly one JSON object carrying
/// exactly one member. The bytes are built here rather than serialised from a type, because a
/// serialiser is free to reorder members, to write a different escape for the same character
/// and to be configured differently by a host, and the shape never varying is the whole
/// property. There is one member and one value, so there is nothing a serialiser would buy.
/// </remarks>
public static class Refusal
{
    /// <summary>
    /// The HTTP status every refusal on this plane carries.
    /// </summary>
    public const int Status = 403;

    /// <summary>
    /// The wire spelling of a refusal code.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The value the <c>code</c> member carries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The code is not one of the defined values.</exception>
    public static string Wire(RefusalCode code) => code switch
    {
        RefusalCode.Refused => "refused",
        RefusalCode.Clock => "clock",
        RefusalCode.Version => "version",
        RefusalCode.State => "state",
        RefusalCode.Malformed => "malformed",
        RefusalCode.Replay => "replay",
        RefusalCode.Busy => "busy",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    /// <summary>
    /// The whole body of a refusal, as the bytes that go on the wire.
    /// </summary>
    /// <param name="code">The code the body carries.</param>
    /// <returns>The body bytes.</returns>
    public static string Body(RefusalCode code) => "{\"code\":\"" + Wire(code) + "\"}";
}
