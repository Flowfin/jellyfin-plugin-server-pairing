using System;
using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Thrown when the key store on disk is in a format this build does not understand.
/// </summary>
/// <remarks>
/// This is the downgrade case and it is the reason a format number exists at all. An operator
/// who installs a newer plugin, pairs, and then rolls the plugin back leaves a file written by
/// a build that knew more than this one does. Reading it as far as it parses would drop
/// whatever the newer format added, and the drop would land on key material.
/// <para>
/// So this refuses instead, and every operation on the store refuses with it, because every
/// operation reads the file. What an operator gets is a plugin that will not pair and says
/// why, rather than one that pairs against a store it has silently truncated.
/// </para>
/// <para>
/// It is deliberately NOT the answer for a file that does not parse. That is a different
/// question - a damaged store rather than a newer one - and it is
/// <see cref="StoreDamagedException"/>. A file this type refuses is one that parsed, carried a
/// format number, and carried one higher than <see cref="StoreFormat.Current"/>. The two are
/// separate because what an operator does about them is separate: this one is fixed by
/// installing the newer plugin again, and a damaged file is not fixed by installing anything.
/// </para>
/// </remarks>
public sealed class StoreFormatRefusedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFormatRefusedException"/> class.
    /// </summary>
    /// <param name="found">The format the file declares.</param>
    /// <param name="understood">The highest format this build understands.</param>
    /// <param name="file">The file that was read.</param>
    public StoreFormatRefusedException(int found, int understood, string file)
        : this(found, understood, file, StoreDamagedException.KeyStoreName)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFormatRefusedException"/> class, for a
    /// store of the kind named.
    /// </summary>
    /// <param name="found">The format the file declares.</param>
    /// <param name="understood">The highest format this build understands.</param>
    /// <param name="file">The file that was read.</param>
    /// <param name="store">What the store is called, which is one of the two names on
    /// <see cref="StoreDamagedException"/>.</param>
    /// <remarks>
    /// The name is a parameter for the reason the neighbouring type gives: the two stores are
    /// refused by one rule and read by two files, and a sentence naming the wrong file sends an
    /// operator to look at a file that is fine.
    /// </remarks>
    public StoreFormatRefusedException(int found, int understood, string file, string store)
        : base(string.Format(
            CultureInfo.InvariantCulture,
            "The {3} at '{0}' is in format {1} and this build understands format {2} at the highest. It was written by a newer plugin than this one, so it is refused rather than read: reading it would drop whatever that format added. Install the newer plugin again, or move this file aside and pair afresh. No pairing works until then.",
            file,
            found,
            understood,
            store))
    {
        Found = found;
        Understood = understood;
        File = file;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFormatRefusedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public StoreFormatRefusedException(string message)
        : base(message)
    {
        File = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFormatRefusedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What caused it.</param>
    public StoreFormatRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        File = string.Empty;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="StoreFormatRefusedException"/> class.
    /// </summary>
    public StoreFormatRefusedException()
    {
        File = string.Empty;
    }

    /// <summary>
    /// Gets the format the file declares.
    /// </summary>
    public int Found { get; }

    /// <summary>
    /// Gets the highest format this build understands.
    /// </summary>
    public int Understood { get; }

    /// <summary>
    /// Gets the file that was read.
    /// </summary>
    public string File { get; }
}
