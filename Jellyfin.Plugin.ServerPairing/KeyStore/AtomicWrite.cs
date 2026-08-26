using System;
using System.IO;
using System.Text;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Puts a file in place in one step, or leaves the previous one where it was.
/// </summary>
/// <remarks>
/// Writing over a file in place has a window in which it is neither the old contents nor the
/// new ones. A power loss, a full disk or a process killed inside that window leaves a file
/// that parses as far as it got, and for a key store that is a pairing whose key is half of
/// one key and half of another. Writing beside it and moving it over closes the window: the
/// move is one operation as far as anything reading the directory is concerned, so a reader
/// sees the old file or the new file and never a third thing.
/// <para>
/// The temporary file is in the same directory as its destination on purpose. A move across a
/// filesystem boundary is a copy and a delete, which is the window this exists to close, and
/// a temporary directory is a different filesystem on more machines than not.
/// </para>
/// <para>
/// The temporary file is created with the store's own permissions rather than the platform's,
/// which is <see cref="StorePermissions"/> and issue #35. A move preserves the mode of the
/// file being moved, so creating the temporary with a default mode would put that default on
/// the store's file, and would do it with the key material already written under it.
/// </para>
/// <para>
/// WHAT THIS DOES NOT DO is force the bytes to the platter before the move. The move is
/// ordered after the write by this code and not by any promise the filesystem makes, so a
/// machine that loses power between them may come back with the move done and the contents
/// not. Closing that needs a flush the runtime does not expose portably, and it is named here
/// rather than implied away.
/// </para>
/// </remarks>
public static class AtomicWrite
{
    /// <summary>
    /// The suffix a temporary file carries while it is being written.
    /// </summary>
    public const string TemporarySuffix = ".writing";

    /// <summary>
    /// Writes text to a file, leaving either the previous contents or the new ones and never
    /// anything between.
    /// </summary>
    /// <param name="path">The file to put in place.</param>
    /// <param name="contents">What it should hold.</param>
    /// <param name="moveIntoPlace">
    /// How the temporary file becomes the destination. The default is
    /// <see cref="System.IO.File.Move(string, string, bool)"/>; a caller passes its own only to
    /// drive the failure this method is written against.
    /// </param>
    /// <exception cref="ArgumentNullException">The path or the contents are null.</exception>
    /// <remarks>
    /// A failure anywhere leaves the destination as it was. The temporary file is removed on
    /// the way out where it can be, and a temporary left behind by a process that died is
    /// overwritten by the next write rather than being read: nothing reads a file with this
    /// suffix.
    /// </remarks>
    public static void Replace(string path, string contents, Action<string, string>? moveIntoPlace = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(contents);

        var directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            StorePermissions.PrepareDirectory(directory);
        }

        var temporary = path + TemporarySuffix;

        try
        {
            // The temporary file is the one that has to be created with the store's mode, not
            // the destination. A move preserves the mode of what is moved, so a temporary
            // created with the platform's default and moved into place puts the default on the
            // store's file - and does it after the key material is already on the disk under
            // it. Writing through the stream that names the mode at creation closes that.
            using (var stream = StorePermissions.CreateFile(temporary))
            {
                var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(contents);

                stream.Write(bytes, 0, bytes.Length);
            }

            (moveIntoPlace ?? Move)(temporary, path);
        }
        catch
        {
            Discard(temporary);

            throw;
        }
    }

    private static void Move(string temporary, string destination) => File.Move(temporary, destination, overwrite: true);

    private static void Discard(string temporary)
    {
        try
        {
            File.Delete(temporary);
        }
        catch (IOException)
        {
            // The failure being reported is the write, and a temporary that could not be
            // removed does not change what the destination holds. Swallowing this one is what
            // keeps the exception the caller sees the one that matters; the next write
            // overwrites the temporary, and nothing ever reads it.
        }
        catch (UnauthorizedAccessException)
        {
            // Same reason.
        }
    }
}
