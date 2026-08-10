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
    public PairingRecord(
        string pairingId,
        PairingState state,
        PairingState cameFrom,
        string cause,
        string actor,
        DateTimeOffset at)
    {
        PairingId = pairingId;
        State = state;
        CameFrom = cameFrom;
        Cause = cause;
        Actor = actor;
        At = at;
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
}
