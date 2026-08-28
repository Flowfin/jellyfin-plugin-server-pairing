using System;
using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// Thrown when a plugin configuration is in a format this build does not understand and
/// something asked for it to be written back.
/// </summary>
/// <remarks>
/// This is the downgrade case, the same one <see cref="KeyStore.StoreFormatRefusedException"/>
/// covers for the key store. An operator installs a newer plugin, configures it, and rolls the
/// plugin back. The host deserialises that file into this build's type, which drops every
/// member the newer format added, and writing the result back would put the truncation on disk.
/// <para>
/// SO THE WRITE PATH THROWS AND THE READ PATH DOES NOT. Nothing may throw out of the read: a
/// setter or a reading that threw would take the plugin out at load, and the repair for that is
/// a text editor on the server's filesystem, which is what leaving the plugin loaded exists to
/// spare the operator. <see cref="ConfigurationReading"/> therefore refuses a newer format the
/// way it refuses any other value, by naming the setting and not pairing. This type is what
/// stops the file itself being overwritten, and it is raised only where a write was asked for.
/// </para>
/// <para>
/// What that leaves is stated rather than tidied away: the host sets its in-memory
/// configuration before it calls the save, so a dashboard save against a newer file leaves this
/// build running on what the dashboard sent and leaves the file on disk untouched. The file is
/// what the newer build reads when it is installed again, and it is the one that had to survive.
/// </para>
/// </remarks>
public sealed class ConfigurationFormatRefusedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationFormatRefusedException"/> class.
    /// </summary>
    /// <param name="found">The format the configuration declares.</param>
    /// <param name="understood">The highest format this build understands.</param>
    public ConfigurationFormatRefusedException(int found, int understood)
        : base(Sentence(found, understood))
    {
        Found = found;
        Understood = understood;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationFormatRefusedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    public ConfigurationFormatRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationFormatRefusedException"/> class.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">What caused it.</param>
    public ConfigurationFormatRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfigurationFormatRefusedException"/> class.
    /// </summary>
    public ConfigurationFormatRefusedException()
    {
    }

    /// <summary>
    /// Gets the format the configuration declares.
    /// </summary>
    public int Found { get; }

    /// <summary>
    /// Gets the highest format this build understands.
    /// </summary>
    public int Understood { get; }

    /// <summary>
    /// The sentence for one end or the other.
    /// </summary>
    /// <param name="found">The format the configuration declares.</param>
    /// <param name="understood">The highest format this build understands.</param>
    /// <returns>The message.</returns>
    /// <remarks>
    /// Two ends, two causes, two sentences. A format above the highest understood is a newer
    /// plugin's file and the operator's way out is to install that plugin again. A format below
    /// the lowest is a value no build of this plugin has ever written, so it was edited by hand,
    /// and telling that operator to reinstall something would send them after a plugin that does
    /// not exist.
    /// </remarks>
    private static string Sentence(int found, int understood) => found > understood
        ? string.Format(
            CultureInfo.InvariantCulture,
            "The plugin configuration is in format {0} and this build understands format {1} at the highest. It was written by a newer plugin than this one, so it is not written back: doing so would drop whatever that format added. Install the newer plugin again, or move the configuration file aside and set this plugin up afresh.",
            found,
            understood)
        : string.Format(
            CultureInfo.InvariantCulture,
            "The plugin configuration declares format {0}, and no build of this plugin has ever written a format below 0, so that number was put there by hand. It is not written back and nothing was corrected. Set it to 0 in the configuration file, or remove the element, and this build will carry the file up to format {1} on the next save.",
            found,
            understood);
}
