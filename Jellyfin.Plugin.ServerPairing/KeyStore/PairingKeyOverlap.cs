using System;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// What a stored overlap looks like at an instant.
/// </summary>
/// <remarks>
/// One implementation of this rule rather than one per store. Both stores read the same
/// persisted three values, and a superseded key whose overlap has run out is not a key any
/// more, so deciding that in each store separately is two places for it to drift.
/// </remarks>
internal static class PairingKeyOverlap
{
    /// <summary>
    /// The keys as they stand at an instant, with a superseded key dropped once its overlap
    /// has run out.
    /// </summary>
    /// <param name="held">What the store holds.</param>
    /// <param name="at">The instant this is judged at.</param>
    /// <returns>The keys, with the superseded slot empty where the overlap has ended.</returns>
    /// <remarks>
    /// The boundary is the instant itself. The rotation section of <c>docs/protocol.md</c>
    /// fixes the superseded key as verifying until it stops, so the instant it stops is the
    /// first instant at which it does not, and a key exactly at its own end is gone.
    /// </remarks>
    public static PairingKeys AsOf(PairingKeys held, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(held);

        if (held.Superseded is null || at < held.SupersededStopsAt)
        {
            return held;
        }

        return new PairingKeys(held.PairingId, held.Current, null, default);
    }
}
