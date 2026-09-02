using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// A pairing record store held in memory, for the suite.
/// </summary>
/// <remarks>
/// This one is shared rather than nested privately, because the cases that drive it live on
/// both planes: the administrative read of the open enrolment windows asks it what is held and
/// the protocol cases ask it what a transition wrote. A copy per file would let the two drift
/// apart, and what the read is judged against is the shape the state machine writes.
/// <para>
/// The doubles nested inside the mapping and state machine files are not replaced by it. Those
/// carry counters their own cases assert on, and merging them into one would either put every
/// counter here or take one away from a case that reads it.
/// </para>
/// <para>
/// A store that answers a read from something other than what a write put there proves nothing
/// about a caller, so this holds records exactly as given and answers <see cref="Pairings"/>
/// from the same dictionary rather than from a second list somebody has to remember to update.
/// </para>
/// </remarks>
internal sealed class InMemoryPairingRecords : IPairingRecordStore
{
    private readonly Dictionary<string, PairingRecord> _held =
        new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

    /// <inheritdoc />
    public IReadOnlyList<string> Pairings() => new List<string>(_held.Keys);

    /// <inheritdoc />
    public PairingRecord? Read(string pairingId)
        => _held.TryGetValue(pairingId, out var record) ? record : null;

    /// <inheritdoc />
    public void Write(PairingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        _held[record.PairingId] = record;
    }

    /// <inheritdoc />
    public void Delete(string pairingId) => _held.Remove(pairingId);
}
