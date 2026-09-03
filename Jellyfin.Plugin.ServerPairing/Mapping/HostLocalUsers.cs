using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// The users this server has, read from the host's own user manager.
/// </summary>
/// <remarks>
/// The one type in this plugin that touches the host's user entity, and it takes two members of
/// it: the identifier and the username. Both are read from the server's source at the floor of
/// each supported line and at the tag each line is built against, and the entity is the same
/// file at all four:
/// <code>
/// git grep -n 'public Guid Id\|public string Username' v10.11.0 v10.11.9 v12.0-rc1 v12.0-rc3 -- src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs
/// v10.11.0:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:64:        public Guid Id { get; set; }
/// v10.11.0:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:74:        public string Username { get; set; }
/// v10.11.9:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:64:        public Guid Id { get; set; }
/// v10.11.9:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:74:        public string Username { get; set; }
/// v12.0-rc1:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:65:        public Guid Id { get; set; }
/// v12.0-rc1:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:75:        public string Username { get; set; }
/// v12.0-rc3:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:65:        public Guid Id { get; set; }
/// v12.0-rc3:src/Jellyfin.Database/Jellyfin.Database.Implementations/Entities/User.cs:75:        public string Username { get; set; }
/// </code>
/// <para>
/// THE ENUMERATION IS BOUND AT RUN TIME, BECAUSE THE HOST RENAMED IT INSIDE ONE LINE WITHOUT
/// MOVING THE ASSEMBLY VERSION. At the floor of the 10.11 line the user manager exposes the
/// users as a property, and by the tag the line is built against it exposes them as a method
/// with a different name; the 12.0 line has only the method:
/// </para>
/// <code>
/// git grep -nE 'IEnumerable&lt;User&gt; ' v10.11.0 v10.11.9 v12.0-rc1 v12.0-rc3 -- MediaBrowser.Controller/Library/IUserManager.cs
/// v10.11.0:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; Users { get; }
/// v10.11.9:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; GetUsers();
/// v12.0-rc1:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; GetUsers();
/// v12.0-rc3:MediaBrowser.Controller/Library/IUserManager.cs:28:        IEnumerable&lt;User&gt; GetUsers();
/// </code>
/// <para>
/// A call compiled against either spelling is a missing member on a server carrying the other,
/// and the manifest's floor says this plugin installs on the first. The floor build in
/// <c>.github/abi-floor.sh</c> is what found it: the first version of this type called the
/// method and compiled against the tag, and the floor refused it. So <see cref="Of"/> asks the
/// contract for the method and then for the property, at the moment of the call, and reads
/// through whichever the running host has. That is the only reflection in this plugin and it is
/// held to two names, both declared as constants so a case can watch each one being found.
/// </para>
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
    /// <summary>
    /// The name of the method that enumerates the users, on hosts that have it as a method.
    /// </summary>
    public const string MethodSpelling = "GetUsers";

    /// <summary>
    /// The name of the property that enumerates the users, on hosts that have it as a property.
    /// </summary>
    public const string PropertySpelling = "Users";

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
    public IReadOnlyList<LocalUser> Users() => Of(_users, typeof(IUserManager));

    /// <summary>
    /// Every user a host has, read through whichever spelling of the enumeration its contract
    /// carries.
    /// </summary>
    /// <param name="host">The host's user manager.</param>
    /// <param name="contract">The contract the enumeration is looked up on.</param>
    /// <returns>The users, in the order the host gave them.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="MissingMemberException">The contract carries neither spelling.</exception>
    /// <exception cref="InvalidOperationException">The host answered with something that is not a sequence of users.</exception>
    /// <remarks>
    /// The contract is a parameter rather than <see cref="IUserManager"/> so that both arms can
    /// be reached by the suite: the compile-time contract carries the method, and a contract
    /// carrying only the property cannot be built out of it. The method is asked for first,
    /// because it is the spelling every tag this plugin is built against has, and the property
    /// is the floor's.
    /// </remarks>
    public static IReadOnlyList<LocalUser> Of(object host, Type contract)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(contract);

        var method = contract.GetMethod(MethodSpelling, BindingFlags.Public | BindingFlags.Instance, Type.EmptyTypes);
        var property = method is null ? contract.GetProperty(PropertySpelling, BindingFlags.Public | BindingFlags.Instance) : null;

        var answered = method is not null
            ? method.Invoke(host, null)
            : property is not null
                ? property.GetValue(host)
                : throw new MissingMemberException(contract.FullName, MethodSpelling);

        if (answered is not IEnumerable<User> users)
        {
            throw new InvalidOperationException("The host's user manager answered with something that is not a sequence of its users, so what users this server has is unknown.");
        }

        var found = new List<LocalUser>();

        foreach (var user in users)
        {
            found.Add(new LocalUser(user.Id.ToString("N", CultureInfo.InvariantCulture), user.Username));
        }

        return found;
    }
}
