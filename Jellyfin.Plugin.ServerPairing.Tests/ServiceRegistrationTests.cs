using System;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Runs the plugin's registrator against a container built in this process, with the
/// server's host interface replaced by a substitute, so a registration that cannot be
/// satisfied fails here rather than on somebody's server at plugin load.
/// </summary>
public class ServiceRegistrationTests
{
    /// <summary>
    /// The server discovers the registrator by scanning the plugin assembly for this
    /// interface and constructs it with no arguments. A registrator with a constructor
    /// parameter, or one that is not public, is not found and the plugin loads with none
    /// of its services present.
    /// </summary>
    [Fact]
    public void RegistratorIsDiscoverableAndConstructibleTheWayTheServerDoesIt()
    {
        var found = typeof(Plugin).Assembly.GetTypes()
            .Where(t => typeof(IPluginServiceRegistrator).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .ToArray();

        var registratorType = Assert.Single(found);
        Assert.True(registratorType.IsPublic);
        Assert.NotNull(registratorType.GetConstructor(Type.EmptyTypes));
        Assert.NotNull(Activator.CreateInstance(registratorType));
    }

    /// <summary>
    /// Every service the plugin registers is resolvable without a running server. What is
    /// substituted is what the server supplies: the host interface it passes into the
    /// registrator, and the paths it registers in its own container, which the key store
    /// derives its file from.
    /// </summary>
    [Fact]
    public void EveryServiceTheRegistratorAddsResolvesWithoutAServer()
    {
        var services = new ServiceCollection();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(System.IO.Path.GetTempPath());
        services.AddSingleton(paths);

        // The other thing the server supplies. Its container is a generic host's with Serilog
        // on it, and the plugin registrators run inside a ConfigureServices callback on that
        // same builder, read at the two lines this plugin builds against rather than assumed:
        //
        //     gh api -H "Accept: application/vnd.github.raw" \
        //       "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Program.cs?ref=v10.11.9" \
        //       | grep -nE 'CreateDefaultBuilder|Init\(services\)|UseSerilog\(\)'
        //     168:                _jellyfinHost = Host.CreateDefaultBuilder()
        //     170:                    .ConfigureServices(services => appHost.Init(services))
        //     181:                    .UseSerilog()
        //
        // The same three at v12.0-rc3 are 169, 171 and 182. That the builder registers the
        // logging services is the generic host's own behaviour rather than something read out
        // of the server's tree.
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        AssertEveryRegisteredServiceResolves(services);
    }

    /// <summary>
    /// The allowances the peer plane is given are the ones the configuration carries. The
    /// numbers here are none of the defaults, so a registration that built the limit from
    /// nothing would answer with the defaults and this would redden.
    ///
    /// It is the only assertion that the settings reach the object that uses them. Everything
    /// else about them is proved at the reading, and a reading nobody hands to the plane is
    /// three numbers in a file.
    /// </summary>
    [Fact]
    public void ThePeerPlaneIsGivenTheAllowancesTheConfigurationCarries()
    {
        var services = new ServiceCollection();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(System.IO.Path.GetTempPath());
        services.AddSingleton(paths);
        services.AddLogging();

        // Registered before the registrator runs, which is what the TryAdd in it is for.
        services.AddSingleton(new PluginConfiguration
        {
            PeerPlaneWindowSeconds = 30,
            PeerPlaneArrivalsPerPairing = 9,
            PeerPlaneArrivalsPerEnrolment = 3
        });

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();

        var limit = provider.GetRequiredService<ArrivalLimit>();

        Assert.Equal(30, limit.CountedOverSeconds);
        Assert.Equal(9, limit.PerPairing);
        Assert.Equal(3, limit.PerEnrolment);
    }

    /// <summary>
    /// One limit per server rather than one per caller. A limit held per caller is no limit:
    /// what it counts lives in the object, so a second instance hands every flood a second
    /// allowance.
    /// </summary>
    [Fact]
    public void ThePeerPlaneGetsOneLimitRatherThanOnePerCaller()
    {
        var services = new ServiceCollection();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(System.IO.Path.GetTempPath());
        services.AddSingleton(paths);
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<ArrivalLimit>(), provider.GetRequiredService<ArrivalLimit>());
    }

    /// <summary>
    /// One freshness window per server rather than one per caller. The reason is stronger than
    /// the one above it: what this object holds is the nonces already seen, so a second instance
    /// remembers none of them and every replay is fresh to it.
    /// </summary>
    /// <remarks>
    /// A per-caller window is not a weaker replay guard, it is no replay guard, and nothing about
    /// the plane's own behaviour would say so - every case driving one plane holds one window and
    /// passes either way. This is the only assertion in the tree that the server gets one.
    /// </remarks>
    [Fact]
    public void ThePeerPlaneGetsOneFreshnessWindowRatherThanOnePerCaller()
    {
        var services = new ServiceCollection();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(System.IO.Path.GetTempPath());
        services.AddSingleton(paths);
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredService<FreshnessWindow>(),
            provider.GetRequiredService<FreshnessWindow>());
    }

    /// <summary>
    /// The skew an operator set is the skew a verified request is judged against. Without this
    /// the setting is a number that is read, refused out of range, and handed to a window the
    /// plane never sees.
    /// </summary>
    [Fact]
    public void ThePeerPlaneIsGivenTheSkewTheConfigurationCarries()
    {
        var services = new ServiceCollection();

        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(System.IO.Path.GetTempPath());
        services.AddSingleton(paths);
        services.AddLogging();

        // Registered before the registrator runs, which is what the TryAdd in it is for. The
        // value is none of the defaults, so a window built on the default would fail here.
        services.AddSingleton(new PluginConfiguration { TimestampWindowSeconds = 42 });

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();

        Assert.Equal(42, provider.GetRequiredService<FreshnessWindow>().AcceptedSkewSeconds);
    }

    /// <summary>
    /// The check above runs over whatever the registrator added, so it is empty either
    /// because everything resolved or because there was nothing to resolve, and the result
    /// does not say which. This is the same check against a registrator that adds a service
    /// whose dependency nobody registered, and it has to fail. Delete the loop in
    /// <see cref="AssertEveryRegisteredServiceResolves"/> and this test goes red, which is
    /// the only reason to trust the one above.
    /// </summary>
    [Fact]
    public void TheResolutionCheckRefusesAServiceWhoseDependencyIsMissing()
    {
        var services = new ServiceCollection();

        new UnsatisfiableRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        Assert.ThrowsAny<InvalidOperationException>(() => AssertEveryRegisteredServiceResolves(services));
    }

    /// <summary>
    /// Builds the container and asks it for every service type that was registered, in a
    /// scope, so a scoped registration is resolved the way the server would resolve it.
    /// </summary>
    /// <param name="services">The collection the registrator was run against.</param>
    private static void AssertEveryRegisteredServiceResolves(IServiceCollection services)
    {
        var descriptors = services.ToArray();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true
        });

        using var scope = provider.CreateScope();

        foreach (var descriptor in descriptors)
        {
            if (descriptor.ServiceType.IsGenericTypeDefinition)
            {
                continue;
            }

            Assert.NotNull(scope.ServiceProvider.GetRequiredService(descriptor.ServiceType));
        }
    }

    /// <summary>
    /// A registrator that adds a service the container cannot build, used only to prove the
    /// check above refuses one. It is not registered anywhere and the server never sees it,
    /// because it lives in the test assembly.
    /// </summary>
    private sealed class UnsatisfiableRegistrator : IPluginServiceRegistrator
    {
        public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
        {
            serviceCollection.AddSingleton<NeedsSomethingNobodyRegistered>();
        }
    }

    /// <summary>
    /// Takes a dependency that is deliberately absent from the container.
    /// </summary>
    private sealed class NeedsSomethingNobodyRegistered
    {
        public NeedsSomethingNobodyRegistered(IDisposable missing)
        {
            Missing = missing;
        }

        public IDisposable Missing { get; }
    }
}
