using System;
using System.Linq;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What this server is already paired with, read out of the pairing record store.
/// </summary>
/// <remarks>
/// This is the implementation <see cref="IPairedPeers"/> was declared without.
/// <see cref="EnrolmentWindow"/> was built and proved against fixtures inside the test project,
/// so the fifth property of the enrolment bounds - a window refuses to open against a peer this
/// server is already paired with - held in the suite and held nowhere on a server, because no
/// type a container could build answered the question at all.
/// <para>
/// It answers by address and never by identifier, which is the whole reason
/// <see cref="IPairedPeers"/> exists as its own question. A derived identifier comes from both
/// public keys, so a peer offering a different key produces a different identifier and a lookup
/// by identifier would not find the pairing that is already held - which is exactly the
/// displacement the rule is written against.
/// </para>
/// <para>
/// A RECORD IN <see cref="PairingState.Offered"/> IS NOT A PAIRING TO DISPLACE, AND IT IS THE
/// ONE STATE THIS SKIPS. Every other state a record survives in answers true, revoked included,
/// for the reason <see cref="IPairedPeers.HasPairingWith"/> gives. <c>Offered</c> is different in
/// kind: it is this server's own half-open window rather than a relationship with a peer, no peer
/// key has ever arrived under it, and <see cref="EnrolmentWindow"/> already has an answer for a
/// second window against the same address - <see cref="WindowOpening.AlreadyOpen"/>. Answering
/// true here would replace that answer with <see cref="WindowOpening.AlreadyPaired"/> and tell an
/// operator who opened a window twice that they are paired with a server they have never reached.
/// </para>
/// <para>
/// A RECORD CARRYING NO ADDRESS MATCHES NOTHING. That is what a record written before format 2 of
/// the store looks like, and treating an absent address as a match would refuse every window on
/// the strength of a record that does not say which peer it is for.
/// </para>
/// <para>
/// Every call reads the store, so a record written since the last call is seen, and nothing here
/// is cached to go stale against a file that moved. That is the trade the store itself takes, at
/// the same price of a read per pairing, and the population is the pairings one server holds.
/// </para>
/// </remarks>
public sealed class RecordedPeers : IPairedPeers
{
    private readonly IPairingRecordStore _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="RecordedPeers"/> class.
    /// </summary>
    /// <param name="records">Where the pairing records are kept.</param>
    /// <exception cref="ArgumentNullException">The store is null.</exception>
    public RecordedPeers(IPairingRecordStore records)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
    }

    /// <inheritdoc />
    public bool HasPairingWith(PeerAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        return Held(_records, address).Any();
    }

    /// <summary>
    /// The identifiers of the records held for an address in a state, in no particular order.
    /// </summary>
    /// <param name="records">Where the pairing records are kept.</param>
    /// <param name="address">The address to look under.</param>
    /// <param name="state">The state to look for.</param>
    /// <returns>The identifiers.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A static beside the instance question rather than a second member of
    /// <see cref="IPairedPeers"/>. That interface answers one question and returns no record, no
    /// key material and no count, which is what makes it safe to hand to
    /// <see cref="EnrolmentWindow"/>; widening it so that <see cref="Enrolment"/> can find the
    /// identifier it wrote would hand the window a walk over the store it has no business making.
    /// <para>
    /// It is the same read as the instance question with the state named, so the two cannot
    /// disagree about what an address matches - a record with no address matches nothing here as
    /// well, and for the same reason.
    /// </para>
    /// </remarks>
    public static System.Collections.Generic.IReadOnlyList<string> Under(
        IPairingRecordStore records,
        PeerAddress address,
        PairingState state)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(address);

        return records.Pairings()
            .Select(records.Read)
            .OfType<PairingRecord>()
            .Where(record => record.State == state && Matches(record, address))
            .Select(record => record.PairingId)
            .ToList();
    }

    private static System.Collections.Generic.IEnumerable<PairingRecord> Held(
        IPairingRecordStore records,
        PeerAddress address)
        => records.Pairings()
            .Select(records.Read)
            .OfType<PairingRecord>()
            .Where(record => record.State != PairingState.Offered && Matches(record, address));

    private static bool Matches(PairingRecord record, PeerAddress address)
        => record.PeerAddress is not null
            && string.Equals(record.PeerAddress, address.Value, StringComparison.Ordinal);
}
