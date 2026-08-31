namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// The three values one side put on the wire that a surface may never carry back.
/// </summary>
/// <remarks>
/// The nonce and the signature are on the list <c>docs/logging.md</c> says may never be
/// written at any level, and a case asserting a surface does not carry them has to know what
/// this run produced rather than searching for a constant. The timestamp is not on that list
/// and is here because the three are what identifies one send when a case has made several.
/// </remarks>
/// <param name="Nonce">The nonce this send carried.</param>
/// <param name="Signature">The signature this send carried, as base64.</param>
/// <param name="Timestamp">The sender's instant, as it went on the wire.</param>
internal readonly record struct SentMessage(string Nonce, string Signature, string Timestamp);
