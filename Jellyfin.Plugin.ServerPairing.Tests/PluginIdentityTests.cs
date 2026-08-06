using System;
using System.Linq;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Guards the parts of the plugin's identity that are read by name at run time and
/// would otherwise fail only on a server, where nothing here can see it.
/// </summary>
public class PluginIdentityTests
{
    private const string ConfigurationPageResource =
        "Jellyfin.Plugin.ServerPairing.Configuration.configPage.html";

    /// <summary>
    /// GetPages builds the embedded resource path from the runtime namespace and the
    /// csproj gives the embedded file its logical name from RootNamespace. Those two are
    /// written in different files and nothing but this makes them agree. When they stop
    /// agreeing the plugin still builds, still loads, and serves a blank settings page.
    /// </summary>
    [Fact]
    public void ConfigurationPageIsEmbeddedUnderTheNamespaceGetPagesAsksFor()
    {
        var assembly = typeof(Plugin).Assembly;

        var askedFor = string.Concat(typeof(Plugin).Namespace, ".Configuration.configPage.html");

        Assert.Equal(ConfigurationPageResource, askedFor);
        Assert.Contains(ConfigurationPageResource, assembly.GetManifestResourceNames());
    }

    /// <summary>
    /// The server discovers a plugin by scanning the assembly for a type deriving from
    /// BasePlugin and offers a settings page only for one that also implements
    /// IHasWebPages. Losing either is a plugin that installs and does nothing.
    /// </summary>
    [Fact]
    public void PluginTypeCarriesTheContractsTheServerDiscoversItBy()
    {
        Assert.True(typeof(BasePlugin<Configuration.PluginConfiguration>).IsAssignableFrom(typeof(Plugin)));
        Assert.True(typeof(IHasWebPages).IsAssignableFrom(typeof(Plugin)));
    }

    /// <summary>
    /// The plugin assembly is what build.yaml names as the artifact to package, by file
    /// name, so a rename that misses the project file produces a package with nothing
    /// in it that the manifest points at.
    /// </summary>
    [Fact]
    public void PluginAssemblyIsNamedWhatTheManifestPackages()
    {
        var name = typeof(Plugin).Assembly.GetName().Name;

        Assert.Equal("Jellyfin.Plugin.ServerPairing.Deliberate.Failure", name);
    }

    /// <summary>
    /// The settings page is the one thing this assembly ships as data. Anything else
    /// arriving in the manifest is a file that was embedded by accident, which for a
    /// plugin holding key material is a way for a local file to leave the machine
    /// inside the package.
    /// </summary>
    [Fact]
    public void ConfigurationPageResourceIsTheOnlyEmbeddedResource()
    {
        var resources = typeof(Plugin).Assembly.GetManifestResourceNames();

        Assert.Equal(new[] { ConfigurationPageResource }, resources.OrderBy(r => r, StringComparer.Ordinal).ToArray());
    }
}
