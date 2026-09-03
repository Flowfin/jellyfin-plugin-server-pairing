using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// One mapping held for a local user, as an administrator is told about it when they ask what
/// this plugin holds about that person.
/// </summary>
/// <remarks>
/// This is the report half of issue #60. An operator running a server for a household may be
/// asked by one of its users what is held about them, and the answer has to be readable rather
/// than a file somebody opens by hand. <c>docs/data.md</c> fixes what the report covers, and
/// the four members here are those three bullets and nothing beyond them: the mapping, which is
/// one local identifier and one opaque peer identifier; the cached peer display name beside it;
/// and the pairing the mapping belongs to.
/// <para>
/// THE DISPLAY NAME IS SAID TO BE A CACHE IN THE OUTPUT ITSELF, by the name of the member rather
/// than by a sentence beside it. It is a copy the peer may change or withdraw at any moment,
/// nothing is decided from it, and it is never the identity a request is authorised against. It
/// is still in the report, because it is the only field in the mapping table that names a
/// person: a report listing the two opaque identifiers and dropping the readable name beside
/// them is a report of the fields rather than of the data, which <c>docs/data.md</c> says in
/// as many words.
/// </para>
/// <para>
/// Who decided the mapping and when are on the record and are not here. They are data about the
/// administrator rather than about the user the report is for, and the scope this shape obeys is
/// the document's three bullets rather than the record's member list.
/// </para>
/// </remarks>
/// <param name="PairingId">The pairing the mapping belongs to.</param>
/// <param name="LocalUserId">The user on this server the report is about.</param>
/// <param name="PeerUserId">The opaque identifier of the user on the peer.</param>
/// <param name="CachedPeerDisplayName">
/// The peer's display name for that user as this server last cached it. It may be empty, because
/// a peer that sends no display name is not a reason to refuse a mapping an administrator asked
/// for, and an empty cache is reported as empty rather than dropped.
/// </param>
public sealed record HeldMapping(
    [property: JsonPropertyName("pairingId")] string PairingId,
    [property: JsonPropertyName("localUserId")] string LocalUserId,
    [property: JsonPropertyName("peerUserId")] string PeerUserId,
    [property: JsonPropertyName("cachedPeerDisplayName")] string CachedPeerDisplayName);
