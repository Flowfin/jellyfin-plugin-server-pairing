using System;
using System.IO;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Where the key store's file lives, which is nowhere near the plugin configuration.
/// </summary>
/// <remarks>
/// The host owns <c>PluginConfigurationsPath</c> and writes every plugin's configuration there
/// as plaintext XML. Putting key material anywhere under that directory would put it in the
/// same backups, behind the same reads and beside the file the dashboard already serves back,
/// so the store takes its own directory under the server's data path instead.
/// <para>
/// The path is derived from the host's own paths rather than written down, so a server whose
/// data directory is somewhere unusual is served correctly without a setting. The directory
/// name is this plugin's, so two plugins on one server do not meet in it.
/// </para>
/// <para>
/// Creating the directory, and with what permissions, is issue #35 and is not decided here.
/// This type answers where, and nothing else.
/// </para>
/// </remarks>
public static class KeyStorePath
{
    /// <summary>
    /// The directory this plugin keeps its own files in, under the server's data path.
    /// </summary>
    public const string DirectoryName = "server-pairing";

    /// <summary>
    /// The name of the file the keys are held in.
    /// </summary>
    public const string FileName = "keys.json";

    /// <summary>
    /// The directory the store owns.
    /// </summary>
    /// <param name="paths">The host's paths.</param>
    /// <returns>The directory.</returns>
    /// <exception cref="ArgumentNullException">The paths are null.</exception>
    public static string DirectoryFor(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Combine(paths.DataPath, DirectoryName);
    }

    /// <summary>
    /// The file the keys are held in.
    /// </summary>
    /// <param name="paths">The host's paths.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentNullException">The paths are null.</exception>
    public static string FileFor(IApplicationPaths paths) => Path.Combine(DirectoryFor(paths), FileName);
}
