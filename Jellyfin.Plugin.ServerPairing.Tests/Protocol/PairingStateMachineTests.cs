using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The state machine against the two tables in <c>docs/protocol.md</c>.
/// </summary>
/// <remarks>
/// The expectations live in <see cref="TransitionTables"/>, transcribed from that document
/// cell by cell. Both tables are walked in full rather than sampled, and the number of cells
/// is checked against the size of the enumerations, so a state or a message added later fails
/// this suite until its row is written rather than passing untested.
/// </remarks>
public class PairingStateMachineTests
{
    private const string PairingId = "3b1f0c7d9e2a48561bd0f37ac5e6902f";
    private const string Peer = "the peer";
    private const string Administrator = "an administrator";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 8, 10, 4, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Every cell of the transition table, walked.
    /// </summary>
    /// <param name="cell">The cell, named as the document's row and column.</param>
    [Theory]
    [MemberData(nameof(TransitionTables.MessageCells), MemberType = typeof(TransitionTables))]
    public void TheTransitionTableIsWhatTheDocumentSays(string cell)
    {
        var expected = TransitionTables.Message(cell);

        var transition = PairingStateMachine.Next(expected.From, expected.Message, expected.Offered);

        Assert.Equal(expected.To, transition.To);
        Assert.Equal(expected.Outcome, transition.Outcome);
    }

    /// <summary>
    /// Every cell of the local events table, walked.
    /// </summary>
    /// <param name="cell">The cell, named as the document's row and column.</param>
    [Theory]
    [MemberData(nameof(TransitionTables.LocalEventCells), MemberType = typeof(TransitionTables))]
    public void TheLocalEventsTableIsWhatTheDocumentSays(string cell)
    {
        var expected = TransitionTables.Local(cell);

        var transition = PairingStateMachine.Next(expected.From, expected.Event);

        Assert.Equal(expected.To, transition.To);
        Assert.Equal(expected.Outcome, transition.Outcome);
    }

    /// <summary>
    /// The two walks above prove nothing about cells nobody wrote down. This counts the
    /// transcribed cells against the enumerations and fails the moment a state, a message or a
    /// local event is added without its row.
    /// </summary>
    [Fact]
    public void EveryPairOfStateAndEventHasACell()
    {
        var states = Enum.GetValues<PairingState>().Length;
        var messages = Enum.GetValues<PairingMessage>().Length;
        var events = Enum.GetValues<LocalEvent>().Length;

        Assert.Equal(8, states);
        Assert.Equal(5, messages);
        Assert.Equal(5, events);

        // Three states hold a recorded peer key, and a hello reaching one of them has two
        // answers rather than one, which is where the three extra cells come from.
        Assert.Equal((states * messages) + 3, TransitionTables.Messages.Count);
        Assert.Equal(states * events, TransitionTables.LocalEvents.Count);

        Assert.Equal(
            TransitionTables.Messages.Count,
            TransitionTables.Messages.Select(c => c.Cell).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            TransitionTables.LocalEvents.Count,
            TransitionTables.LocalEvents.Select(c => c.Cell).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// Nothing that is refused arrives somewhere that answers. One refused cell moves the
    /// pairing, which is the second hello with a different key destroying a half-built one, so
    /// this is stated as the destination never being a state that talks rather than as the
    /// state never moving.
    /// </summary>
    [Fact]
    public void NothingRefusedArrivesAtAStateThatAnswers()
    {
        foreach (var cell in TransitionTables.Messages.Where(c => c.Outcome != TransitionOutcome.Answered))
        {
            var transition = PairingStateMachine.Next(cell.From, cell.Message, cell.Offered);

            Assert.NotEqual(TransitionOutcome.Answered, transition.Outcome);
            Assert.False(
                transition.To != cell.From && transition.To is PairingState.Active or PairingState.Rotating,
                $"{cell.Cell} is refused and arrives at {transition.To}.");
        }

        foreach (var cell in TransitionTables.LocalEvents.Where(c => c.Outcome != TransitionOutcome.Answered))
        {
            var transition = PairingStateMachine.Next(cell.From, cell.Event);

            Assert.Equal(TransitionOutcome.Refused, transition.Outcome);
            Assert.Equal(cell.From, transition.To);
        }
    }

    /// <summary>
    /// Revoked is terminal. Both tables are walked out of it rather than the one row that says
    /// so being asserted.
    /// </summary>
    [Fact]
    public void NothingMovesOutOfRevoked()
    {
        foreach (var message in Enum.GetValues<PairingMessage>())
        {
            var transition = PairingStateMachine.Next(PairingState.Revoked, message, OfferedKey.NotApplicable);

            Assert.Equal(PairingState.Revoked, transition.To);
            Assert.Equal(TransitionOutcome.Refused, transition.Outcome);
        }

        foreach (var local in Enum.GetValues<LocalEvent>())
        {
            var transition = PairingStateMachine.Next(PairingState.Revoked, local);

            Assert.Equal(PairingState.Revoked, transition.To);
            Assert.Equal(TransitionOutcome.Refused, transition.Outcome);
        }
    }

    /// <summary>
    /// A hello reaching a state that already holds a peer key, without saying whether the key
    /// is the recorded one, is a caller error rather than a cell of the table. The two answers
    /// are answering as before and destroying the pairing, and guessing either way is worse
    /// than refusing to.
    /// </summary>
    /// <param name="from">A state holding a recorded peer key.</param>
    [Theory]
    [InlineData(PairingState.Pending)]
    [InlineData(PairingState.ConfirmedHere)]
    [InlineData(PairingState.ConfirmedByPeer)]
    public void AHelloWithNoKeyComparisonIsRefusedAsACallerError(PairingState from)
    {
        Assert.Throws<ArgumentException>(
            () => PairingStateMachine.Next(from, PairingMessage.Hello, OfferedKey.NotApplicable));
    }

    /// <summary>
    /// A value outside an enumeration is not treated as the nearest defined one.
    /// </summary>
    [Fact]
    public void AValueOutsideTheEnumerationIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PairingStateMachine.Next((PairingState)99, PairingMessage.Hello, OfferedKey.NotApplicable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PairingStateMachine.Next(PairingState.Active, (PairingMessage)99, OfferedKey.NotApplicable));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PairingStateMachine.Next((PairingState)99, LocalEvent.WindowOpened));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PairingStateMachine.Next(PairingState.Active, (LocalEvent)99));
    }

    /// <summary>
    /// An identifier nothing is held for is absent, which is what that state means rather than
    /// a case to handle separately.
    /// </summary>
    [Fact]
    public void AnIdentifierNothingIsHeldForIsAbsent()
    {
        var machine = new PairingStateMachine(new InMemoryRecords(), new InMemoryUserMappings());

        Assert.Equal(PairingState.Absent, machine.StateOf(PairingId));
        Assert.Null(machine.RecordOf(PairingId));
    }

    /// <summary>
    /// The lifecycle through the persisting methods, so the tables are shown driving a stored
    /// pairing rather than only computing cells. The last move records who caused it, when,
    /// and which state it came from.
    /// </summary>
    [Fact]
    public void ALifecycleRunsThroughTheStoreAndRecordsItsLastMove()
    {
        var machine = new PairingStateMachine(new InMemoryRecords(), new InMemoryUserMappings());

        Assert.Equal(
            TransitionOutcome.Answered,
            machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At).Outcome);
        Assert.Equal(PairingState.Offered, machine.StateOf(PairingId));

        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
        Assert.Equal(PairingState.Pending, machine.StateOf(PairingId));

        machine.Apply(PairingId, LocalEvent.FingerprintConfirmed, Administrator, At);
        Assert.Equal(PairingState.ConfirmedHere, machine.StateOf(PairingId));

        machine.Receive(PairingId, PairingMessage.Confirm, OfferedKey.NotApplicable, Peer, At);
        Assert.Equal(PairingState.Active, machine.StateOf(PairingId));

        machine.Receive(PairingId, PairingMessage.Rotate, OfferedKey.NotApplicable, Peer, At);
        Assert.Equal(PairingState.Rotating, machine.StateOf(PairingId));

        machine.Apply(PairingId, LocalEvent.RotationOverlapClosed, Administrator, At);
        Assert.Equal(PairingState.Active, machine.StateOf(PairingId));

        machine.Receive(PairingId, PairingMessage.Revoke, OfferedKey.NotApplicable, Peer, At);

        var record = machine.RecordOf(PairingId);

        Assert.NotNull(record);
        Assert.Equal(PairingState.Revoked, record!.State);
        Assert.Equal(PairingState.Active, record.CameFrom);
        Assert.Equal(nameof(PairingMessage.Revoke), record.Cause);
        Assert.Equal(Peer, record.Actor);
        Assert.Equal(At, record.At);
    }

    /// <summary>
    /// Reaching absent removes the record, because that state is defined by there being none.
    /// Revoked is the opposite case and keeps its record, so a later request naming the
    /// identifier is refused rather than treated as new.
    /// </summary>
    [Fact]
    public void ReachingAbsentRemovesTheRecordAndRevokedKeepsIt()
    {
        var machine = new PairingStateMachine(new InMemoryRecords(), new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.Different, Peer, At);

        Assert.Equal(PairingState.Absent, machine.StateOf(PairingId));
        Assert.Null(machine.RecordOf(PairingId));

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(PairingState.Revoked, machine.StateOf(PairingId));
        Assert.NotNull(machine.RecordOf(PairingId));
    }

    /// <summary>
    /// A transition that does not move the pairing writes nothing. Every refusal from a caller
    /// this server has not authenticated is such a transition, so this is what stops anyone who
    /// can reach the endpoint from making the server write to disk as fast as it answers.
    /// </summary>
    [Fact]
    public void ATransitionThatDoesNotMoveTheStateWritesNothing()
    {
        var records = new InMemoryRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);

        var afterTheWindowOpened = records.Writes;

        machine.Receive(PairingId, PairingMessage.Exchange, OfferedKey.NotApplicable, Peer, At);
        machine.Receive(PairingId, PairingMessage.Confirm, OfferedKey.NotApplicable, Peer, At);
        machine.Receive(PairingId, PairingMessage.Rotate, OfferedKey.NotApplicable, Peer, At);
        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Apply(PairingId, LocalEvent.RotationOverlapClosed, Administrator, At);

        Assert.Equal(afterTheWindowOpened, records.Writes);
        Assert.Equal(PairingState.Offered, machine.StateOf(PairingId));
    }

    /// <summary>
    /// A write that does not commit leaves the pairing in the state it was in. The store is
    /// injected and fails on demand, which is this suite's stand-in for the process being
    /// killed between the write and its commit.
    /// </summary>
    [Fact]
    public void AWriteThatDoesNotCommitLeavesThePairingInItsPreviousState()
    {
        var records = new FailOnDemandRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);

        Assert.Equal(PairingState.Pending, machine.StateOf(PairingId));

        records.FailTheNextWrite();

        Assert.Throws<InvalidOperationException>(
            () => machine.Apply(PairingId, LocalEvent.FingerprintConfirmed, Administrator, At));

        Assert.Equal(PairingState.Pending, machine.StateOf(PairingId));
        Assert.Equal(PairingState.Pending, new PairingStateMachine(records, new InMemoryUserMappings()).StateOf(PairingId));

        var record = machine.RecordOf(PairingId);

        Assert.NotNull(record);
        Assert.Equal(PairingState.Offered, record!.CameFrom);
    }

    /// <summary>
    /// The same for the removal half. A window expiring whose removal does not commit leaves
    /// the pairing where it was rather than in a state nobody planned for.
    /// </summary>
    [Fact]
    public void ARemovalThatDoesNotCommitLeavesThePairingInItsPreviousState()
    {
        var records = new FailOnDemandRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        records.FailTheNextWrite();

        Assert.Throws<InvalidOperationException>(
            () => machine.Apply(PairingId, LocalEvent.WindowExpired, Administrator, At));

        Assert.Equal(PairingState.Offered, machine.StateOf(PairingId));
    }

    /// <summary>
    /// The failing store is worth nothing unless the same step succeeds when it is not
    /// failing, which is the floor under the two cases above.
    /// </summary>
    [Fact]
    public void TheSameStepSucceedsWhenTheStoreIsNotFailing()
    {
        var records = new FailOnDemandRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
        machine.Apply(PairingId, LocalEvent.FingerprintConfirmed, Administrator, At);

        Assert.Equal(PairingState.ConfirmedHere, machine.StateOf(PairingId));
    }

    /// <summary>
    /// Nothing is cached. A record changed underneath the machine is seen on the next call,
    /// which is what makes the two failure cases above statements about the store rather than
    /// about a field this type happens not to have.
    /// </summary>
    [Fact]
    public void TheStateIsReadFromTheStoreEveryTime()
    {
        var records = new InMemoryRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);

        var afterOpening = records.Reads;

        Assert.Equal(PairingState.Offered, machine.StateOf(PairingId));
        Assert.Equal(afterOpening + 1, records.Reads);

        records.Write(new PairingRecord(PairingId, PairingState.Active, PairingState.ConfirmedHere, "elsewhere", Peer, At));

        Assert.Equal(PairingState.Active, machine.StateOf(PairingId));
    }

    /// <summary>
    /// A record store that keeps pairings in memory and counts what it was asked to do.
    /// </summary>
    private class InMemoryRecords : IPairingRecordStore
    {
        private readonly Dictionary<string, PairingRecord> _held =
            new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public PairingRecord? Read(string pairingId)
        {
            Reads++;

            return _held.TryGetValue(pairingId, out var record) ? record : null;
        }

        public virtual void Write(PairingRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);

            Writes++;
            _held[record.PairingId] = record;
        }

        public virtual void Delete(string pairingId)
        {
            Writes++;
            _held.Remove(pairingId);
        }
    }

    /// <summary>
    /// The same store, which can be told to fail its next write or removal before it changes
    /// anything. That is what a process killed between a write and its commit looks like from
    /// the caller's side: the call does not return and the previous record is still readable.
    /// </summary>
    private sealed class FailOnDemandRecords : InMemoryRecords
    {
        private bool _failNext;

        public void FailTheNextWrite() => _failNext = true;

        public override void Write(PairingRecord record)
        {
            Fail();
            base.Write(record);
        }

        public override void Delete(string pairingId)
        {
            Fail();
            base.Delete(pairingId);
        }

        private void Fail()
        {
            if (!_failNext)
            {
                return;
            }

            _failNext = false;

            throw new InvalidOperationException("The write did not commit.");
        }
    }
}
