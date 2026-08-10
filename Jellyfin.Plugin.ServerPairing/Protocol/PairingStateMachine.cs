using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The one type that owns what state a pairing is in.
/// </summary>
/// <remarks>
/// Every other part of the plugin asks this rather than deciding for itself, and a state is
/// changed by calling a method here rather than by writing a field. The two tables it
/// implements are the transition table and the local events table in <c>docs/protocol.md</c>,
/// which is the authority for both; a difference between that document and this file is a
/// defect in this file.
/// <para>
/// Nothing here reads a clock. The instant a transition happened arrives as an argument,
/// because an expiry that reads the wall clock is an expiry no test can move. Which clock
/// supplies it is issue #26.
/// </para>
/// <para>
/// Nothing here is cached. Every call reads the store, so a record changed underneath this
/// type is seen on the next call and a write that failed leaves nothing stale in memory
/// pretending it succeeded.
/// </para>
/// </remarks>
public sealed class PairingStateMachine
{
    private readonly IPairingRecordStore _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="PairingStateMachine"/> class.
    /// </summary>
    /// <param name="records">Where a pairing's state is kept.</param>
    public PairingStateMachine(IPairingRecordStore records)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
    }

    /// <summary>
    /// The cell of the transition table for one state and one arriving message.
    /// </summary>
    /// <param name="from">The state on this side.</param>
    /// <param name="message">The message that arrived.</param>
    /// <param name="offered">
    /// For a hello reaching a state that has a recorded peer key, whether the offered key is
    /// that one. <see cref="OfferedKey.NotApplicable"/> everywhere else.
    /// </param>
    /// <returns>What the pairing becomes and what the caller is told.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The state or the message is not one of the defined values.</exception>
    /// <exception cref="ArgumentException">
    /// A hello reached a state holding a recorded peer key without saying whether the offered
    /// key is that one. That comparison decides between answering and destroying the
    /// half-built pairing, so it is refused as a caller error rather than guessed at.
    /// </exception>
    public static PairingTransition Next(PairingState from, PairingMessage message, OfferedKey offered)
    {
        if (!Enum.IsDefined(from))
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        if (!Enum.IsDefined(message))
        {
            throw new ArgumentOutOfRangeException(nameof(message));
        }

        return message switch
        {
            PairingMessage.Hello => Hello(from, offered),
            PairingMessage.Confirm => Confirm(from),
            PairingMessage.Rotate => Rotate(from),
            PairingMessage.Revoke => Revoke(from),
            _ => Exchange(from),
        };
    }

    /// <summary>
    /// The cell of the local events table for one state and one event on this side.
    /// </summary>
    /// <param name="from">The state on this side.</param>
    /// <param name="local">What happened here.</param>
    /// <returns>What the pairing becomes, and whether the event was accepted.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The state or the event is not one of the defined values.</exception>
    public static PairingTransition Next(PairingState from, LocalEvent local)
    {
        if (!Enum.IsDefined(from))
        {
            throw new ArgumentOutOfRangeException(nameof(from));
        }

        if (!Enum.IsDefined(local))
        {
            throw new ArgumentOutOfRangeException(nameof(local));
        }

        return local switch
        {
            LocalEvent.WindowOpened => from == PairingState.Absent
                ? new PairingTransition(PairingState.Offered, TransitionOutcome.Answered)
                : Stay(from),
            LocalEvent.FingerprintConfirmed => from switch
            {
                PairingState.Pending => new PairingTransition(PairingState.ConfirmedHere, TransitionOutcome.Answered),
                PairingState.ConfirmedByPeer => new PairingTransition(PairingState.Active, TransitionOutcome.Answered),
                _ => Stay(from),
            },
            LocalEvent.WindowExpired => IsHalfBuilt(from)
                ? new PairingTransition(PairingState.Absent, TransitionOutcome.Answered)
                : Stay(from),
            LocalEvent.AdministratorRevoked => from is PairingState.Absent or PairingState.Revoked
                ? Stay(from)
                : new PairingTransition(PairingState.Revoked, TransitionOutcome.Answered),
            _ => from == PairingState.Rotating
                ? new PairingTransition(PairingState.Active, TransitionOutcome.Answered)
                : Stay(from),
        };
    }

    /// <summary>
    /// The state a pairing is in here.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>
    /// The state, which is <see cref="PairingState.Absent"/> where nothing is held for the
    /// identifier, because that is what that state means.
    /// </returns>
    public PairingState StateOf(string pairingId) => _records.Read(pairingId)?.State ?? PairingState.Absent;

    /// <summary>
    /// The record held for a pairing, which carries who caused its last move, when, and from
    /// which state.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>The record, or null where nothing is held.</returns>
    public PairingRecord? RecordOf(string pairingId) => _records.Read(pairingId);

    /// <summary>
    /// Applies an arriving message to a pairing and persists the result.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="message">The message that arrived.</param>
    /// <param name="offered">Whether a hello's key is the one already recorded.</param>
    /// <param name="actor">Who caused it, which for a message is the peer.</param>
    /// <param name="at">When it happened.</param>
    /// <returns>What the pairing became and what the peer is told.</returns>
    public PairingTransition Receive(
        string pairingId,
        PairingMessage message,
        OfferedKey offered,
        string actor,
        DateTimeOffset at)
    {
        var from = StateOf(pairingId);
        var transition = Next(from, message, offered);

        Persist(pairingId, from, transition, message.ToString(), actor, at);

        return transition;
    }

    /// <summary>
    /// Applies something that happened on this side and persists the result.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="local">What happened here.</param>
    /// <param name="actor">Who caused it, which for most of these is an administrator.</param>
    /// <param name="at">When it happened.</param>
    /// <returns>What the pairing became, and whether the event was accepted.</returns>
    public PairingTransition Apply(string pairingId, LocalEvent local, string actor, DateTimeOffset at)
    {
        var from = StateOf(pairingId);
        var transition = Next(from, local);

        Persist(pairingId, from, transition, local.ToString(), actor, at);

        return transition;
    }

    private static bool IsHalfBuilt(PairingState state)
        => state is PairingState.Offered
            or PairingState.Pending
            or PairingState.ConfirmedHere
            or PairingState.ConfirmedByPeer;

    private static bool HoldsAPeerKey(PairingState state)
        => state is PairingState.Pending or PairingState.ConfirmedHere or PairingState.ConfirmedByPeer;

    private static PairingTransition Stay(PairingState from)
        => new PairingTransition(from, TransitionOutcome.Refused);

    private static PairingTransition Answer(PairingState to)
        => new PairingTransition(to, TransitionOutcome.Answered);

    private static PairingTransition Hello(PairingState from, OfferedKey offered)
    {
        if (from == PairingState.Offered)
        {
            return Answer(PairingState.Pending);
        }

        if (!HoldsAPeerKey(from))
        {
            return Stay(from);
        }

        return offered switch
        {
            OfferedKey.Identical => Answer(from),
            OfferedKey.Different => new PairingTransition(PairingState.Absent, TransitionOutcome.Refused),
            _ => throw new ArgumentException(
                "A hello reaching a state that already holds a peer key has to say whether the offered key is that one.",
                nameof(offered)),
        };
    }

    private static PairingTransition Confirm(PairingState from)
        => from switch
        {
            PairingState.Pending => Answer(PairingState.ConfirmedByPeer),
            PairingState.ConfirmedHere => Answer(PairingState.Active),
            PairingState.ConfirmedByPeer or PairingState.Active or PairingState.Rotating => Answer(from),
            _ => Stay(from),
        };

    private static PairingTransition Rotate(PairingState from)
        => from switch
        {
            PairingState.Active => Answer(PairingState.Rotating),
            PairingState.Rotating => new PairingTransition(from, TransitionOutcome.WrongState),
            _ => HoldsAPeerKey(from)
                ? new PairingTransition(from, TransitionOutcome.WrongState)
                : Stay(from),
        };

    /// <summary>
    /// An arriving revoke, which is accepted wherever this side knows the peer's key and
    /// refused everywhere else.
    /// </summary>
    /// <remarks>
    /// The three refusing states are the two that answer nothing at all and <c>Offered</c>,
    /// where a window is open and no peer key has arrived. A revoke reaching that state cannot
    /// have been signed by anything this side could verify, so accepting it would let anyone
    /// who can reach the endpoint end an enrolment. An administrator on this side ending it is
    /// the local event and is unaffected.
    /// </remarks>
    /// <param name="from">The state on this side.</param>
    /// <returns>The cell.</returns>
    private static PairingTransition Revoke(PairingState from)
        => from is PairingState.Absent or PairingState.Offered or PairingState.Revoked
            ? Stay(from)
            : Answer(PairingState.Revoked);

    private static PairingTransition Exchange(PairingState from)
        => from switch
        {
            PairingState.Active or PairingState.Rotating => Answer(from),
            _ => HoldsAPeerKey(from)
                ? new PairingTransition(from, TransitionOutcome.WrongState)
                : Stay(from),
        };

    /// <summary>
    /// Writes the result of a transition, and only where the pairing moved.
    /// </summary>
    /// <remarks>
    /// A transition that leaves the state where it was writes nothing, and that is a decision
    /// rather than an omission. A repeated confirm and a repeated hello with the identical key
    /// are answered as before, and every refusal from an unauthenticated caller stays where it
    /// was, so persisting on every call would let anyone who can reach the endpoint make this
    /// server write to disk as fast as it can answer. What such a call produces is a log line,
    /// which is <c>docs/logging.md</c>, and the record here carries the last move.
    /// </remarks>
    private void Persist(
        string pairingId,
        PairingState from,
        PairingTransition transition,
        string cause,
        string actor,
        DateTimeOffset at)
    {
        if (!transition.Moves(from))
        {
            return;
        }

        if (transition.To == PairingState.Absent)
        {
            _records.Delete(pairingId);

            return;
        }

        _records.Write(new PairingRecord(pairingId, transition.To, from, cause, actor, at));
    }
}
