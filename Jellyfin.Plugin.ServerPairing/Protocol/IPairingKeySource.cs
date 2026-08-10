using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Supplies the key that authenticates requests arriving from one pairing.
/// </summary>
/// <remarks>
/// The key is selected by the pairing identifier in the request and by nothing else, so an
/// implementation answers about the identifier it was given and never about the caller. The
/// store behind it is M4, and until that exists the only implementation is the one the suite
/// substitutes.
/// </remarks>
public interface IPairingKeySource
{
    /// <summary>
    /// The key that verifies requests arriving from the named pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier from the request.</param>
    /// <returns>
    /// The key, or an empty memory where no pairing with that identifier is known. An
    /// implementation answers in the same time in both cases, or the caller's own work to
    /// hide the difference is undone by the lookup in front of it.
    /// </returns>
    ReadOnlyMemory<byte> ArrivingKey(string pairingId);
}
