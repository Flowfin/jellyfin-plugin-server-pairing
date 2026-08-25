using System;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// What serving one request on the pairing plane produced.
/// </summary>
/// <remarks>
/// <paramref name="VerifiedBody"/> is what makes the ordering readable rather than assumed. A
/// request whose signature did not verify never has its body handed on, so the value is empty
/// and <see cref="BodyWasHandedOn"/> is false; the body of a request that did verify is
/// handed on whole, for whichever message handler eventually reads it. Nothing parses one
/// today.
/// </remarks>
/// <param name="Code">The refusal the caller receives.</param>
/// <param name="BodyWasHandedOn">Whether the body was passed on past verification.</param>
/// <param name="VerifiedBody">The body of a request that verified, empty otherwise.</param>
public readonly record struct PeerPlaneOutcome(
    RefusalCode Code,
    bool BodyWasHandedOn,
    ReadOnlyMemory<byte> VerifiedBody);
