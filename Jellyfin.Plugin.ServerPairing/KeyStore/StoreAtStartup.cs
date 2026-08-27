using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Reads the key store once, when the server starts, and says what it found.
/// </summary>
/// <remarks>
/// The store outlives an uninstall. It sits under the server's data path and the host removes
/// the plugin directory, which are different directories, so a plugin reinstalled over a
/// surviving store comes up paired with whatever it was paired with before. That is a feature
/// and it is also a surprise, and <c>docs/lifecycle.md</c> is where both halves are argued.
/// This type is the half of it that says so rather than presenting the pairings as though
/// nothing happened.
/// <para>
/// It asks the store what it holds and changes nothing itself. A store the operator does not
/// have is not created by looking at it, which is the property issue #35 landed for the write
/// path and which this must not undo: <see cref="IPairingKeyStore.Pairings"/> over an absent
/// file answers with nothing and leaves the disk alone. WHAT A READ CAN DO ON A FILE THAT IS
/// THERE is carry it up to the current format, which <see cref="StoreFormat"/> describes, so
/// this is also the moment a store written by an older build is migrated.
/// </para>
/// <para>
/// A read that throws is caught. A hosted service whose <see cref="StartAsync"/> throws stops
/// the host, so a key store file that does not parse would take the whole server down at boot
/// - and a store that does not parse is exactly the case issue #33 says nothing answers for
/// yet. What an operator gets instead is one line at Error and a server that starts. The
/// pairings do not work either way; the difference is whether anything else on the server
/// does.
/// </para>
/// <para>
/// Nothing here runs at shutdown, and that is the whole of <see cref="StopAsync"/>. A plugin
/// that swept, compacted or removed anything on the way down would be a plugin whose store
/// depends on a clean shutdown, and a media server is stopped by having its power cut often
/// enough that this is a property rather than a preference.
/// </para>
/// </remarks>
public sealed class StoreAtStartup : IHostedService
{
    private readonly IPairingKeyStore _store;
    private readonly ILogger<StoreAtStartup> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreAtStartup"/> class.
    /// </summary>
    /// <param name="store">The key store to read.</param>
    /// <param name="logger">Where what was found is written.</param>
    public StoreAtStartup(IPairingKeyStore store, ILogger<StoreAtStartup> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads the store and writes one entry per pairing it already holds.
    /// </summary>
    /// <param name="cancellationToken">Cancels the start.</param>
    /// <returns>A completed task.</returns>
    /// <remarks>
    /// One entry per pairing rather than a count, because every other row of the table in
    /// <c>docs/logging.md</c> carries the pairing identifier and an operator asking why a peer
    /// is talking to this server wants the identifier rather than the number of them. A store
    /// holding nothing writes nothing: silence is the ordinary case, and an entry for it is a
    /// line an operator learns to skip.
    /// </remarks>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // The read happens whatever the level is set to, because a read that throws is
            // what the catch below is for and an operator who turned Information off has not
            // asked to be kept from an Error. The guard is around the writing only, and it is
            // here at all because the analyzers refuse a call at a level that can be switched
            // off without one.
            var held = _store.Pairings();

            if (_logger.IsEnabled(LogLevel.Information))
            {
                foreach (var pairingId in held)
                {
                    _logger.LogInformation(
                        "This plugin started against a key store that already holds this pairing. It was kept across whatever removed the plugin, because the store is not in the plugin's directory. Pairing: {PairingId}",
                        pairingId);
                }
            }
        }
#pragma warning disable CA1031 // A read that throws must not stop the host, so the type cannot be narrowed without deciding which failures are allowed to take the server down.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The key store could not be read at startup, so what it holds is unknown and no pairing will work. The server is left running.");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Does nothing, deliberately.
    /// </summary>
    /// <param name="cancellationToken">Cancels the stop.</param>
    /// <returns>A completed task.</returns>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
