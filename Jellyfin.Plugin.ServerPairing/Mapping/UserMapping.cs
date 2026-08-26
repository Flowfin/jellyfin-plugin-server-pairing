using System;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// One administrator's statement that a user here is a user on the peer.
/// </summary>
/// <remarks>
/// Both identifiers are opaque to this plugin. It never parses them, compares them to each
/// other, or derives one from the other, because the whole point of this type is that the
/// correspondence was decided by a person rather than worked out from the data. Two servers
/// sharing a household will have two different people called <c>dad</c>, and a plugin that
/// matches on names puts one person's history on the other person's account.
/// <para>
/// <see cref="PeerDisplayName"/> is a cache and is never the truth about who the peer user
/// is. It exists so the dashboard can show a person something they recognise beside an
/// opaque identifier, it may be discarded at any moment, and it is personal data sitting
/// next to a table that deliberately holds none. It goes when the mapping goes.
/// </para>
/// <para>
/// A mapping belongs to exactly one pairing and cannot outlive it. What enforces that is
/// <see cref="UserMappings"/> on the way in and
/// <see cref="Protocol.PairingStateMachine"/> on the way out.
/// </para>
/// </remarks>
public sealed class UserMapping
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserMapping"/> class.
    /// </summary>
    /// <param name="pairingId">The pairing this mapping belongs to.</param>
    /// <param name="localUserId">The user on this server.</param>
    /// <param name="peerUserId">The user on the peer.</param>
    /// <param name="peerDisplayName">The peer's display name for that user, cached for the dashboard.</param>
    /// <param name="actor">The administrator who decided this.</param>
    /// <param name="at">When they decided it.</param>
    /// <exception cref="ArgumentException">Any of the four strings is null, empty or blank.</exception>
    public UserMapping(
        string pairingId,
        string localUserId,
        string peerUserId,
        string peerDisplayName,
        string actor,
        DateTimeOffset at)
    {
        PairingId = Required(pairingId, nameof(pairingId));
        LocalUserId = Required(localUserId, nameof(localUserId));
        PeerUserId = Required(peerUserId, nameof(peerUserId));
        PeerDisplayName = peerDisplayName ?? throw new ArgumentNullException(nameof(peerDisplayName));
        Actor = Required(actor, nameof(actor));
        At = at;
    }

    /// <summary>
    /// Gets the pairing this mapping belongs to.
    /// </summary>
    public string PairingId { get; }

    /// <summary>
    /// Gets the user on this server.
    /// </summary>
    public string LocalUserId { get; }

    /// <summary>
    /// Gets the user on the peer.
    /// </summary>
    public string PeerUserId { get; }

    /// <summary>
    /// Gets the peer's display name for that user.
    /// </summary>
    /// <remarks>
    /// A cache for the dashboard to read and never an identifier. Nothing decides anything
    /// from this value, and it is allowed to be empty, because a peer that sends no display
    /// name is not a reason to refuse a mapping an administrator asked for.
    /// </remarks>
    public string PeerDisplayName { get; }

    /// <summary>
    /// Gets the administrator who decided this mapping.
    /// </summary>
    public string Actor { get; }

    /// <summary>
    /// Gets when the decision was made.
    /// </summary>
    public DateTimeOffset At { get; }

    private static string Required(string value, string name)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A mapping needs a " + name + ".", name)
            : value;
}
