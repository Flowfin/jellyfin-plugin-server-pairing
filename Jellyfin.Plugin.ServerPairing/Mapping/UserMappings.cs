using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// The only way a mapping comes into existence, and it takes an administrator to do it.
/// </summary>
/// <remarks>
/// Every prior attempt at this problem matches usernames automatically, and that is the
/// mechanism behind the story where one person's watch history lands on another person's
/// account. So there is no method here that takes two sets of users and works out a
/// correspondence, and there is no overload of <see cref="Map"/> without an actor. A
/// suggestion offered on the dashboard is a suggestion; the administrator still confirms
/// each one, and what they confirm arrives here.
/// <para>
/// The pairing is read through <see cref="PairingStateMachine"/> rather than from the record
/// store, so this type asks the one thing that owns what state a pairing is in rather than
/// deciding for itself.
/// </para>
/// </remarks>
public sealed class UserMappings
{
    private readonly IUserMappingStore _mappings;
    private readonly PairingStateMachine _pairings;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserMappings"/> class.
    /// </summary>
    /// <param name="mappings">Where the mappings are kept.</param>
    /// <param name="pairings">What owns the state of a pairing.</param>
    public UserMappings(IUserMappingStore mappings, PairingStateMachine pairings)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
    }

    /// <summary>
    /// Records that an administrator decided a user here is a user on the peer.
    /// </summary>
    /// <param name="pairingId">The pairing the mapping belongs to.</param>
    /// <param name="localUserId">The user on this server.</param>
    /// <param name="peerUserId">The user on the peer.</param>
    /// <param name="peerDisplayName">The peer's display name for that user, cached for the dashboard.</param>
    /// <param name="administrator">The administrator making the decision.</param>
    /// <param name="at">When they made it.</param>
    /// <returns>What happened.</returns>
    /// <remarks>
    /// The administrator is a required argument rather than something defaulted or read from
    /// an ambient context, so there is no way to reach this without naming who decided. That
    /// is what makes the audit trail on the mapping worth reading.
    /// <para>
    /// ONE LOCAL USER MAPS TO AT MOST ONE PEER USER PER PAIRING, AND ONE PEER USER TO AT MOST
    /// ONE LOCAL USER. Both directions are refused here rather than replaced, and the pair of
    /// them is one rule rather than a rule and its mirror: a table that guards only the local
    /// side accepts two local users pointing at one peer user, which puts two people's data
    /// on one account. Which of the two stood in the way is read back with <see cref="Of"/>
    /// and <see cref="From"/>, so a refusal can name the mapping that is already there instead
    /// of telling an administrator only that something failed.
    /// </para>
    /// <para>
    /// CHANGING A MAPPING IS TWO ACTS, <see cref="Unmap"/> and then this. That is not
    /// ceremony: a replacement reads as a repair and is not one, because everything that
    /// already arrived under the old mapping stays on the user it arrived on and nothing here
    /// reaches it. An administrator who has to remove the old mapping first has been shown
    /// that the old one existed.
    /// </para>
    /// </remarks>
    public MappingOutcome Map(
        string pairingId,
        string localUserId,
        string peerUserId,
        string peerDisplayName,
        string administrator,
        DateTimeOffset at)
    {
        var state = _pairings.StateOf(pairingId);

        if (state == PairingState.Absent)
        {
            return MappingOutcome.NoSuchPairing;
        }

        if (state == PairingState.Revoked)
        {
            return MappingOutcome.PairingIsOver;
        }

        if (Of(pairingId, localUserId) is not null)
        {
            return MappingOutcome.LocalUserAlreadyMapped;
        }

        if (From(pairingId, peerUserId) is not null)
        {
            return MappingOutcome.PeerUserAlreadyMapped;
        }

        _mappings.Put(new UserMapping(pairingId, localUserId, peerUserId, peerDisplayName, administrator, at));

        return MappingOutcome.Mapped;
    }

    /// <summary>
    /// Removes the mapping an administrator made for one local user under one pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="localUserId">The user on this server.</param>
    public void Unmap(string pairingId, string localUserId) => _mappings.Remove(pairingId, localUserId);

    /// <summary>
    /// The mappings held for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>The mappings, empty where the pairing holds none.</returns>
    public IReadOnlyList<UserMapping> For(string pairingId) => _mappings.For(pairingId);

    /// <summary>
    /// The mapping held for one local user under one pairing, if any.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="localUserId">The user on this server.</param>
    /// <returns>The mapping, or null where that user is unmapped.</returns>
    /// <remarks>
    /// Null is the fail-closed answer and the caller's job is to skip that user. An unmapped
    /// user is not synced, silently and by default, because the alternative is guessing which
    /// peer user they are.
    /// </remarks>
    public UserMapping? Of(string pairingId, string localUserId)
        => _mappings.For(pairingId)
            .FirstOrDefault(mapping => string.Equals(mapping.LocalUserId, localUserId, StringComparison.Ordinal));

    /// <summary>
    /// The mapping held for one peer user under one pairing, if any.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="peerUserId">The user on the peer.</param>
    /// <returns>The mapping that claims that peer user, or null where none does.</returns>
    /// <remarks>
    /// The reverse of <see cref="Of"/>, and it exists for the refusal rather than for a sync
    /// path. When a second local user is offered a peer user that is already spoken for, this
    /// is what names the local user who has it, so an administrator is told which mapping is
    /// in the way instead of being told that something failed.
    /// </remarks>
    public UserMapping? From(string pairingId, string peerUserId)
        => _mappings.For(pairingId)
            .FirstOrDefault(mapping => string.Equals(mapping.PeerUserId, peerUserId, StringComparison.Ordinal));
}
