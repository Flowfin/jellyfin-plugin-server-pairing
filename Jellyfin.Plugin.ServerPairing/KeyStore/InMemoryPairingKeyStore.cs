using System;
using System.Collections.Generic;
using System.Threading;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// A key store that keeps nothing between one run and the next.
/// </summary>
/// <remarks>
/// This is the store the suite drives, and it exists so that everything above the store can be
/// proved without a filesystem. It is the authority for what the interface means: the file
/// implementation is judged against the same cases, so a difference between the two shows up
/// as a case that passes on one and fails on the other rather than as a surprise on a server.
/// <para>
/// It is not registered with the container. A server that forgot its keys on every restart
/// would lose every pairing on every upgrade, so what the plugin registers is the file
/// implementation and this one is reached only by a test.
/// </para>
/// </remarks>
public sealed class InMemoryPairingKeyStore : IPairingKeyStore
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<string, PairingKeys> _held = new Dictionary<string, PairingKeys>(StringComparer.Ordinal);

    /// <inheritdoc />
    public KeyMaterial? Live(string pairingId, DateTimeOffset at) => Both(pairingId, at)?.Current;

    /// <inheritdoc />
    public PairingKeys? Both(string pairingId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            return _held.TryGetValue(pairingId, out var keys) ? PairingKeyOverlap.AsOf(keys, at) : null;
        }
    }

    /// <inheritdoc />
    public void Add(string pairingId, KeyMaterial current)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(current);

        lock (_gate)
        {
            if (_held.ContainsKey(pairingId))
            {
                throw new InvalidOperationException(
                    "A key is already held for this pairing, and replacing one is a rotation rather than an add.");
            }

            _held[pairingId] = new PairingKeys(pairingId, current, null, default);
        }
    }

    /// <inheritdoc />
    public void Replace(string pairingId, KeyMaterial replacement, DateTimeOffset supersededStopsAt)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            if (!_held.TryGetValue(pairingId, out var held))
            {
                throw new InvalidOperationException(
                    "There is no key held for this pairing, so there is nothing for a replacement to supersede.");
            }

            _held[pairingId] = new PairingKeys(pairingId, replacement, held.Current, supersededStopsAt);
        }
    }

    /// <inheritdoc />
    public void Destroy(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            if (_held.Remove(pairingId, out var gone))
            {
                gone.Current.Destroy();
                gone.Superseded?.Destroy();
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Pairings()
    {
        lock (_gate)
        {
            return new List<string>(_held.Keys);
        }
    }
}
