using System;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
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
        // The settings themselves. They are reached through the instance the base class sets,
        // which is the one static this assembly has and is the host's own way of handing a
        // plugin its settings. That happens here, in the composition root, and nowhere else.
        // A server that has not constructed the plugin yet gets the same object a fresh
        // installation gets rather than a null.
        //
        // TryAdd rather than Add, so a caller that supplied a configuration keeps it. That is
        // what lets the wiring below be proved with allowances that are none of the defaults;
        // without it, a container built without a server can only ever see one configuration
        // and every assertion about what reaches the plane is an assertion about the default.
        serviceCollection.TryAddSingleton(_ => Plugin.Instance?.Configuration ?? new PluginConfiguration());

        // What the plugin makes of those settings, read fresh on every resolve rather than
        // once: an operator saves the settings page while the server is running, and a
        // singleton here would answer with what was on disk at boot.
        serviceCollection.AddTransient(services => ConfigurationReading.Of(
            services.GetRequiredService<PluginConfiguration>()));

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
        // The key source reads the store registered below, so a pairing that has a key can be
        // verified. What has never put a key into that store is the enrolment, which is issue
        // #18: a server's store is empty until one runs, and an empty store refuses everything
        // for want of a key rather than for want of a lookup.
        serviceCollection.AddSingleton<IPairingKeySource>(services =>
            new StoreBackedKeys(services.GetRequiredService<IPairingKeyStore>()));
        serviceCollection.AddSingleton(services => new RequestAuthenticator(services.GetRequiredService<IPairingKeySource>()));

        // Once, because a limit held per caller is no limit. What it counts lives in this
        // object, so a second instance would hand every flood a second allowance. The
        // allowances it runs on are the operator's, read through the same reading every other
        // setting comes through, so a refused allowance is named at Error and the plane runs on
        // the one a server nobody configured runs on rather than on no limit at all.
        serviceCollection.AddSingleton(services =>
            services.GetRequiredService<ConfigurationReading>().NewArrivalLimit());

        // Once, for the same reason the limit above is once: what it holds is a count over the
        // whole server, and a second instance would hand the diagnostics action a number taken
        // from a plane nobody is talking to. The peer plane writes into it and the
        // administrative plane reads it, which is the only path between the two planes and
        // carries numbers rather than anything a caller supplied.
        serviceCollection.AddSingleton<RefusalCounters>();

        // Once, and for a reason that is not the same as the two above even though the word is.
        // What this holds is the nonces already seen for each pairing, so a second instance
        // remembers none of them and every replay it judges is fresh to it. A per-caller
        // freshness window is not a weaker limit, it is no limit. The span it runs on is the
        // operator's, read through the same reading every other setting comes through, so a
        // refused skew is named at Error and the plane runs on the span a server nobody
        // configured runs on.
        serviceCollection.AddSingleton(services =>
            services.GetRequiredService<ConfigurationReading>().NewFreshnessWindow());
        serviceCollection.AddSingleton(services => new PeerPlane(
            services.GetRequiredService<RequestAuthenticator>(),
            services.GetRequiredService<ArrivalLimit>(),
            services.GetRequiredService<FreshnessWindow>(),
            services.GetRequiredService<RefusalCounters>()));

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
        // The request path resolves this, through the key source above. Every read takes the
        // instant it is judged at, and the instant is the one the controller reads from the
        // clock registered above and hands down, so the store and the arrival limit are judged
        // against one reading of the time rather than two.
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

        // The pairing record store, over its own file in the same directory. Two files rather
        // than one, because the two answer different questions: a key store that refuses is not a
        // reason an operator cannot be told what state a pairing is in, and a record carries no
        // key material for the two to share a refusal over.
        //
        // THIS COMMENT SAID NOTHING RESOLVES THIS YET AND THAT THIS IS THE POINT OF REGISTERING
        // IT. The administrative plane resolves it now, for the read that says whether an
        // enrolment window is open, so the registration is load-bearing rather than ahead of its
        // callers.
        //
        // THIS COMMENT ALSO SAID THE STATE MACHINE COULD NOT BE REGISTERED BESIDE IT, because
        // PairingStateMachine takes this and an IUserMappingStore and the second had no
        // implementation in this assembly. It has one now and both are registered below.
        //
        // What is unchanged is that nothing on a server writes a record, because nothing joins an
        // enrolment to the state machine, which is issue #18. So the read above answers an empty
        // list on every server, which is stated at the action rather than left for a reader to
        // infer.
        serviceCollection.AddSingleton<IPairingRecordStore>(services =>
            new FilePairingRecordStore(
                RecordStorePath.FileFor(services.GetRequiredService<IApplicationPaths>())));

        // The mapping table, over the third file in the same directory. It is a third file rather
        // than a member of either of the other two because the three answer different questions
        // and refuse separately: a key store that refuses is not a reason an administrator cannot
        // be shown which users are mapped, and a mapping carries no key material for the two to
        // share a refusal over.
        //
        // It is out of the plugin configuration for the reason IUserMappingStore states: the
        // configuration is a file an operator edits by hand and the host rewrites as plaintext
        // XML and serves back to the dashboard, and a table deciding where one person's data goes
        // does not belong in it.
        serviceCollection.AddSingleton<IUserMappingStore>(services =>
            new FileUserMappingStore(
                MappingStorePath.FileFor(services.GetRequiredService<IApplicationPaths>())));

        // The one type that owns what state a pairing is in, which could not be registered at all
        // until the mapping store above existed. It is registered here rather than constructed by
        // whoever needs it, because it sweeps the mapping table when a pairing ends and a second
        // instance over a second mapping store would sweep a table nobody is reading.
        //
        // WHAT RESOLVES IT TODAY IS NOTHING, and that is stated rather than implied: no plane and
        // no page reaches it yet, and what would put a record into it is the enrolment join in
        // issue #18. What the registration buys before then is that the container can build it,
        // which ServiceRegistrationTests asserts, so the day something does resolve it is not the
        // day a server finds out it cannot be built.
        serviceCollection.AddSingleton(services => new PairingStateMachine(
            services.GetRequiredService<IPairingRecordStore>(),
            services.GetRequiredService<IUserMappingStore>()));

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
