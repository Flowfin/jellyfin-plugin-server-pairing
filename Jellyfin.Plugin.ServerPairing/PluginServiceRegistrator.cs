using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

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
        serviceCollection.AddSingleton(services => new PeerPlane(services.GetRequiredService<RequestAuthenticator>()));
    }
}
