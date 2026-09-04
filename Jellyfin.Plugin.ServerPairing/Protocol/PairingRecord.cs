using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What is held about one pairing's state, and how it got there.
/// </summary>
/// <remarks>
/// The second half is the audit trail <c>docs/logging.md</c> promises. Every transition
/// records who caused it, when, and which state it came from, because a log entry saying a
/// pairing is revoked answers none of the questions an operator asks after finding one.
/// <para>
/// Key material is not here and never will be. This record says what state a pairing is in;
/// what verifies its requests is the key store, which is M4, and the two are written together
/// so that a pairing and its keys cannot get out of step.
/// </para>
/// <para>
/// THE PEER ADDRESS IS HERE AND IS NOT KEY MATERIAL. It is the one thing an operator typed, it
/// is what <see cref="IPairedPeers"/> has to answer by, and it cannot be reached from the
/// identifier: the identifier is derived from both public keys, so a peer offering a different
/// key produces a different one and the pairing already held is not found by it. It arrives
/// through the constructor like every other member, so a record cannot exist for a moment
/// without one and then be given one.
/// </para>
/// </remarks>
public sealed class PairingRecord
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PairingRecord"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="state">The state the pairing is in.</param>
    /// <param name="cameFrom">The state it was in before the transition that produced this.</param>
    /// <param name="cause">What caused the transition, named as the message or the local event.</param>
    /// <param name="actor">Who caused it.</param>
    /// <param name="at">When it happened.</param>
    /// <param name="peerAddress">
    /// The canonical spelling of the address the operator entered, or null where the record was
    /// written by a build that had no such member.
    /// </param>
    public PairingRecord(
        string pairingId,
        PairingState state,
        PairingState cameFrom,
        string cause,
        string actor,
        DateTimeOffset at,
        string? peerAddress)
    {
        PairingId = pairingId;
        State = state;
        CameFrom = cameFrom;
        Cause = cause;
        Actor = actor;
        At = at;
        PeerAddress = peerAddress;
    }

    /// <summary>
    /// Gets the pairing identifier.
    /// </summary>
    public string PairingId { get; }

    /// <summary>
    /// Gets the state the pairing is in.
    /// </summary>
    public PairingState State { get; }

    /// <summary>
    /// Gets the state it was in before the transition that produced this record.
    /// </summary>
    public PairingState CameFrom { get; }

    /// <summary>
    /// Gets what caused the transition.
    /// </summary>
    public string Cause { get; }

    /// <summary>
    /// Gets who caused it.
    /// </summary>
    public string Actor { get; }

    /// <summary>
    /// Gets when it happened. It is supplied by the caller rather than read from a clock here,
    /// so an expiry is testable without waiting for one.
    /// </summary>
    public DateTimeOffset At { get; }

    /// <summary>
    /// Gets the peer this pairing is with, as the canonical spelling
    /// <see cref="Protocol.PeerAddress.Value"/> produces, or null where the record carries none.
    /// </summary>
    /// <remarks>
    /// A string rather than a <see cref="Protocol.PeerAddress"/>, because a record is read back
    /// out of a file and re-parsing on that path would fail for a cleartext address whose
    /// acknowledgement is a setting the store cannot see. What is kept is the spelling the parse
    /// already produced, and two addresses are compared as that spelling, which is what
    /// <see cref="Protocol.PeerAddress"/> says its value is for.
    /// <para>
    /// NULL IS A REAL ANSWER AND NOT A DEFAULT NOBODY REACHES. A record written before format 2
    /// of the store carries no address, so a reader that took this for present would find one
    /// missing on exactly the files a migration exists for. What answers by address treats such a
    /// record as a record of no address rather than as a match.
    /// </para>
    /// </remarks>
    public string? PeerAddress { get; }
}
