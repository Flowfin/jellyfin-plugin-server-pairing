using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// Reads the configuration once, when the server starts, and says what was refused.
/// </summary>
/// <remarks>
/// A configuration that fails validation leaves the plugin loaded and refusing to pair, so
/// something has to say so where an operator will see it. This is that something: one line
/// per refused setting, at Error, naming the setting and the rule.
/// <para>
/// It is the same shape as <see cref="KeyStore.StoreAtStartup"/> and for the same reason. A
/// hosted service whose <see cref="StartAsync"/> throws stops the host, so a configuration
/// file with a bad value would take the whole server down at boot - and the repair for that
/// is a text editor on the server's filesystem, which is exactly what leaving the plugin
/// loaded exists to spare the operator. Nothing here throws and nothing here corrects a
/// value.
/// </para>
/// <para>
/// The cleartext acknowledgement is written out too, at Warning, on the runs where it is set.
/// An operator who ticked it months ago and is now reading a log about a pairing that leaks
/// is owed the line saying which setting made it cleartext, and a setting that weakens the
/// transport silently is the state issue #50 was written against.
/// </para>
/// </remarks>
public sealed class ConfigurationAtStartup : IHostedService
{
    private readonly ConfigurationReading _reading;
    private readonly ILogger<ConfigurationAtStartup> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationAtStartup"/> class.
    /// </summary>
    /// <param name="reading">What the plugin made of the configuration it was handed.</param>
    /// <param name="logger">Where the refusals are written.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public ConfigurationAtStartup(ConfigurationReading reading, ILogger<ConfigurationAtStartup> logger)
    {
        _reading = reading ?? throw new ArgumentNullException(nameof(reading));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Writes one entry per refused setting, and one where the transport was weakened
    /// deliberately.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var refusal in _reading.Refusals)
        {
            _logger.LogError(
                "A setting was refused, so this plugin is loaded and will not pair. Nothing was corrected: the value in the configuration file is the one you set. {Refusal}",
                refusal.Message);
        }

        if (_reading.CleartextAcknowledged)
        {
            _logger.LogWarning(
                "This server is configured to accept a cleartext peer address. Request and response bodies, the mapping table among them, are readable by anything on the path between the two servers. Setting: {Setting}",
                nameof(PluginConfiguration.AcknowledgeCleartextTransport));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing, deliberately. Nothing here is written on the way down, for the reason
    /// <see cref="KeyStore.StoreAtStartup"/> gives: a media server is stopped by having its
    /// power cut often enough that a clean shutdown is not a thing to depend on.
    /// </summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
