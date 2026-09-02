using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The trail a mapping change leaves. A mapping that turns out to be wrong has no history and
/// no explanation without one, which is what issue #40 is written against.
/// </summary>
/// <remarks>
/// What the entry carries was decided on that issue on 2026-08-31 and the shape is not
/// re-argued here: the pairing, the administrator, and which way the mapping moved. The
/// identities on either side of the mapping are the first thing on the never-log list in
/// <c>docs/logging.md</c>, so widening the entry to hold them would put exactly that data into
/// the record an operator keeps longest and pastes into a public thread.
/// <para>
/// The absence is asserted rather than assumed, in
/// <see cref="TheEntryNamesNeitherUserOnEitherSideOfTheMapping"/>. A trail proved only by what
/// it contains stays green on the day somebody adds the peer identifier to it for the best of
/// reasons.
/// </para>
/// </remarks>
public class MappingAuditTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Administrator = "administrator-anna";
    private const string SecondAdministrator = "administrator-bea";
    private const string Peer = "peer";
    private const string LocalUser = "local-user-1";
    private const string PeerUser = "peer-user-1";
    private const string PeerDisplayName = "Anna Example";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A mapping made writes one entry, at the level the table gives the row, naming the
    /// administrator who made it and the direction it moved.
    /// </summary>
    [Fact]
    public void AMappingAddedWritesOneEntryNamingTheAdministratorAndTheDirection()
    {
        var (subject, log) = Paired();

        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At));

        var entry = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(PairingId, entry.Text, StringComparison.Ordinal);
        Assert.Contains(Administrator, entry.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(MappingDirection.Mapped), entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mapping removed writes one entry as well, and it names the administrator who removed
    /// it rather than the one who made it.
    /// </summary>
    /// <remarks>
    /// This is the half that could not be written at all before the administrator became an
    /// argument of <see cref="UserMappings.Unmap"/>. A removal recorded under whoever created
    /// the mapping is worse than no entry, because it names somebody who did not do it.
    /// </remarks>
    [Fact]
    public void AMappingRemovedWritesOneEntryNamingWhoRemovedItRatherThanWhoMadeIt()
    {
        var (subject, log) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At);

        Assert.True(subject.Unmap(PairingId, LocalUser, SecondAdministrator));

        var entry = log.Written[^1];

        Assert.Equal(2, log.Written.Count);
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(SecondAdministrator, entry.Text, StringComparison.Ordinal);
        Assert.Contains(nameof(MappingDirection.Unmapped), entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Administrator, entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every change writes an entry and nothing else does, so a trail read end to end has one
    /// line per change and no line that is not one.
    /// </summary>
    /// <remarks>
    /// Changing a mapping is two acts, so it is two entries, one in each direction. That is
    /// the whole of what the entry means by direction and it is asserted here rather than
    /// inferred from the two cases above.
    /// </remarks>
    [Fact]
    public void ChangingAMappingIsTwoEntriesOneInEachDirection()
    {
        var (subject, log) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At);
        subject.Unmap(PairingId, LocalUser, Administrator);
        subject.Map(PairingId, LocalUser, "peer-user-2", "Bea Example", SecondAdministrator, At);

        Assert.Equal(
            new[]
            {
                nameof(MappingDirection.Mapped),
                nameof(MappingDirection.Unmapped),
                nameof(MappingDirection.Mapped),
            },
            log.Written.Select(DirectionIn).ToArray());
    }

    /// <summary>
    /// Neither the local user nor the peer user appears in the entry, in any form.
    /// </summary>
    /// <remarks>
    /// The peer identity is refused by <c>docs/logging.md</c> at every level, Debug included,
    /// and the local one is not a field the row names. This is the assertion the entry exists
    /// under rather than a detail of it: an audit trail is the record kept longest, so it is
    /// the worst place for the data the rules forbid to arrive by accident.
    /// </remarks>
    [Fact]
    public void TheEntryNamesNeitherUserOnEitherSideOfTheMapping()
    {
        var (subject, log) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At);
        subject.Unmap(PairingId, LocalUser, Administrator);

        Assert.NotEmpty(log.Written);

        foreach (var entry in log.Written)
        {
            Assert.DoesNotContain(LocalUser, entry.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(PeerUser, entry.Text, StringComparison.Ordinal);
            Assert.DoesNotContain(PeerDisplayName, entry.Text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// A refused mapping is not a change, so it writes nothing.
    /// </summary>
    /// <remarks>
    /// An entry per call rather than per change would let a caller grow an operator's log
    /// without a mapping ever moving, and every one of those lines would read as a change
    /// somebody made.
    /// </remarks>
    [Fact]
    public void AMappingThatIsRefusedWritesNothing()
    {
        var (subject, log) = Paired();

        subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At);
        log.Written.Clear();

        Assert.Equal(
            MappingOutcome.LocalUserAlreadyMapped,
            subject.Map(PairingId, LocalUser, "peer-user-2", "Bea Example", SecondAdministrator, At));

        Assert.Empty(log.Written);
    }

    /// <summary>
    /// Removing a mapping that is not there removes nothing, says so, and writes nothing.
    /// </summary>
    [Fact]
    public void RemovingAMappingThatIsNotThereWritesNothing()
    {
        var (subject, log) = Paired();

        Assert.False(subject.Unmap(PairingId, LocalUser, Administrator));
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A pairing ending takes its mappings and writes no mapping-change entry for them.
    /// </summary>
    /// <remarks>
    /// The sweep is the relationship ending rather than an administrator changing a mapping,
    /// and <c>docs/logging.md</c> gives a revocation its own row at its own level. An entry per
    /// swept mapping would report one revocation as many mapping changes, none of them decided
    /// by anybody, and an operator counting changes in a log would count them.
    /// </remarks>
    [Fact]
    public void APairingEndingSweepsItsMappingsAndWritesNoMappingChange()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new InMemoryRecords(), mappings);
        var log = new CapturingLogger();
        var subject = new UserMappings(mappings, machine, log);

        Open(machine);
        subject.Map(PairingId, LocalUser, PeerUser, PeerDisplayName, Administrator, At);
        log.Written.Clear();

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(1, mappings.Sweeps);
        Assert.Empty(mappings.For(PairingId));
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// The mapping direction of an entry, read out of the text the way an operator would.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The name of the direction the entry carries.</returns>
    /// <exception cref="InvalidOperationException">The entry carries neither direction.</exception>
    private static string DirectionIn((LogLevel Level, string Text) entry)
        => Enum.GetNames<MappingDirection>()
            .SingleOrDefault(name => entry.Text.Contains(name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException("The entry names no mapping direction: " + entry.Text);

    /// <summary>
    /// A pairing that has reached a state a mapping may be made under, and the surface that
    /// makes them, over a log a case can read.
    /// </summary>
    /// <returns>The mapping surface and its log.</returns>
    private static (UserMappings Subject, CapturingLogger Log) Paired()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new InMemoryRecords(), mappings);
        var log = new CapturingLogger();

        Open(machine);

        return (new UserMappings(mappings, machine, log), log);
    }

    private static void Open(PairingStateMachine machine)
    {
        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
    }

    private sealed class InMemoryRecords : IPairingRecordStore
    {
        private readonly Dictionary<string, PairingRecord> _held =
            new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        public PairingRecord? Read(string pairingId)
            => _held.TryGetValue(pairingId, out var record) ? record : null;

        public void Write(PairingRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            _held[record.PairingId] = record;
        }

        public void Delete(string pairingId) => _held.Remove(pairingId);
    }

    private sealed class CapturingLogger : ILogger<UserMappings>
    {
        public List<(LogLevel Level, string Text)> Written { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Written.Add((logLevel, formatter(state, exception)));
        }
    }
}
