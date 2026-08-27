using System;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing;

/// <summary>
/// Registers this plugin's services with the server's container.
/// </summary>
/// <remarks>
/// The server finds this type by scanning the plugin assembly for an implementation of
/// <see cref="IPluginServiceRegistrator"/> and constructs it with a parameterless
/// constructor, so it has no dependencies of its own. Every service this plugin adds is
/// registered here and nowhere else. Nothing resolves a service from a static, because a
/// static is a service a test cannot replace and a second instance cannot have its own of.
/// </remarks>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // What the plugin makes of the configuration the host handed it, read fresh on every
        // resolve rather than once: an operator saves the settings page while the server is
        // running, and a singleton here would answer with what was on disk at boot.
        //
        // The configuration is reached through the instance the base class sets, which is the
        // one static this assembly has and is the host's own way of handing a plugin its
        // settings. It is read here, in the composition root, and nowhere else. A server that
        // has not constructed the plugin yet, which is every test that builds this container,
        // gets the same object a fresh installation gets rather than a null.
        serviceCollection.AddTransient(_ => ConfigurationReading.Of(
            Plugin.Instance?.Configuration ?? new PluginConfiguration()));

        // The one thing that says a setting was refused. Without it a refused configuration is
        // a plugin that is loaded, will not pair, and has told nobody why.
        serviceCollection.AddHostedService<ConfigurationAtStartup>();

        // The outbound side, once, with the handler the plugin runs against a real peer. It
        // is registered here so that the client carrying the timeouts and the redirect
        // refusal is the client every caller gets, rather than one each caller builds.
        serviceCollection.AddSingleton(_ => new PeerChannel(PeerChannel.CreateHandler()));

        // The inbound side. The controller is found by the host's own scan of this assembly
        // and is constructed from the container, so what it needs is registered here or the
        // five paths answer with a server error instead of a refusal.
        //
        // The key source is the one this tree can honestly supply: there is no key store, so
        // nothing arriving verifies. Issue #30 is where that changes, and this line is what
        // it replaces.
        serviceCollection.AddSingleton<IPairingKeySource, NoPairingKeys>();
        serviceCollection.AddSingleton(services => new RequestAuthenticator(services.GetRequiredService<IPairingKeySource>()));

        // Once, because a limit held per caller is no limit. What it counts lives in this
        // object, so a second instance would hand every flood a second allowance.
        serviceCollection.AddSingleton(_ => new ArrivalLimit());
        serviceCollection.AddSingleton(services => new PeerPlane(
            services.GetRequiredService<RequestAuthenticator>(),
            services.GetRequiredService<ArrivalLimit>()));

        // The one place in this plugin that reads a real clock. Everything downstream judges
        // at an instant handed in, so a test moves time by handing in a different one, and
        // ClockSourceTests refuses a second site from reading the wall clock. TryAdd rather
        // than Add: the host may already have registered one, and two clocks in a container is
        // two answers to what time it is.
        serviceCollection.TryAddSingleton(TimeProvider.System);

        // The key store, over the file the host's own paths put it at. The path is derived
        // from IApplicationPaths rather than written down, so a server whose data directory is
        // somewhere unusual is served without a setting, and the file is nowhere near the
        // directory the host writes plugin configurations into.
        //
        // Nothing on the request path resolves this yet. What would is the request
        // authenticator, and reaching a store whose every read takes an instant needs the
        // injected clock, which is issue #26. It is registered here rather than when that
        // arrives so that a server carries one store rather than one per caller.
        //
        // The logger is handed in for one line only, which is the one saying a store written by
        // an older build has been carried up to the format this one reads. That is the single
        // thing the store does that nobody asked it for, and it leaves a second file holding key
        // material beside the first.
        serviceCollection.AddSingleton<IPairingKeyStore>(services =>
            new FilePairingKeyStore(
                KeyStorePath.FileFor(services.GetRequiredService<IApplicationPaths>()),
                null,
                services.GetRequiredService<ILogger<FilePairingKeyStore>>()));

        // The one thing that runs on its own rather than answering a caller. It reads the
        // store once at startup and says what survived, because a store outside the plugin
        // directory outlives an uninstall and a reinstall comes up paired with whatever it was
        // paired with before.
        //
        // A hosted service added here is started by the server's own host: the plugin
        // registrators run inside appHost.Init, which is a ConfigureServices callback on the
        // generic host that Jellyfin.Server builds, read at the two lines this plugin builds
        // against rather than assumed:
        //
        //     gh api -H "Accept: application/vnd.github.raw" \
        //       "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Program.cs?ref=v10.11.9" \
        //       | grep -nE 'CreateDefaultBuilder|Init\(services\)'
        //     168:                _jellyfinHost = Host.CreateDefaultBuilder()
        //     170:                    .ConfigureServices(services => appHost.Init(services))
        //
        // The same two at v12.0-rc3 are 169 and 171. That a generic host starts what is
        // registered as IHostedService is the host's own behaviour rather than something read
        // out of the server's tree, and no run on a server has been made to watch it.
        serviceCollection.AddHostedService<StoreAtStartup>();
    }
}
