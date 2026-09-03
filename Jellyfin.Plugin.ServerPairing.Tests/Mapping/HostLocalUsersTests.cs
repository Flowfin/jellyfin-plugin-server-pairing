using System;
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
/// manager.
/// </summary>
/// <remarks>
/// What is asserted is what the plugin makes of what the host answers. Whether a real host
/// answers <c>GetUsers()</c> with its users is a reading of the server's source at two tags,
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
}
