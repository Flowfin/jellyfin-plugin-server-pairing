using System;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// What the store's directory and file are created with, and what happens when one of them is
/// already there and wider than that.
/// </summary>
/// <remarks>
/// The permissions are applied AT CREATION rather than set afterwards. A file created with the
/// platform's default and narrowed on the next line exists, for that line, with whatever the
/// umask gave it, and on a shared machine that window is all a reader needs. Both calls below
/// take the mode as a creation argument for that reason, so there is no moment at which the
/// store's bytes are on disk under a wider mode than the one this type names.
/// <para>
/// A directory that is already there is not narrowed. An operator who widened it did so on
/// purpose or by accident, and this plugin cannot tell which; silently taking the permission
/// away is a change to something outside this plugin made without saying so, and silently
/// writing keys into it is worse. So it refuses, and the refusal names the path, because the
/// operator's next action is to look at it.
/// </para>
/// <para>
/// ON WINDOWS NONE OF THIS HAPPENS AND NOTHING PRETENDS OTHERWISE. A Unix mode is not
/// expressible there, <see cref="PlatformExpressesThem"/> is false, the directory and the file
/// are created with whatever the platform gives them, and no check is made. That is a real
/// gap rather than a platform on which the guard is unnecessary: what protects the store on
/// Windows is the access control the operator has on the server's data directory, which this
/// plugin neither reads nor sets. <c>docs/keystore.md</c> is where that is stated to an
/// operator.
/// </para>
/// </remarks>
public static class StorePermissions
{
    /// <summary>
    /// The mode the store's directory is created with: the owner may read it, write in it and
    /// traverse it, and nobody else has anything. This is <c>0700</c>.
    /// </summary>
    public const UnixFileMode DirectoryMode =
        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    /// <summary>
    /// The mode the store's file is created with: the owner reads and writes it, and nobody
    /// else has anything. This is <c>0600</c>.
    /// </summary>
    public const UnixFileMode FileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>
    /// Gets a value indicating whether this platform can express the modes above at all.
    /// </summary>
    /// <remarks>
    /// Windows is the platform that cannot. It is asked by name rather than by trying a call
    /// and reading the exception, so a caller can decide before it acts and a test can say why
    /// it is skipping rather than passing.
    /// <para>
    /// The guard attribute is what makes this readable by the platform-compatibility analyzer
    /// as well as by a person. Without it every call below is reported as reachable on Windows
    /// and the branch that exists to keep it off Windows counts for nothing.
    /// </para>
    /// </remarks>
    [UnsupportedOSPlatformGuard("windows")]
    public static bool PlatformExpressesThem => !OperatingSystem.IsWindows();

    /// <summary>
    /// Makes sure the store's directory exists and is not readable by anyone but its owner.
    /// </summary>
    /// <param name="directory">The directory the store's file lives in.</param>
    /// <exception cref="ArgumentNullException">The directory is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The directory is already there with permissions wider than <see cref="DirectoryMode"/>.
    /// </exception>
    /// <remarks>
    /// Creating it and setting its mode are one call, so the directory never exists under a
    /// wider mode, not even for the instant between two statements.
    /// </remarks>
    public static void PrepareDirectory(string directory)
    {
        ArgumentNullException.ThrowIfNull(directory);

        if (!PlatformExpressesThem)
        {
            Directory.CreateDirectory(directory);

            return;
        }

        if (Directory.Exists(directory))
        {
            RefuseIfWiderThanTheStoreWouldSet(directory);

            return;
        }

        Directory.CreateDirectory(directory, DirectoryMode);
    }

    /// <summary>
    /// Opens the store's file for writing, creating it with <see cref="FileMode"/> where the
    /// platform can express one.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <returns>The stream to write it through.</returns>
    /// <exception cref="ArgumentNullException">The path is null.</exception>
    /// <remarks>
    /// <see cref="FileStreamOptions.UnixCreateMode"/> is the creation argument rather than a
    /// call made afterwards, which is the whole point: the first byte of key material to reach
    /// the disk reaches a file that is already <c>0600</c>.
    /// <para>
    /// A MODE IS ONLY APPLIED TO A FILE THAT IS ACTUALLY CREATED, so anything already at this
    /// path is removed first and the open is <see cref="System.IO.FileMode.CreateNew"/> rather
    /// than <see cref="System.IO.FileMode.Create"/>. Truncating an existing file leaves it
    /// carrying the mode it already had, and the file this method is called for is the
    /// temporary one an atomic write moves into place - so a temporary left behind by a
    /// process that died, at whatever mode that process's umask gave it, would have carried
    /// that mode onto the store's file. The delete is what stops it.
    /// </para>
    /// </remarks>
    public static FileStream CreateFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);

        File.Delete(path);

        var options = new FileStreamOptions
        {
            Mode = System.IO.FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };

        if (PlatformExpressesThem)
        {
            options.UnixCreateMode = FileMode;
        }

        return new FileStream(path, options);
    }

    /// <summary>
    /// Refuses a directory carrying any permission the store would not have set.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <exception cref="InvalidOperationException">It carries one.</exception>
    [UnsupportedOSPlatform("windows")]
    private static void RefuseIfWiderThanTheStoreWouldSet(string directory)
    {
        var mode = File.GetUnixFileMode(directory);
        var wider = mode & ~DirectoryMode;

        if (wider == UnixFileMode.None)
        {
            return;
        }

        throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "The key store directory '{0}' allows {1}, which is wider than the {2} this plugin creates it with. "
            + "It is refused rather than narrowed, because narrowing a directory an operator widened would be a "
            + "change made to their server without saying so. Remove the extra permissions from that path, or "
            + "move it aside so the store can create its own.",
            directory,
            wider,
            DirectoryMode));
    }
}
