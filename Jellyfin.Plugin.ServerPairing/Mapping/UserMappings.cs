using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.Extensions.Logging;

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
/// <para>
/// THE AUDIT ENTRY IS WRITTEN HERE BECAUSE EVERY CHANGE PASSES HERE. A mapping is made and
/// removed through this type and through nothing else, which the suite refuses a second route
/// against, so an entry written at this one place cannot be skipped by a caller that forgot.
/// An entry written by an administration surface instead would cover the changes that surface
/// made and no others, and the surface does not exist yet, which would leave the trail owed
/// by whatever is built next.
/// </para>
/// </remarks>
public sealed class UserMappings
{
    private readonly IUserMappingStore _mappings;
    private readonly PairingStateMachine _pairings;
    private readonly ILogger<UserMappings> _log;

    /// <summary>
    /// Initializes a new instance of the <see cref="UserMappings"/> class.
    /// </summary>
    /// <param name="mappings">Where the mappings are kept.</param>
    /// <param name="pairings">What owns the state of a pairing.</param>
    /// <param name="log">Where the audit entry for a change goes.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// The log is required rather than optional, for the reason the mapping store is required
    /// on <see cref="PairingStateMachine"/>: an overload without it would build a type that
    /// changes the table an administrator is answerable for and records none of it, silently,
    /// and a caller reaching that overload would have to notice an absence rather than a
    /// failure.
    /// </remarks>
    public UserMappings(IUserMappingStore mappings, PairingStateMachine pairings, ILogger<UserMappings> log)
    {
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _pairings = pairings ?? throw new ArgumentNullException(nameof(pairings));
        _log = log ?? throw new ArgumentNullException(nameof(log));
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

        Record(pairingId, administrator, MappingDirection.Mapped);

        return MappingOutcome.Mapped;
    }

    /// <summary>
    /// Removes the mapping an administrator made for one local user under one pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="localUserId">The user on this server.</param>
    /// <param name="administrator">The administrator removing it.</param>
    /// <returns>True where a mapping was there and is now gone, false where there was none.</returns>
    /// <exception cref="ArgumentNullException">The pairing, the user or the administrator is null.</exception>
    /// <remarks>
    /// The administrator is required here for the same reason it is required on
    /// <see cref="Map"/>. This method took none until issue #40's answer landed, so the one
    /// field every reading of the audit entry agreed on could not be written for a removal at
    /// all, and half of every trail was a change nobody was named for.
    /// <para>
    /// WHAT IT DOES NOT TAKE IS AN INSTANT, AND THE NOTE ON #40 THAT ASKED FOR ONE IS ANSWERED
    /// RATHER THAN FOLLOWED. <see cref="Map"/> takes one because the mapping it writes carries
    /// it; a removal writes no record, and the only place a removal's moment could go is the
    /// audit entry, whose fields are fixed by the row in <c>docs/logging.md</c> and which says
    /// of itself that it adds no field that table does not name. When a change happened is the
    /// log line's own timestamp, which every entry carries and no row lists.
    /// </para>
    /// <para>
    /// Removing a mapping that is not there writes nothing and says so in the return. An entry
    /// per call rather than per change would let anything that can reach this make an
    /// administrator's log grow without a mapping ever moving, and a trail that records
    /// non-events is one a reader stops trusting.
    /// </para>
    /// <para>
    /// A pairing ending sweeps its mappings and writes none of these. That removal is not an
    /// administrator changing a mapping, it is the relationship ending, which
    /// <c>docs/logging.md</c> gives its own row and its own level; an entry per swept mapping
    /// would report one revocation as many mapping changes, none of them decided by anybody.
    /// </para>
    /// </remarks>
    public bool Unmap(string pairingId, string localUserId, string administrator)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(localUserId);
        ArgumentNullException.ThrowIfNull(administrator);

        if (Of(pairingId, localUserId) is null)
        {
            return false;
        }

        _mappings.Remove(pairingId, localUserId);

        Record(pairingId, administrator, MappingDirection.Unmapped);

        return true;
    }

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

    /// <summary>
    /// Writes the audit entry for a mapping that moved.
    /// </summary>
    /// <param name="pairingId">The pairing the mapping belongs to.</param>
    /// <param name="administrator">The administrator who moved it.</param>
    /// <param name="direction">Which way it moved.</param>
    /// <remarks>
    /// ONE CALL SITE FOR BOTH DIRECTIONS, so the sentence an operator finds in a log cannot
    /// drift between adding and removing and so the guard that ties call sites to rows of
    /// <c>docs/logging.md</c> has one message to hold rather than two that have to be kept
    /// saying the same thing.
    /// <para>
    /// NEITHER IDENTIFIER IS WRITTEN, AND THAT IS THE POINT OF THE ENTRY RATHER THAN A LIMIT ON
    /// IT. The peer user identity is the first thing on the never-log list, in any form, and
    /// the local one is not a field the row names. What is held longest would otherwise be the
    /// record carrying exactly the data the rules forbid. An operator asking which peer user a
    /// local user is mapped to reads the mapping table, live, and is entitled to; the log
    /// answers that a mapping moved, who moved it and which way.
    /// </para>
    /// </remarks>
    private void Record(string pairingId, string administrator, MappingDirection direction)
    {
        if (_log.IsEnabled(LogLevel.Information))
        {
            _log.LogInformation(
                "A mapping was added, changed or removed by an administrator. Pairing: {PairingId}, administrator: {Administrator}, direction: {Direction}",
                pairingId,
                administrator,
                direction);
        }
    }
}
