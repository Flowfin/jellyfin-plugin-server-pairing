using System;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The conflict rules of the mapping table, one case per rule and named after it.
///
/// These are the rules that decide whether one person's data ends up on another person's
/// account, so issue #37 asks for them to be written as rules and tested as rules rather
/// than left to emerge from whatever the storage happened to allow. Before this the store
/// took the last write for a local user and kept no opinion at all about a peer user being
/// claimed twice.
/// </summary>
public class MappingConflictRuleTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";
    private const string Administrator = "administrator";
    private const string SecondAdministrator = "second-administrator";
    private const string Peer = "peer";
    private const string LocalUser = "local-user-1";
    private const string AnotherLocalUser = "local-user-2";
    private const string PeerUser = "peer-user-1";
    private const string AnotherPeerUser = "peer-user-2";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset Later = At.AddHours(1);

    /// <summary>
    /// One local user maps to at most one peer user per pairing. A second peer user for a
    /// local user who has one is refused.
    /// </summary>
    [Fact]
    public void OneLocalUserMapsToAtMostOnePeerUserPerPairing()
    {
        var (mappings, _, subject) = Paired();

        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));

        Assert.Equal(
            MappingOutcome.LocalUserAlreadyMapped,
            subject.Map(PairingId, LocalUser, AnotherPeerUser, "Bea", SecondAdministrator, Later));

        Assert.Single(mappings.For(PairingId));
    }

    /// <summary>
    /// One peer user maps to at most one local user per pairing. This is the direction that
    /// is easy to leave out, and leaving it out is what puts two people's history on one
    /// account: two local users both pointing at one peer user is a table that reads as
    /// correct and syncs as wrong.
    /// </summary>
    [Fact]
    public void OnePeerUserMapsToAtMostOneLocalUserPerPairing()
    {
        var (mappings, _, subject) = Paired();

        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));

        Assert.Equal(
            MappingOutcome.PeerUserAlreadyMapped,
            subject.Map(PairingId, AnotherLocalUser, PeerUser, "Anna", SecondAdministrator, Later));

        Assert.Single(mappings.For(PairingId));
    }

    /// <summary>
    /// Issue #37's second done condition. The refusal leaves the mapping that is already
    /// there exactly as it was, field for field, rather than half-updating it.
    /// </summary>
    /// <remarks>
    /// Every field is checked rather than the row count, because the failure this is written
    /// against is a store that refuses and writes anyway - a display cache updated on the way
    /// to the refusal, or an actor stamped by the administrator whose attempt was turned
    /// down, would leave a row nobody decided.
    /// </remarks>
    [Fact]
    public void ASecondMappingIsRefusedAndTheExistingOneIsUnchanged()
    {
        var (_, _, subject) = Paired();

        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(PairingId, LocalUser, PeerUser, "Anna Example", Administrator, At));

        Assert.Equal(
            MappingOutcome.LocalUserAlreadyMapped,
            subject.Map(PairingId, LocalUser, AnotherPeerUser, "Bea Example", SecondAdministrator, Later));

        var held = subject.Of(PairingId, LocalUser);

        Assert.NotNull(held);
        Assert.Equal(PairingId, held!.PairingId);
        Assert.Equal(LocalUser, held.LocalUserId);
        Assert.Equal(PeerUser, held.PeerUserId);
        Assert.Equal("Anna Example", held.PeerDisplayName);
        Assert.Equal(Administrator, held.Actor);
        Assert.Equal(At, held.At);
    }

    /// <summary>
    /// A refusal can name the mapping that is in the way, from either direction, so an
    /// administrator is told which mapping stopped them rather than that something failed.
    /// </summary>
    [Fact]
    public void TheMappingInTheWayIsReachableFromEitherDirection()
    {
        var (_, _, subject) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At);

        Assert.Equal(
            MappingOutcome.PeerUserAlreadyMapped,
            subject.Map(PairingId, AnotherLocalUser, PeerUser, "Anna", SecondAdministrator, Later));

        var blocking = subject.From(PairingId, PeerUser);

        Assert.NotNull(blocking);
        Assert.Equal(LocalUser, blocking!.LocalUserId);
        Assert.Equal(Administrator, blocking.Actor);

        Assert.Equal(PeerUser, subject.Of(PairingId, LocalUser)!.PeerUserId);
    }

    /// <summary>
    /// Correcting a mapping is removing it and making the new one, which is two acts because
    /// it is two acts. A replacement reads as a repair and is not one: what already arrived
    /// under the old mapping stays where it arrived.
    /// </summary>
    [Fact]
    public void ChangingAMappingIsRemovingItAndMakingTheNewOne()
    {
        var (_, _, subject) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At);

        Assert.Equal(
            MappingOutcome.LocalUserAlreadyMapped,
            subject.Map(PairingId, LocalUser, AnotherPeerUser, "Bea", SecondAdministrator, Later));

        subject.Unmap(PairingId, LocalUser, SecondAdministrator);

        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(PairingId, LocalUser, AnotherPeerUser, "Bea", SecondAdministrator, Later));

        Assert.Equal(AnotherPeerUser, subject.Of(PairingId, LocalUser)!.PeerUserId);
    }

    /// <summary>
    /// Across two pairings the same local user may map to different peer users, because
    /// those are different relationships. The rules above are per pairing and this is the
    /// case that keeps them from being read as global.
    /// </summary>
    [Fact]
    public void OneLocalUserMapsToDifferentPeerUsersAcrossTwoPairings()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new InMemoryRecords(), mappings);
        var subject = new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance);

        Open(machine, PairingId);
        Open(machine, AnotherPairing);

        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));
        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(AnotherPairing, LocalUser, AnotherPeerUser, "Anna", Administrator, At));

        Assert.Equal(PeerUser, subject.Of(PairingId, LocalUser)!.PeerUserId);
        Assert.Equal(AnotherPeerUser, subject.Of(AnotherPairing, LocalUser)!.PeerUserId);
    }

    /// <summary>
    /// The same peer user under two pairings, which is the mirror of the case above and the
    /// one that a rule written over the whole table rather than over a pairing would refuse.
    /// </summary>
    [Fact]
    public void OnePeerUserMayBeMappedUnderTwoPairings()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new InMemoryRecords(), mappings);
        var subject = new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance);

        Open(machine, PairingId);
        Open(machine, AnotherPairing);

        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));
        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(AnotherPairing, AnotherLocalUser, PeerUser, "Anna", Administrator, At));

        Assert.Equal(LocalUser, subject.From(PairingId, PeerUser)!.LocalUserId);
        Assert.Equal(AnotherLocalUser, subject.From(AnotherPairing, PeerUser)!.LocalUserId);
    }

    private static (InMemoryUserMappings Mappings, PairingStateMachine Machine, UserMappings Subject) Paired()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new InMemoryRecords(), mappings);

        Open(machine, PairingId);

        return (mappings, machine, new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance));
    }

    private static void Open(PairingStateMachine machine, string pairingId)
    {
        machine.Apply(pairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(pairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
    }

    /// <summary>
    /// A record store held in memory, so the state machine has somewhere to keep a pairing.
    /// </summary>
    private sealed class InMemoryRecords : IPairingRecordStore
    {
        private readonly System.Collections.Generic.Dictionary<string, PairingRecord> _held =
            new System.Collections.Generic.Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        public System.Collections.Generic.IReadOnlyList<string> Pairings()
            => new System.Collections.Generic.List<string>(_held.Keys);

        public PairingRecord? Read(string pairingId)
            => _held.TryGetValue(pairingId, out var record) ? record : null;

        public void Write(PairingRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            _held[record.PairingId] = record;
        }

        public void Delete(string pairingId) => _held.Remove(pairingId);
    }
}
