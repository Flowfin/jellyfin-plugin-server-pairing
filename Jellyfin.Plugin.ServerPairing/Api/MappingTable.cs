using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ServerPairing.Mapping;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// A pairing's mapping table as an administrator is shown it: every mapping, and every local
/// user who has none.
/// </summary>
/// <remarks>
/// Two lists rather than one, because they answer two questions an operator asks at different
/// moments. The first is who is mapped to whom, which is the table itself. The second is who is
/// NOT mapped, which is the question an operator asks when somebody's history is not moving,
/// and issue #40 is explicit that they should not have to answer it by subtraction. An unmapped
/// user is not synced, silently and by default, so the silence has to be visible somewhere, and
/// this is where.
/// <para>
/// THE SECOND LIST IS THE HOST'S USER SET MINUS THE FIRST, AND THAT IS ALL IT IS. Nothing here
/// suggests a peer user for anybody on it; a suggestion would need the peer's user list, which is
/// a protocol operation this plugin cannot make yet, and would have to be marked as a suggestion
/// on the page rather than folded into an answer. What is listed is the set an administrator
/// chooses from, in the words the host uses for its users.
/// </para>
/// </remarks>
/// <param name="PairingId">The pairing the table belongs to.</param>
/// <param name="Mappings">Every mapping held under it, ordered by how the local user is shown.</param>
/// <param name="UnmappedLocalUsers">Every user this server has who is not mapped under it, ordered by name.</param>
public sealed record MappingTable(
    [property: JsonPropertyName("pairingId")] string PairingId,
    [property: JsonPropertyName("mappings")] IReadOnlyList<ListedMapping> Mappings,
    [property: JsonPropertyName("unmappedLocalUsers")] IReadOnlyList<ListedLocalUser> UnmappedLocalUsers)
{
    /// <summary>
    /// The table for one pairing, out of what the mapping store holds under it and what the host
    /// says this server's users are.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="held">The mappings the store holds under that pairing.</param>
    /// <param name="localUsers">The users this server has.</param>
    /// <returns>The table.</returns>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <remarks>
    /// A mapping and a user are matched by ordinal equality of the identifier and nothing else.
    /// The model calls both identifiers opaque, so nothing here parses one; what makes the match
    /// work is that <see cref="HostLocalUsers"/> formats the host's identifier the one way the
    /// host itself formats it.
    /// </remarks>
    public static MappingTable Of(string pairingId, IReadOnlyList<UserMapping> held, IReadOnlyList<LocalUser> localUsers)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(held);
        ArgumentNullException.ThrowIfNull(localUsers);

        var nameOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var user in localUsers)
        {
            nameOf[user.Id] = user.Name;
        }

        var mappings = new List<ListedMapping>();
        var mapped = new HashSet<string>(StringComparer.Ordinal);

        foreach (var mapping in held)
        {
            mapped.Add(mapping.LocalUserId);

            // A local user the host no longer has is the third rule of issue #37: detected and
            // reported, never repaired by guessing. The report is the flag and not the empty
            // name, so a page never has to read an absence as a problem.
            var exists = nameOf.TryGetValue(mapping.LocalUserId, out var name);
            var localName = exists ? name! : string.Empty;

            mappings.Add(new ListedMapping(
                mapping.LocalUserId,
                localName,
                ShownAs(localName, mapping.LocalUserId),
                exists,
                mapping.PeerUserId,
                mapping.PeerDisplayName,
                ShownAs(mapping.PeerDisplayName, mapping.PeerUserId)));
        }

        var unmapped = localUsers
            .Where(user => !mapped.Contains(user.Id))
            .Select(user => new ListedLocalUser(user.Id, user.Name))
            .OrderBy(user => user.LocalUserName, StringComparer.Ordinal)
            .ThenBy(user => user.LocalUserId, StringComparer.Ordinal)
            .ToArray();

        return new MappingTable(
            pairingId,
            mappings
                .OrderBy(mapping => mapping.LocalUserShownAs, StringComparer.Ordinal)
                .ThenBy(mapping => mapping.LocalUserId, StringComparer.Ordinal)
                .ToArray(),
            unmapped);
    }

    /// <summary>
    /// The rule issue #40 states for a cell: the name where there is one, the identifier where
    /// there is not, and never an empty cell.
    /// </summary>
    /// <param name="name">The name, which may be empty.</param>
    /// <param name="id">The identifier standing in for it.</param>
    /// <returns>What a page shows.</returns>
    private static string ShownAs(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;
}
