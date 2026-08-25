using System;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Everything one pairing's key state is: what it signs with now, what it still accepts, and
/// when that stops.
/// </summary>
/// <remarks>
/// All three are persisted rather than only the current key, which is the answer taken on
/// issue #30 on 2026-08-24. A restart in the middle of a rotation overlap that kept only the
/// current key would drop the superseded one, and a peer that had not yet caught up would stop
/// being understood, which is exactly the failure the overlap exists to prevent.
/// <para>
/// This is the shape <see cref="Protocol.KeyOverlap"/> holds in memory, written down so that
/// the two agree. A difference between them is a defect in whichever one was changed without
/// the other.
/// </para>
/// </remarks>
public sealed class PairingKeys
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PairingKeys"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing these keys belong to.</param>
    /// <param name="current">The key this side signs with and accepts.</param>
    /// <param name="superseded">The key a rotation replaced, or null where none is held.</param>
    /// <param name="supersededStopsAt">When the superseded key stops verifying.</param>
    /// <exception cref="ArgumentNullException">The identifier or the current key is null.</exception>
    public PairingKeys(
        string pairingId,
        KeyMaterial current,
        KeyMaterial? superseded,
        DateTimeOffset supersededStopsAt)
    {
        PairingId = pairingId ?? throw new ArgumentNullException(nameof(pairingId));
        Current = current ?? throw new ArgumentNullException(nameof(current));
        Superseded = superseded;
        SupersededStopsAt = supersededStopsAt;
    }

    /// <summary>
    /// Gets the pairing these keys belong to.
    /// </summary>
    public string PairingId { get; }

    /// <summary>
    /// Gets the key this side signs with and accepts.
    /// </summary>
    public KeyMaterial Current { get; }

    /// <summary>
    /// Gets the key a rotation replaced, or null where none is held.
    /// </summary>
    public KeyMaterial? Superseded { get; }

    /// <summary>
    /// Gets the instant the superseded key stops verifying.
    /// </summary>
    /// <remarks>
    /// Meaningless where <see cref="Superseded"/> is null, and never read in that case. A
    /// reader that consults it without checking the slot first has read a rotation that is not
    /// open.
    /// </remarks>
    public DateTimeOffset SupersededStopsAt { get; }
}
