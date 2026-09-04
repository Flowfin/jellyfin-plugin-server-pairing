using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The join between an enrolment window and the state a pairing is in.
/// </summary>
/// <remarks>
/// <see cref="EnrolmentWindow"/> holds addresses and knows nothing about records;
/// <see cref="PairingStateMachine"/> holds records and knows nothing about windows. Nothing put
/// the two together, so opening a window wrote no record, an administrative read of the open
/// windows answered an empty list on every server, and no pairing had ever been in
/// <see cref="PairingState.Offered"/> anywhere outside the test project. This is that join and
/// nothing else: it takes no decision either of those two types takes, and it holds no state of
/// its own.
/// <para>
/// THE FIRST ROW OF THE LOCAL EVENTS TABLE IS WHAT THIS EXECUTES. <c>docs/protocol.md</c> is the
/// authority for it and a difference between that document and this file is a defect in this
/// file. The record is written under a <see cref="ProvisionalPairingId"/>, because a window opens
/// before any peer key has arrived and the derived identifier does not exist yet.
/// </para>
/// <para>
/// THE WINDOW ANSWERS FIRST AND THE RECORD FOLLOWS IT. An opening that the window refuses writes
/// nothing, so a refusal leaves the store exactly as it was, and the store is never asked to hold
/// a pairing for a window that does not exist. The reverse order would mint an identifier for
/// every refused attempt.
/// </para>
/// <para>
/// Nothing here reads a clock. The instant arrives as an argument, like everywhere else in this
/// namespace, so an expiry is testable without waiting for one.
/// </para>
/// </remarks>
public sealed class Enrolment
{
    /// <summary>
    /// What is written on the record as the cause of a transition this type drives.
    /// </summary>
    /// <remarks>
    /// The state machine writes the event's own name, so this type supplies the actor rather than
    /// the cause. What is named here is the actor a sweep uses, which is the one transition
    /// nobody asked for: an administrator is the actor of an opening and of a closing they
    /// asked for, and a window that ran out of time was moved by this server.
    /// </remarks>
    public const string SweepActor = "server";

    private readonly EnrolmentWindow _windows;

    private readonly PairingStateMachine _pairings;

    private readonly IPairingRecordStore _records;

    /// <summary>
    /// Initializes a new instance of the <see cref="Enrolment"/> class.
    /// </summary>
    /// <param name="windows">The windows this server has open.</param>
    /// <param name="pairings">The one type that owns what state a pairing is in.</param>
    /// <param name="records">
    /// Where the records are kept, read to find the identifier a window's record was written
    /// under.
    /// </param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// The record store is taken as well as the state machine, and the two are not the same
    /// dependency read twice. The state machine answers about one identifier at a time and this
    /// type has an address, so what turns the address back into the identifier a window's record
    /// was written under is a walk, and a walk is what the store offers.
    /// </remarks>
    public Enrolment(EnrolmentWindow windows, PairingStateMachine pairings, IPairingRecordStore records)
    {
        _windows = windows ?? throw new ArgumentNullException(nameof(windows));
        _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
        _records = records ?? throw new ArgumentNullException(nameof(records));
    }

    /// <summary>
    /// Opens a window against a peer address and writes the record that says a pairing is being
    /// built with it.
    /// </summary>
    /// <param name="address">The address an administrator entered.</param>
    /// <param name="actor">Who asked.</param>
    /// <param name="at">When they asked.</param>
    /// <returns>What the window answered, and the identifier where one was written.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// A RECORD LEFT IN <see cref="PairingState.Offered"/> FOR THIS ADDRESS IS RETIRED BEFORE A
    /// NEW ONE IS WRITTEN, and that is not tidying. Windows live in memory and records live in a
    /// file, so a server restarted while a window was open comes back with the record and without
    /// the window. If the window answered <see cref="WindowOpening.Opened"/> then no window was
    /// open for this address, so any record still in <c>Offered</c> for it belongs to a window
    /// that is gone. Leaving it would put two half-built pairings for one peer in the store, one
    /// of which nothing will ever move again.
    /// <para>
    /// The retirement is the same transition the expiry is, because that is what happened to it:
    /// the window it belonged to ended without being used. It goes through the state machine like
    /// every other move rather than deleting a record behind its back.
    /// </para>
    /// </remarks>
    public WindowOpened Open(PeerAddress address, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(actor);

        var opening = _windows.Open(address, at);

        if (opening != WindowOpening.Opened)
        {
            return new WindowOpened(opening, null);
        }

        Retire(address, SweepActor, at);

        var pairingId = ProvisionalPairingId.Mint();

        _pairings.Apply(pairingId, LocalEvent.WindowOpened, actor, at, address);

        return new WindowOpened(opening, pairingId);
    }

    /// <summary>
    /// Closes a window an administrator no longer wants open, and destroys the record with it.
    /// </summary>
    /// <param name="address">The address it was opened against.</param>
    /// <param name="actor">Who asked.</param>
    /// <param name="at">When they asked.</param>
    /// <returns>True where a window was held for that address.</returns>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <remarks>
    /// The record goes in the same call the window does, which is the <c>Absent</c> row of the
    /// local events table rather than the <c>Revoked</c> one: a window that closes without being
    /// used leaves no pairing behind, and <see cref="PairingState.Absent"/> is defined by there
    /// being no record.
    /// <para>
    /// The record is retired whether or not a window was held. An address with a record in
    /// <c>Offered</c> and no window is the restart case above, and an administrator closing that
    /// address is asking for exactly what the retirement does.
    /// </para>
    /// </remarks>
    public bool Close(PeerAddress address, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(actor);

        var held = _windows.Close(address);

        Retire(address, actor, at);

        return held;
    }

    /// <summary>
    /// Closes every window that has run out of time and destroys the records with them.
    /// </summary>
    /// <param name="at">This server's clock.</param>
    /// <returns>The addresses whose windows were closed, in no defined order.</returns>
    /// <remarks>
    /// This is the caller <see cref="EnrolmentWindow.CloseElapsed"/> says it needs and did not
    /// have. An elapsed window refuses before this runs, so what this adds is the moment the
    /// half-built record is destroyed - and a record nobody destroys outlives the window it
    /// belongs to and sits in the store saying a pairing is being built that nothing will ever
    /// move again.
    /// <para>
    /// WHAT DRIVES THIS ON A SERVER IS NOT BUILT, and no run on a server has been made to watch
    /// it: nothing schedules a sweep, so an elapsed record is destroyed only when this is called
    /// and a caller that never calls it leaves them. The record is nevertheless not readable as an
    /// open window, because an elapsed window is already outside
    /// <see cref="EnrolmentWindow.OpenAddresses"/>.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CloseElapsed(DateTimeOffset at)
    {
        var elapsed = _windows.CloseElapsed(at);

        foreach (var address in elapsed)
        {
            foreach (var pairingId in Under(address, PairingState.Offered))
            {
                _pairings.Apply(pairingId, LocalEvent.WindowExpired, SweepActor, at);
            }
        }

        return elapsed;
    }

    private void Retire(PeerAddress address, string actor, DateTimeOffset at)
    {
        foreach (var pairingId in RecordedPeers.Under(_records, address, PairingState.Offered))
        {
            _pairings.Apply(pairingId, LocalEvent.WindowExpired, actor, at);
        }
    }

    /// <summary>
    /// The records held under an address that is a string rather than a parsed address.
    /// </summary>
    /// <remarks>
    /// <see cref="EnrolmentWindow"/> answers in the canonical strings it was given rather than in
    /// <see cref="PeerAddress"/> values, and a record holds the same canonical string, so the two
    /// are compared as they stand. Re-parsing here would refuse a cleartext address whose
    /// acknowledgement is a setting neither this type nor the store can see.
    /// </remarks>
    private List<string> Under(string address, PairingState state)
    {
        var found = new List<string>();

        foreach (var pairingId in _records.Pairings())
        {
            var record = _records.Read(pairingId);

            if (record is not null
                && record.State == state
                && string.Equals(record.PeerAddress, address, StringComparison.Ordinal))
            {
                found.Add(pairingId);
            }
        }

        return found;
    }
}
