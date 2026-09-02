using System;
using System.IO;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// Where the mapping store's file lives, which is beside the key store and the pairing records
/// and nowhere near the plugin configuration.
/// </summary>
/// <remarks>
/// The directory is <see cref="KeyStorePath.DirectoryFor(IApplicationPaths)"/> rather than one
/// of this type's own, for the reason <see cref="Protocol.RecordStorePath"/> gives for the file
/// beside it: two directories would be two sets of permissions to get right, two things for an
/// operator to move when they move this plugin's state, and one of the two would be the one
/// somebody forgot.
/// <para>
/// The file is separate from both of the others because the three answer different questions.
/// A key store that refuses is not a reason an administrator cannot be shown the mapping table,
/// and a mapping carries no key material for the two to share a refusal over. This is the third
/// file under that directory rather than the second, and <c>docs/keystore.md</c> says so where a
/// reader listing the directory will meet it.
/// </para>
/// <para>
/// It is kept out of the plugin configuration for the reason <see cref="IUserMappingStore"/>
/// states: the configuration is a file an operator edits by hand and the host rewrites as
/// plaintext XML and serves back to the dashboard, and a table deciding where one person's data
/// goes does not belong in it.
/// </para>
/// </remarks>
public static class MappingStorePath
{
    /// <summary>
    /// The name of the file the mappings are held in.
    /// </summary>
    public const string FileName = "mappings.json";

    /// <summary>
    /// The file the mappings are held in.
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
