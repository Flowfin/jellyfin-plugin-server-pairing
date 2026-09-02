using System;
using Jellyfin.Plugin.ServerPairing.KeyStore;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// An administrator stopping a pairing on this server, which is one act over two stores.
/// </summary>
/// <remarks>
/// Destroying the key and recording the transition were both reachable before this type and
/// nothing joined them, so a revocation was something a test stipulated rather than something a
/// server could perform. What made the join possible is the record store that ships:
/// <see cref="PairingStateMachine"/> had no implementation to write a <c>Revoked</c> record
/// through until <see cref="FilePairingRecordStore"/> landed.
/// <para>
/// THE KEY IS DESTROYED FIRST AND THE ORDER IS THE WHOLE DESIGN. Two files are written and
/// either write can fail, so one of the two half-done states is reached whenever something goes
/// wrong, and they are not equally bad. A key destroyed with no record written leaves a server
/// that refuses every request for that pairing while its record still says the pairing is live:
/// the link is stopped and the record is wrong. A record written with the key still in the store
/// leaves a server that reports the pairing revoked to an operator and goes on authenticating
/// the peer with it, which is the failure this issue exists against. So the destruction goes
/// first, and the residual is a stale record rather than a live key.
/// </para>
/// <para>
/// THE KEY IS DESTROYED EVEN WHERE THERE IS NOTHING TO RECORD. The two stores are separate
/// files that can disagree - one restored from a backup, one replaced by hand - so a record
/// saying <see cref="PairingState.Absent"/> is not evidence that no key is held.
/// <see cref="IPairingKeyStore.Destroy"/> is declared to do nothing where nothing is held, so
/// sweeping unconditionally costs a caller nothing and closes the case where the record is the
/// half that is wrong.
/// </para>
/// <para>
/// NOTHING HERE REACHES THE PEER, AND THAT IS THE PROPERTY RATHER THAN A GAP IN IT. A
/// revocation that depends on reaching the peer is not a revocation, so this type is
/// constructed from two stores and nothing else: there is no channel it could call and no
/// answer it could wait for, and a revocation therefore completes identically whether the peer
/// is listening, hostile or gone. The courtesy notification issue #24 also asks for is a
/// separate act that follows a completed revocation, and it cannot be attempted at all today:
/// a message this server sends needs the peer's address and the version the pairing settled on,
/// and <see cref="PairingRecord"/> carries neither.
/// </para>
/// </remarks>
public sealed class Revocation
{
    private readonly IPairingKeyStore _keys;
    private readonly PairingStateMachine _pairings;

    /// <summary>
    /// Initializes a new instance of the <see cref="Revocation"/> class.
    /// </summary>
    /// <param name="keys">Where this server keeps the key material it holds.</param>
    /// <param name="pairings">The state machine that records what a pairing became.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public Revocation(IPairingKeyStore keys, PairingStateMachine pairings)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
    }

    /// <summary>
    /// Stops a pairing here, unilaterally and without contacting the peer.
    /// </summary>
    /// <param name="pairingId">The pairing to stop.</param>
    /// <param name="actor">Who decided it, which is recorded on the transition.</param>
    /// <param name="at">When it was decided.</param>
    /// <returns>Whether a transition into <see cref="PairingState.Revoked"/> was recorded.</returns>
    /// <exception cref="ArgumentNullException">The identifier or the actor is null.</exception>
    public RevocationOutcome Revoke(string pairingId, string actor, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(actor);

        _keys.Destroy(pairingId);

        var transition = _pairings.Apply(pairingId, LocalEvent.AdministratorRevoked, actor, at);

        return transition.Outcome == TransitionOutcome.Answered
            ? RevocationOutcome.Revoked
            : RevocationOutcome.NothingToRevoke;
    }
}
