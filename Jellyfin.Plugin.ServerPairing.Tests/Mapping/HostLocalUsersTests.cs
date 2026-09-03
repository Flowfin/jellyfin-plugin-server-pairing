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

        host.GetUsers().Returns(new[] { new User("anna", "provider", "reset") { Id = id } });

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

        host.GetUsers().Returns(Array.Empty<User>());

        Assert.Empty(new HostLocalUsers(host).Users());

        host.GetUsers().Returns(new[]
        {
            new User("anna", "provider", "reset") { Id = Guid.NewGuid() },
            new User("bea", "provider", "reset") { Id = Guid.NewGuid() },
        });

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

        host.GetUsers().Returns(new[] { new User("anna", "provider", "reset") { Id = Guid.NewGuid() } });

        Assert.Single(users.Users());

        host.GetUsers().Returns(Array.Empty<User>());

        Assert.Empty(users.Users());
    }

    /// <summary>
    /// The floor of the 10.11 line carries the enumeration as a property, and the plugin reads
    /// through it. This is the arm the floor build found missing, reached through a contract that
    /// carries only the property, because the compile-time contract carries the method and cannot
    /// be made not to.
    /// </summary>
    [Fact]
    public void AHostCarryingTheEnumerationAsAPropertyIsReadThroughIt()
    {
        var id = Guid.NewGuid();
        var host = new UsersAsAProperty(new[] { new User("anna", "provider", "reset") { Id = id } });

        var user = Assert.Single(HostLocalUsers.Of(host, typeof(ICarriesUsersAsAProperty)));

        Assert.Equal(id.ToString("N", CultureInfo.InvariantCulture), user.Id);
        Assert.Equal("anna", user.Name);
    }

    /// <summary>
    /// The method is what the tags this plugin is built against carry, and it is the spelling
    /// read on the real contract. The floor under the two cases above: the two names are looked
    /// up on the contract rather than assumed, so a contract carrying neither is refused as a
    /// missing member rather than answered empty, which would read as a server with no users.
    /// </summary>
    [Fact]
    public void AContractCarryingNeitherSpellingIsRefusedRatherThanAnsweredEmpty()
    {
        Assert.NotNull(typeof(IUserManager).GetMethod(HostLocalUsers.MethodSpelling, Type.EmptyTypes));
        Assert.Null(typeof(IUserManager).GetProperty(HostLocalUsers.PropertySpelling));
        Assert.NotNull(typeof(ICarriesUsersAsAProperty).GetProperty(HostLocalUsers.PropertySpelling));

        Assert.Throws<MissingMemberException>(() => HostLocalUsers.Of(new CarriesNeither(), typeof(ICarriesNeither)));
    }

    /// <summary>
    /// What the floor's user manager looks like to this plugin: the users as a property and no
    /// method, which is the shape at v10.11.0 pasted at the type.
    /// </summary>
    internal interface ICarriesUsersAsAProperty
    {
        /// <summary>
        /// Gets the users.
        /// </summary>
        IEnumerable<User> Users { get; }
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

    private sealed class UsersAsAProperty : ICarriesUsersAsAProperty
    {
        public UsersAsAProperty(IEnumerable<User> users)
        {
            Users = users;
        }

        public IEnumerable<User> Users { get; }
    }

    private sealed class CarriesNeither : ICarriesNeither
    {
        public User? GetUserById(Guid id) => null;
    }
}
