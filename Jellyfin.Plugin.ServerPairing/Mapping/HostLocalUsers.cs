using System;
using System.Collections.Generic;
using System.Globalization;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// The users this server has, read from the host's own user manager.
/// </summary>
/// <remarks>
/// The one type in this plugin that touches the host's user entity, and it takes two members of
/// it: the identifier and the username. Both are read from the server's source at both supported
/// tags rather than assumed, and the entity is the same file at both:
/// <code>
/// git grep -n 'IEnumerable&lt;User&gt; GetUsers();' v10.11.9 v12.0-rc3 -- MediaBrowser.Controller/Library/IUserManager.cs
/// v10.11.9:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; GetUsers();
/// v12.0-rc3:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; GetUsers();
///
/// git grep -n 'public Guid Id\|public string Username' v10.11.9 v12.0-rc3 -- src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs
/// v10.11.9:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:64:        public Guid Id { get; set; }
/// v10.11.9:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:74:        public string Username { get; set; }
/// v12.0-rc3:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:65:        public Guid Id { get; set; }
/// v12.0-rc3:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:75:        public string Username { get; set; }
/// </code>
/// <para>
/// THE IDENTIFIER IS FORMATTED THE WAY THE HOST FORMATS ITS OWN CLAIM FOR THE SAME USER, which is
/// thirty-two hex characters with no separators, read at <see cref="Api.RequestingAdministrator"/>
/// from the host's authentication handler. So the string an audit entry names an administrator
/// by, the string a mapping holds for a local user and the string this hands back are one
/// spelling, and a mapping is matched to a user by ordinal equality rather than by parsing a
/// GUID out of an identifier the model calls opaque.
/// </para>
/// <para>
/// Nothing is cached. The host's user set changes whenever an operator adds or removes a user,
/// and a list held here would show a user the host has deleted as unmapped rather than as gone.
/// </para>
/// </remarks>
public sealed class HostLocalUsers : ILocalUsers
{
    private readonly IUserManager _users;

    /// <summary>
    /// Initializes a new instance of the <see cref="HostLocalUsers"/> class.
    /// </summary>
    /// <param name="users">The host's user manager.</param>
    /// <exception cref="ArgumentNullException">The user manager is null.</exception>
    public HostLocalUsers(IUserManager users)
    {
        _users = users ?? throw new ArgumentNullException(nameof(users));
    }

    /// <inheritdoc />
    public IReadOnlyList<LocalUser> Users()
    {
        var found = new List<LocalUser>();

        foreach (var user in _users.GetUsers())
        {
            found.Add(new LocalUser(user.Id.ToString("N", CultureInfo.InvariantCulture), user.Username));
        }

        return found;
    }
}
