using System;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Whether this server already has a pairing with a peer, read out of the record store.
/// </summary>
/// <remarks>
/// The question is asked by address and never by identifier, and the case that separates the two
/// is the one this file is written around: the identifier is derived from both public keys, so a
/// peer offering a different key produces a different identifier, and a reader that looked one up
/// would find nothing and let a fresh window open beside a live relationship.
/// </remarks>
public class RecordedPeersTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The property this exists for. A pairing is found by the address it was made against even
    /// though the identifier a peer would offer today is a different one, because the identifier
    /// is not what is being looked up.
    /// </summary>
    [Fact]
    public void APeerOfferingADifferentKeyIsStillFound()
    {
        var records = Holding(PairingState.Active, "https://peer.example");

        // What a peer offering a different key produces: a derived identifier that is not the one
        // the record is filed under. Nothing in this call names it, which is the whole point - the
        // record is reached by the address the operator entered.
        Assert.Null(records.Read("0000000000000000000000000000dead"));

        Assert.True(new RecordedPeers(records).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// Every state a record survives in answers true, revoked included. A revoked pairing keeps
    /// its record on purpose, and opening a window against a revoked peer is re-pairing, which is
    /// not something an enrolment window does silently.
    /// </summary>
    /// <param name="state">The state the record is in.</param>
    [Theory]
    [InlineData(PairingState.Pending)]
    [InlineData(PairingState.ConfirmedHere)]
    [InlineData(PairingState.ConfirmedByPeer)]
    [InlineData(PairingState.Active)]
    [InlineData(PairingState.Rotating)]
    [InlineData(PairingState.Revoked)]
    public void EveryStateARecordSurvivesInIsAPairing(PairingState state)
    {
        var records = Holding(state, "https://peer.example");

        Assert.True(new RecordedPeers(records).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// A record in <see cref="PairingState.Offered"/> is not a pairing to displace, and it is the
    /// one state this skips. It is this server's own half-open window rather than a relationship
    /// with a peer, and <see cref="EnrolmentWindow"/> already answers a second window against the
    /// same address with <see cref="WindowOpening.AlreadyOpen"/>. Answering true here would
    /// replace that with <see cref="WindowOpening.AlreadyPaired"/> and tell an operator who
    /// opened a window twice that they are paired with a server they have never reached.
    /// </summary>
    [Fact]
    public void AHalfOpenWindowIsNotAPairing()
    {
        var records = Holding(PairingState.Offered, "https://peer.example");

        Assert.False(new RecordedPeers(records).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// A record for another peer is not this peer. The comparison is on the canonical spelling
    /// <see cref="PeerAddress"/> produces, so two addresses that are not the same address do not
    /// match, and a reader answering true for any record at all would refuse every window a
    /// server with one pairing ever tried to open.
    /// </summary>
    [Fact]
    public void ARecordForAnotherAddressIsNotAMatch()
    {
        var records = Holding(PairingState.Active, "https://other.example");

        Assert.False(new RecordedPeers(records).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// A record carrying no address matches nothing. That is what a record written before format
    /// 2 of the store looks like, and treating an absent address as a match would refuse every
    /// window on the strength of a record that does not say which peer it is for.
    /// </summary>
    [Fact]
    public void ARecordCarryingNoAddressMatchesNothing()
    {
        var records = Holding(PairingState.Active, null);

        Assert.False(new RecordedPeers(records).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// An empty store is a server that is paired with nobody, which is what a fresh installation
    /// is. The floor under every case above: a reader that answered true for an empty store would
    /// pass the refusal cases and refuse every first pairing.
    /// </summary>
    [Fact]
    public void AnEmptyStoreIsPairedWithNobody()
    {
        Assert.False(new RecordedPeers(new InMemoryPairingRecords()).HasPairingWith(Address("https://peer.example")));
    }

    /// <summary>
    /// The identifiers held under an address in a state are answered, which is what the enrolment
    /// join uses to find the record a window wrote. It is the same read as the question above with
    /// the state named, so the two cannot disagree about what an address matches.
    /// </summary>
    [Fact]
    public void TheIdentifiersHeldUnderAnAddressAreAnswered()
    {
        var records = Holding(PairingState.Offered, "https://peer.example");

        Assert.Equal(
            new[] { PairingId },
            RecordedPeers.Under(records, Address("https://peer.example"), PairingState.Offered));

        Assert.Empty(RecordedPeers.Under(records, Address("https://peer.example"), PairingState.Active));
        Assert.Empty(RecordedPeers.Under(records, Address("https://other.example"), PairingState.Offered));
    }

    private static PeerAddress Address(string candidate)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(candidate, out var address));

        return address!;
    }

    private static InMemoryPairingRecords Holding(PairingState state, string? address)
    {
        var records = new InMemoryPairingRecords();

        records.Write(new PairingRecord(
            PairingId,
            state,
            PairingState.Absent,
            state.ToString(),
            "an-administrator",
            Noon,
            address));

        return records;
    }
}
