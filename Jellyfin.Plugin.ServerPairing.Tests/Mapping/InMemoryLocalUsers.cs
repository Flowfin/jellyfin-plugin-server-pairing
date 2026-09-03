using System.Collections.Generic;
using Jellyfin.Plugin.ServerPairing.Mapping;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The users a server has, held in memory for the suite, so a case can say who exists on this
/// side without the host's entity types.
/// </summary>
internal sealed class InMemoryLocalUsers : ILocalUsers
{
    private readonly List<LocalUser> _users = new List<LocalUser>();

    /// <summary>
    /// Adds a user this server has.
    /// </summary>
    /// <param name="id">The identifier, as the host would format it.</param>
    /// <param name="name">The username.</param>
    /// <returns>This, so a case can chain.</returns>
    public InMemoryLocalUsers With(string id, string name)
    {
        _users.Add(new LocalUser(id, name));

        return this;
    }

    /// <inheritdoc />
    public IReadOnlyList<LocalUser> Users() => _users.ToArray();
}
