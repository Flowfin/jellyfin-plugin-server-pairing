using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// One mapping in a pairing's table, as an administrator sees it listed.
/// </summary>
/// <remarks>
/// This is the first thing issue #40 asks the surface to do: list every mapping for a pairing,
/// showing the local user and the cached peer display name, with an unset or stale cache shown
/// as the identifier rather than as an empty cell. The two <c>ShownAs</c> members are that rule
/// executed here rather than left to whichever page renders the answer, so the rule is a
/// property of the listing and is asserted as one, and a page that renders
/// <see cref="PeerUserShownAs"/> as text has obeyed it without knowing it.
/// <para>
/// THE CACHE IS STILL SAID TO BE A CACHE, by the name of the member that carries it, for the
/// reason <see cref="HeldMapping"/> gives: it is a copy the peer may change or withdraw at any
/// moment, nothing is decided from it, and it is never the identity a request is authorised
/// against. It is carried beside the identifier rather than folded into <c>ShownAs</c> alone,
/// so a reader can tell a name from an identifier standing in for one.
/// </para>
/// <para>
/// The local side gets the same treatment for the neighbouring reason. A mapping names a local
/// user by identifier, and the host may no longer have a user with that identifier, which
/// <c>docs/mapping.md</c> says nothing refuses. Such a mapping is listed by its identifier
/// with an empty name rather than dropped, because a mapping the operator cannot see is a
/// mapping the operator cannot remove, and it is REPORTED rather than only shown:
/// <see cref="LocalUserExists"/> is false for it and for nothing else, which is the third
/// rule of issue #37 as a property of the listing. An empty name would carry the same fact,
/// and it is not left to carry it, because a page reading an empty name as a problem is a
/// page guessing, and the rule that fact serves is that nothing here is repaired by guessing.
/// </para>
/// <para>
/// Who decided the mapping and when are on the record and are not here, for the reason they
/// are not on <see cref="HeldMapping"/>: the listing is the table as it stands, and the trail
/// of who changed it is the log's.
/// </para>
/// </remarks>
/// <param name="LocalUserId">The user on this server, as the mapping holds them.</param>
/// <param name="LocalUserName">The username the host holds for that user, empty where the host no longer has them.</param>
/// <param name="LocalUserShownAs">The name, or the identifier where the host holds no name.</param>
/// <param name="LocalUserExists">Whether this server still has a user with that identifier; false is the reported problem.</param>
/// <param name="PeerUserId">The opaque identifier of the user on the peer.</param>
/// <param name="CachedPeerDisplayName">The peer's display name for that user as this server last cached it, empty where none was sent.</param>
/// <param name="PeerUserShownAs">The cached name, or the identifier where the cache holds nothing.</param>
public sealed record ListedMapping(
    [property: JsonPropertyName("localUserId")] string LocalUserId,
    [property: JsonPropertyName("localUserName")] string LocalUserName,
    [property: JsonPropertyName("localUserShownAs")] string LocalUserShownAs,
    [property: JsonPropertyName("localUserExists")] bool LocalUserExists,
    [property: JsonPropertyName("peerUserId")] string PeerUserId,
    [property: JsonPropertyName("cachedPeerDisplayName")] string CachedPeerDisplayName,
    [property: JsonPropertyName("peerUserShownAs")] string PeerUserShownAs);
