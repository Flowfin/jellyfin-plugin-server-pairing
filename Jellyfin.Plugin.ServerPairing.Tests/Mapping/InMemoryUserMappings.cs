using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Mapping;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// A mapping store held in memory, for the suite.
/// </summary>
/// <remarks>
/// This one is shared across files rather than nested privately in each, because the state
/// machine tests and the mapping tests both drive it and the property under test is what the
/// two do to each other. Two copies would let one drift and hide exactly that.
/// <para>
/// It counts removals so a test can assert that ending a pairing reached the store, rather
/// than only that the store came back empty. A store that was already empty and one that was
/// emptied look the same from the outside, and only the second is the guard working.
/// </para>
/// </remarks>
internal sealed class InMemoryUserMappings : IUserMappingStore
{
    private readonly List<UserMapping> _held = new List<UserMapping>();

    /// <summary>
    /// Gets the number of times a whole pairing's mappings were removed.
    /// </summary>
    public int Sweeps { get; private set; }

    /// <inheritdoc />
    public IReadOnlyList<UserMapping> For(string pairingId)
        => _held.Where(m => string.Equals(m.PairingId, pairingId, StringComparison.Ordinal)).ToArray();

    /// <inheritdoc />
    public void Put(UserMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        Remove(mapping.PairingId, mapping.LocalUserId);
        _held.Add(mapping);
    }

    /// <inheritdoc />
    public void Remove(string pairingId, string localUserId)
        => _held.RemoveAll(m => string.Equals(m.PairingId, pairingId, StringComparison.Ordinal)
            && string.Equals(m.LocalUserId, localUserId, StringComparison.Ordinal));

    /// <inheritdoc />
    public void RemoveEvery(string pairingId)
    {
        Sweeps++;
        _held.RemoveAll(m => string.Equals(m.PairingId, pairingId, StringComparison.Ordinal));
    }
}
