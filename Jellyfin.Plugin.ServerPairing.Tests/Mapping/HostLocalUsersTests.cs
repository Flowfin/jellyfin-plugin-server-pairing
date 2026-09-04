using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.ServerPairing.Mapping;
using MediaBrowser.Controller.Library;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The one type that reads the host's user entity, against a substitute for the host's user
/// manager, through both spellings of the enumeration the supported lines carry.
/// </summary>
/// <remarks>
/// What is asserted is what the plugin makes of what the host answers. Whether a real host
/// answers the enumeration with its users is a reading of the server's source at four tags,
/// pasted at the type, and not a measurement made here.
/// </remarks>
public class HostLocalUsersTests
{
    /// <summary>
    /// The identifier is formatted the way the host formats its own claim for the same user:
    /// thirty-two hex characters, no separators. That is what makes a mapping's local user and
    /// an audit entry's administrator and this listing one spelling, matched by ordinal equality
    /// rather than by parsing.
    /// </summary>
    [Fact]
    public void TheIdentifierIsFormattedTheWayTheHostFormatsItsOwnClaim()
    {
        var id = Guid.NewGuid();
        var host = Substitute.For<IUserManager>();

        Answers(host, new User("anna", "provider", "reset") { Id = id });

        var user = Assert.Single(new HostLocalUsers(host).Users());

        Assert.Equal(id.ToString("N", CultureInfo.InvariantCulture), user.Id);
        Assert.Equal(32, user.Id.Length);
        Assert.DoesNotContain('-', user.Id);
        Assert.Equal("anna", user.Name);
    }

    /// <summary>
    /// A host with no users answers an empty list rather than a fault, and every user the host
    /// has is in the answer.
    /// </summary>
    [Fact]
    public void EveryUserTheHostHasIsInTheAnswerAndNoneIsInvented()
    {
        var host = Substitute.For<IUserManager>();

        Answers(host);

        Assert.Empty(new HostLocalUsers(host).Users());

        Answers(
            host,
            new User("anna", "provider", "reset") { Id = Guid.NewGuid() },
            new User("bea", "provider", "reset") { Id = Guid.NewGuid() });

        Assert.Equal("anna,bea", string.Join(",", new HostLocalUsers(host).Users().Select(user => user.Name).OrderBy(name => name, StringComparer.Ordinal)));
    }

    /// <summary>
    /// Nothing is cached. A user the host has since removed is not in the next answer, because
    /// a list held here would show a deleted user as unmapped rather than as gone.
    /// </summary>
    [Fact]
    public void NothingIsCachedBetweenTwoReads()
    {
        var host = Substitute.For<IUserManager>();
        var users = new HostLocalUsers(host);

        Answers(host, new User("anna", "provider", "reset") { Id = Guid.NewGuid() });

        Assert.Single(users.Users());

        Answers(host);

        Assert.Empty(users.Users());
    }

    /// <summary>
    /// The later tags of the 10.11 line, and the whole of the 12.0 line, carry the enumeration
    /// as a method, and the plugin reads through it. This is the arm the compile-time contract
    /// cannot reach: the project is pinned at the floor of the line the manifest promises, so
    /// the real contract carries the property and cannot be made to carry the method.
    /// </summary>
    [Fact]
    public void AHostCarryingTheEnumerationAsAMethodIsReadThroughIt()
    {
        var id = Guid.NewGuid();
        var host = new UsersAsAMethod(new[] { new User("anna", "provider", "reset") { Id = id } });

        var user = Assert.Single(HostLocalUsers.Of(host, typeof(ICarriesUsersAsAMethod)));

        Assert.Equal(id.ToString("N", CultureInfo.InvariantCulture), user.Id);
        Assert.Equal("anna", user.Name);
    }

    /// <summary>
    /// The floor under the two cases above: the two names are looked up on the contract rather
    /// than assumed, so a contract carrying neither is refused as a missing member rather than
    /// answered empty, which would read as a server with no users.
    /// </summary>
    /// <remarks>
    /// The real contract is asserted to carry exactly one of the two spellings rather than a
    /// named one. Which of them it is follows from the package the project is pinned at, and
    /// pinning it at the floor the manifest promises moved it from the method to the property on
    /// 2026-09-04; a test naming one spelling has to be rewritten every time that pin moves,
    /// while the plugin's own rule, that it reads through whichever the running host has, does
    /// not change with it.
    /// </remarks>
    [Fact]
    public void AContractCarryingNeitherSpellingIsRefusedRatherThanAnsweredEmpty()
    {
        var asMethod = typeof(IUserManager).GetMethod(HostLocalUsers.MethodSpelling, Type.EmptyTypes) is not null;
        var asProperty = typeof(IUserManager).GetProperty(HostLocalUsers.PropertySpelling) is not null;

        Assert.True(
            asMethod ^ asProperty,
            "The host contract carries the enumeration as a method or as a property and this one carries "
            + (asMethod && asProperty ? "both" : "neither") + ", so what the plugin reads through is unread here.");

        Assert.NotNull(typeof(ICarriesUsersAsAMethod).GetMethod(HostLocalUsers.MethodSpelling, Type.EmptyTypes));

        Assert.Throws<MissingMemberException>(() => HostLocalUsers.Of(new CarriesNeither(), typeof(ICarriesNeither)));
    }

    /// <summary>
    /// Tells a substitute for the host's user manager what its users are, through the spelling
    /// the contract this assembly was compiled against carries.
    /// </summary>
    /// <remarks>
    /// The two lines spell the enumeration differently and each target framework compiles
    /// against one of them: the 10.11 floor carries the property, the 12.0 packages carry the
    /// method. The plugin reads through whichever the running host has and needs no condition;
    /// a substitute can only be told through the one the compiler saw, so the condition lives
    /// here, once, rather than in each case above.
    /// </remarks>
    /// <param name="host">The substitute to tell.</param>
    /// <param name="users">The users it answers with.</param>
    private static void Answers(IUserManager host, params User[] users)
    {
#if NET9_0
        host.Users.Returns(users);
#else
        host.GetUsers().Returns(users);
#endif
    }

    /// <summary>
    /// What a server above the floor looks like to this plugin: the users as a method and no
    /// property, which is the shape at v10.11.9, v12.0-rc1 and v12.0-rc3 pasted at the type.
    /// </summary>
    internal interface ICarriesUsersAsAMethod
    {
        /// <summary>
        /// Gets the users.
        /// </summary>
        /// <returns>The users.</returns>
        IEnumerable<User> GetUsers();
    }

    /// <summary>
    /// A contract with no enumeration at all, so the lookup has nothing to find.
    /// </summary>
    internal interface ICarriesNeither
    {
        /// <summary>
        /// Gets a user by identifier, which is not an enumeration.
        /// </summary>
        /// <param name="id">The identifier.</param>
        /// <returns>The user, or null.</returns>
        User? GetUserById(Guid id);
    }

    private sealed class UsersAsAMethod : ICarriesUsersAsAMethod
    {
        private readonly IEnumerable<User> _users;

        public UsersAsAMethod(IEnumerable<User> users)
        {
            _users = users;
        }

        public IEnumerable<User> GetUsers() => _users;
    }

    private sealed class CarriesNeither : ICarriesNeither
    {
        public User? GetUserById(Guid id) => null;
    }
}
