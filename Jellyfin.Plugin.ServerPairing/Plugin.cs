using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.ServerPairing;

/// <summary>
/// The main plugin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Server Pairing";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("130cc961-461b-49fd-8a3e-f9eb46db0716");

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <summary>
    /// Stamps the format number onto the configuration on its way to the file, and refuses to
    /// write one a newer build wrote.
    /// </summary>
    /// <param name="config">What is about to be serialised.</param>
    /// <exception cref="ArgumentNullException"><paramref name="config"/> is null.</exception>
    /// <exception cref="ConfigurationFormatRefusedException">
    /// The configuration declares a format newer than this build understands, so writing it
    /// would put this build's truncated reading of it on disk.
    /// </exception>
    /// <remarks>
    /// This is the one place every write of the configuration file goes through. The host's
    /// base class calls it from the dashboard save, from a save the plugin asks for itself, and
    /// from the load path when no file exists yet - which is what makes issue #55's condition
    /// hold in the words it uses, that the store carries a format version from its FIRST write
    /// rather than from some later one:
    /// <code>
    /// git show v10.11.9:MediaBrowser.Common/Plugins/BasePluginOfT.cs | grep -n 'SaveConfiguration'
    /// </code>
    /// <para>
    /// The number is stamped here rather than in the constructor because the constructor's value
    /// is also what a file that mentions no number deserialises to. Those are two different
    /// facts - what this build writes, and what a configuration carrying no number is in - and a
    /// single value cannot go on meaning both once the current format moves.
    /// </para>
    /// </remarks>
    public override void SaveConfiguration(PluginConfiguration config)
    {
        ArgumentNullException.ThrowIfNull(config);

        ConfigurationFormat.CarryUp(config);

        base.SaveConfiguration(config);
    }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
