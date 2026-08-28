using System;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// The key source of a server that reads the keys it holds.
/// </summary>
/// <remarks>
/// This is the join between the two halves that were built separately: the store keeps a
/// pairing's key material, the request path asks a source for it, and until this type existed
/// nothing connected them, so a server refused callers it could have authenticated.
/// <para>
/// Both keys, never only the current one. <see cref="IPairingKeyStore.Live"/> answers what this
/// side signs with, and a source built on it would refuse a peer that is still signing under
/// the key a rotation just replaced - which is the whole thing the overlap exists to carry.
/// <see cref="IPairingKeyStore.Both"/> is the read that can express it, and the store has
/// already dropped a superseded key whose overlap ran out by the instant it was asked about.
/// </para>
/// <para>
/// A PAIRING THIS STORE DOES NOT HOLD IS ANSWERED, NOT SHORT-CIRCUITED. The property
/// <see cref="IPairingKeySource"/> asks for is that an unknown identifier and a known one cost
/// the same, and a store-backed source has to keep it rather than inherit it:
/// <see cref="AcceptedKeys.None"/> goes back to <see cref="RequestAuthenticator"/>, which
/// judges it against keys drawn once per receiver and reaches its comparison the same way.
/// WHAT IS NOT CLAIMED IS THE LOOKUP ITSELF. The file store reads and parses its whole file
/// before it looks an identifier up, so the work either answer costs is dominated by a read
/// that happens either way; the difference that is left is a dictionary hit against a miss and
/// it has not been measured on this or any machine.
/// </para>
/// </remarks>
public sealed class StoreBackedKeys : IPairingKeySource
{
    private readonly IPairingKeyStore _store;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreBackedKeys"/> class.
    /// </summary>
    /// <param name="store">Where this server keeps the keys it holds.</param>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    public StoreBackedKeys(IPairingKeyStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    /// <inheritdoc />
    public AcceptedKeys ArrivingKeys(string pairingId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        var held = _store.Both(pairingId, at);

        if (held is null)
        {
            return AcceptedKeys.None;
        }

        return new AcceptedKeys(
            held.Current.Span.ToArray(),
            held.Superseded is null ? default : held.Superseded.Span.ToArray());
    }
}
