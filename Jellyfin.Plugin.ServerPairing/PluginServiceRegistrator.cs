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
        // This plugin adds no service yet. The registrations arrive with the types that
        // need them, and they arrive here rather than in a static initialiser.
    }
}
