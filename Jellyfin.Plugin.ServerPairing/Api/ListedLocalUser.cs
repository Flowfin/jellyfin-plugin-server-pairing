using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// One user on this server, as the listing of a pairing's mapping table names them.
/// </summary>
/// <remarks>
/// The identifier is what a mapping would hold for them and the name is what the host holds
/// for them at the moment of the listing, so a dashboard offering a local user to map shows
/// the name and sends the identifier. Both are read from the host through
/// <see cref="Mapping.ILocalUsers"/> and neither is kept.
/// </remarks>
/// <param name="LocalUserId">The identifier the host gives the user, which is what a mapping holds.</param>
/// <param name="LocalUserName">The username the host holds for them.</param>
public sealed record ListedLocalUser(
    [property: JsonPropertyName("localUserId")] string LocalUserId,
    [property: JsonPropertyName("localUserName")] string LocalUserName);
