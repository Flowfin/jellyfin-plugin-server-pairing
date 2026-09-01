using System;
using System.IO;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Where the pairing record store's file lives, which is beside the key store and nowhere near
/// the plugin configuration.
/// </summary>
/// <remarks>
/// The directory is <see cref="KeyStorePath.DirectoryFor(IApplicationPaths)"/> rather than one
/// of this type's own. Two directories would be two sets of permissions to get right, two
/// things for an operator to move when they move the plugin's state, and one of the two would
/// be the one somebody forgot. The file is separate because the two stores answer different
/// questions and a key store that refuses is not a reason a state cannot be read.
/// <para>
/// A pairing record holds no key material, which <see cref="PairingRecord"/> states and this
/// type does not soften. It is kept out of the plugin configuration anyway: the configuration is
/// a file the host rewrites as plaintext XML and serves back to the dashboard, and which peer a
/// server is paired with, and when an administrator confirmed it, is not something to put there.
/// </para>
/// </remarks>
public static class RecordStorePath
{
    /// <summary>
    /// The name of the file the pairing records are held in.
    /// </summary>
    public const string FileName = "pairings.json";

    /// <summary>
    /// The file the pairing records are held in.
    /// </summary>
    /// <param name="paths">The host's paths.</param>
    /// <returns>The file.</returns>
    /// <exception cref="ArgumentNullException">The paths are null.</exception>
    public static string FileFor(IApplicationPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);

        return Path.Join(KeyStorePath.DirectoryFor(paths), FileName);
    }
}
