using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The two tables in <c>docs/protocol.md</c>, transcribed cell by cell.
/// </summary>
/// <remarks>
/// Every expectation here is read out of that document rather than out of the type it judges.
/// A table derived from the implementation agrees with it by construction and proves nothing,
/// which is why this issue waited for the document to exist rather than being written first.
/// <para>
/// The rows are laid out in the document's order and each carries the document's own wording
/// for the cell, so a failure names the cell a reader can go and look at.
/// </para>
/// </remarks>
internal static class TransitionTables
{
    /// <summary>
    /// Gets every cell of the transition table.
    /// </summary>
    public static IReadOnlyList<MessageCell> Messages { get; } = BuildMessages();

    /// <summary>
    /// Gets every cell of the local events table.
    /// </summary>
    public static IReadOnlyList<LocalEventCell> LocalEvents { get; } = BuildLocalEvents();

    /// <summary>
    /// Gets the label of every transition table cell, as theory data.
    /// </summary>
    /// <returns>One label per cell.</returns>
    public static IEnumerable<object[]> MessageCells()
        => Messages.Select(cell => new object[] { cell.Cell });

    /// <summary>
    /// Gets the label of every local events table cell, as theory data.
    /// </summary>
    /// <returns>One label per cell.</returns>
    public static IEnumerable<object[]> LocalEventCells()
        => LocalEvents.Select(cell => new object[] { cell.Cell });

    /// <summary>
    /// Finds a transition table cell by its label.
    /// </summary>
    /// <param name="cell">The label.</param>
    /// <returns>The cell.</returns>
    public static MessageCell Message(string cell) => Messages.Single(row => row.Cell == cell);

    /// <summary>
    /// Finds a local events table cell by its label.
    /// </summary>
    /// <param name="cell">The label.</param>
    /// <returns>The cell.</returns>
    public static LocalEventCell Local(string cell) => LocalEvents.Single(row => row.Cell == cell);

    private static List<MessageCell> BuildMessages()
    {
        var cells = new List<MessageCell>();

        // Row: Absent. Every cell of it reads refused.
        cells.AddRange(WholeRowRefused(PairingState.Absent));

        // Row: Offered. Hello records the peer key, answers with this side's key, and goes to
        // Pending. Everything else is refused.
        cells.Add(Accepted(PairingState.Offered, PairingMessage.Hello, PairingState.Pending));
        cells.Add(Refused(PairingState.Offered, PairingMessage.Confirm));
        cells.Add(Refused(PairingState.Offered, PairingMessage.Rotate));
        cells.Add(Refused(PairingState.Offered, PairingMessage.Revoke));
        cells.Add(Refused(PairingState.Offered, PairingMessage.Exchange));

        // Row: Pending.
        cells.Add(TheSameKey(PairingState.Pending));
        cells.Add(ADifferentKey(PairingState.Pending));
        cells.Add(Accepted(PairingState.Pending, PairingMessage.Confirm, PairingState.ConfirmedByPeer));
        cells.Add(WrongState(PairingState.Pending, PairingMessage.Rotate));
        cells.Add(Accepted(PairingState.Pending, PairingMessage.Revoke, PairingState.Revoked));
        cells.Add(WrongState(PairingState.Pending, PairingMessage.Exchange));

        // Row: ConfirmedHere.
        cells.Add(TheSameKey(PairingState.ConfirmedHere));
        cells.Add(ADifferentKey(PairingState.ConfirmedHere));
        cells.Add(Accepted(PairingState.ConfirmedHere, PairingMessage.Confirm, PairingState.Active));
        cells.Add(WrongState(PairingState.ConfirmedHere, PairingMessage.Rotate));
        cells.Add(Accepted(PairingState.ConfirmedHere, PairingMessage.Revoke, PairingState.Revoked));
        cells.Add(WrongState(PairingState.ConfirmedHere, PairingMessage.Exchange));

        // Row: ConfirmedByPeer. A repeated confirm is answered as before rather than refused,
        // because the network drops responses and a peer that retries is not an attacker.
        cells.Add(TheSameKey(PairingState.ConfirmedByPeer));
        cells.Add(ADifferentKey(PairingState.ConfirmedByPeer));
        cells.Add(Accepted(PairingState.ConfirmedByPeer, PairingMessage.Confirm, PairingState.ConfirmedByPeer));
        cells.Add(WrongState(PairingState.ConfirmedByPeer, PairingMessage.Rotate));
        cells.Add(Accepted(PairingState.ConfirmedByPeer, PairingMessage.Revoke, PairingState.Revoked));
        cells.Add(WrongState(PairingState.ConfirmedByPeer, PairingMessage.Exchange));

        // Row: Active.
        cells.Add(Refused(PairingState.Active, PairingMessage.Hello));
        cells.Add(Accepted(PairingState.Active, PairingMessage.Confirm, PairingState.Active));
        cells.Add(Accepted(PairingState.Active, PairingMessage.Rotate, PairingState.Rotating));
        cells.Add(Accepted(PairingState.Active, PairingMessage.Revoke, PairingState.Revoked));
        cells.Add(Accepted(PairingState.Active, PairingMessage.Exchange, PairingState.Active));

        // Row: Rotating. A second rotate inside an overlap is the state code rather than a
        // second overlap.
        cells.Add(Refused(PairingState.Rotating, PairingMessage.Hello));
        cells.Add(Accepted(PairingState.Rotating, PairingMessage.Confirm, PairingState.Rotating));
        cells.Add(WrongState(PairingState.Rotating, PairingMessage.Rotate));
        cells.Add(Accepted(PairingState.Rotating, PairingMessage.Revoke, PairingState.Revoked));
        cells.Add(Accepted(PairingState.Rotating, PairingMessage.Exchange, PairingState.Rotating));

        // Row: Revoked. Terminal, and a repeated revoke is refused rather than answered,
        // because this state answers nothing at all.
        cells.AddRange(WholeRowRefused(PairingState.Revoked));

        return cells;
    }

    private static List<LocalEventCell> BuildLocalEvents()
    {
        var cells = new List<LocalEventCell>();

        foreach (var from in Enum.GetValues<PairingState>())
        {
            // An administrator opens a window. Only out of Absent.
            cells.Add(Cell(
                from,
                LocalEvent.WindowOpened,
                from == PairingState.Absent ? PairingState.Offered : from,
                from == PairingState.Absent));

            // An administrator confirms the fingerprint. The document gives this two rows,
            // with two destinations, and every other state is not one of them.
            cells.Add(from switch
            {
                PairingState.Pending => Cell(from, LocalEvent.FingerprintConfirmed, PairingState.ConfirmedHere, true),
                PairingState.ConfirmedByPeer => Cell(from, LocalEvent.FingerprintConfirmed, PairingState.Active, true),
                _ => Cell(from, LocalEvent.FingerprintConfirmed, from, false),
            });

            // The enrolment window expires, out of the four half-built states.
            var expires = from is PairingState.Offered
                or PairingState.Pending
                or PairingState.ConfirmedHere
                or PairingState.ConfirmedByPeer;

            cells.Add(Cell(
                from,
                LocalEvent.WindowExpired,
                expires ? PairingState.Absent : from,
                expires));

            // An administrator revokes, out of every state where a pairing exists.
            var revocable = from is not (PairingState.Absent or PairingState.Revoked);

            cells.Add(Cell(
                from,
                LocalEvent.AdministratorRevoked,
                revocable ? PairingState.Revoked : from,
                revocable));

            // The rotation overlap closes, only out of Rotating.
            cells.Add(Cell(
                from,
                LocalEvent.RotationOverlapClosed,
                from == PairingState.Rotating ? PairingState.Active : from,
                from == PairingState.Rotating));
        }

        return cells;
    }

    private static IEnumerable<MessageCell> WholeRowRefused(PairingState from)
        => Enum.GetValues<PairingMessage>().Select(message => Refused(from, message));

    private static MessageCell Refused(PairingState from, PairingMessage message)
        => new MessageCell
        {
            Cell = Label(from, message),
            From = from,
            Message = message,
            To = from,
            Outcome = TransitionOutcome.Refused,
        };

    private static MessageCell WrongState(PairingState from, PairingMessage message)
        => new MessageCell
        {
            Cell = Label(from, message),
            From = from,
            Message = message,
            To = from,
            Outcome = TransitionOutcome.WrongState,
        };

    private static MessageCell Accepted(PairingState from, PairingMessage message, PairingState to)
        => new MessageCell
        {
            Cell = Label(from, message),
            From = from,
            Message = message,
            To = to,
            Outcome = TransitionOutcome.Answered,
        };

    private static MessageCell TheSameKey(PairingState from)
        => new MessageCell
        {
            Cell = Label(from, PairingMessage.Hello) + ", identical key",
            From = from,
            Message = PairingMessage.Hello,
            Offered = OfferedKey.Identical,
            To = from,
            Outcome = TransitionOutcome.Answered,
        };

    private static MessageCell ADifferentKey(PairingState from)
        => new MessageCell
        {
            Cell = Label(from, PairingMessage.Hello) + ", different key",
            From = from,
            Message = PairingMessage.Hello,
            Offered = OfferedKey.Different,
            To = PairingState.Absent,
            Outcome = TransitionOutcome.Refused,
        };

    private static LocalEventCell Cell(PairingState from, LocalEvent local, PairingState to, bool accepted)
        => new LocalEventCell
        {
            Cell = string.Create(CultureInfo.InvariantCulture, $"{from} + {local}"),
            From = from,
            Event = local,
            To = to,
            Outcome = accepted ? TransitionOutcome.Answered : TransitionOutcome.Refused,
        };

    private static string Label(PairingState from, PairingMessage message)
        => string.Create(CultureInfo.InvariantCulture, $"{from} + {message}");
}
