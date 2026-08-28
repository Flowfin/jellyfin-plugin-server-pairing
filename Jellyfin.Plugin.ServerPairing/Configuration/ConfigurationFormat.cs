using System;
using System.Globalization;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// The number the plugin configuration carries saying what shape it is in, and the ladder
/// that carries an older configuration up to the shape this build reads.
/// </summary>
/// <remarks>
/// This is the configuration half of what <see cref="KeyStore.StoreFormat"/> does for the key
/// store, and it exists for the same reason: issue #55 asks that both stores carry a format
/// version before there is anything to migrate, because the first version to ship is the only
/// one that gets to define a format without migrating one.
/// <para>
/// WHAT IS DIFFERENT HERE IS WHO WRITES THE FILE. The key store writes its own bytes, so it
/// stamps the number as it writes them. This file is written by the host's XML serialiser out
/// of <see cref="PluginConfiguration"/>, so the number is a member of that type and is stamped
/// at the one place every write goes through, which is <c>Plugin.SaveConfiguration</c>. That
/// includes the write the host makes on its own when no file exists yet, so a fresh
/// installation's first file carries the number rather than acquiring one later.
/// </para>
/// <para>
/// THE SECOND DIFFERENCE IS WHAT A MIGRATION MAY SEE. A key store migration works on the
/// parsed JSON document, so a member it does not name travels through untouched. Here the host
/// has already deserialised into this build's own type before the plugin is handed anything,
/// so a member a newer build wrote is gone before any rung could carry it. That is why a
/// configuration from the future is refused outright, in both directions, rather than read for
/// the parts this build recognises.
/// </para>
/// <para>
/// FORMAT 0 IS NOT A FORMAT THAT WAS DESIGNED. It is what this plugin's configuration was
/// before this number existed, and it is what a file that mentions no number deserialises to,
/// because a missing element leaves the value the constructor set. It is named rather than
/// special-cased so a configuration already on an operator's disk has a rung to start from.
/// </para>
/// <para>
/// A RUNG MAY NOT MOVE A FRESH CONFIGURATION. A fresh installation constructs the object at
/// <see cref="Unversioned"/> and is carried up the same ladder an old file is, so a rung that
/// derives a new setting from an old one has to leave this build's own defaults where they
/// are. That is a rule about every rung written from here on rather than a property of the one
/// that exists, and <c>ConfigurationFormatTests.CarryingAFreshConfigurationUpMovesNoSetting</c>
/// is where it is refused.
/// </para>
/// </remarks>
public static class ConfigurationFormat
{
    /// <summary>
    /// The format this build writes and reads.
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// The format a configuration that carries no format number is in.
    /// </summary>
    /// <remarks>
    /// A configuration written before the number existed, and also what a freshly constructed
    /// one holds until it is stamped on its way to the file. Nothing writes it: it is read so
    /// that a configuration already on an operator's disk has somewhere to be carried up from.
    /// </remarks>
    public const int Unversioned = 0;

    /// <summary>
    /// Whether a declared format is one this build is too old to read.
    /// </summary>
    /// <param name="declared">The format the configuration declares.</param>
    /// <returns><c>true</c> where it is higher than <see cref="Current"/>.</returns>
    public static bool IsFromANewerBuild(int declared) => declared > Current;

    /// <summary>
    /// Whether a declared format is one this build can do anything with at all.
    /// </summary>
    /// <param name="declared">The format the configuration declares.</param>
    /// <returns><c>true</c> where it is between <see cref="Unversioned"/> and <see cref="Current"/>.</returns>
    /// <remarks>
    /// The lower end is not the same failure as the upper one and is refused for its own reason.
    /// Nothing this plugin has ever written declares a format below <see cref="Unversioned"/>,
    /// so a file that does was edited by hand into a state no build produces. Left unrefused it
    /// reaches the ladder, which has no rung away from it and fails there - a fault an operator
    /// meets as a save that did not work rather than as a sentence naming the member they
    /// changed.
    /// </remarks>
    public static bool IsUnderstood(int declared) => declared >= Unversioned && declared <= Current;

    /// <summary>
    /// Walks a configuration up the ladder to <see cref="Current"/>, one rung at a time, and
    /// stamps the number it now carries.
    /// </summary>
    /// <param name="configuration">The configuration, in whatever format it declares.</param>
    /// <exception cref="ArgumentNullException">The configuration is null.</exception>
    /// <exception cref="ConfigurationFormatRefusedException">
    /// The configuration declares a format this build does not understand, which is one newer
    /// than <see cref="Current"/> or one below <see cref="Unversioned"/>.
    /// </exception>
    /// <remarks>
    /// One rung at a time in order rather than jumped. Two rungs written as one jump have to
    /// be rewritten every time a rung is added below them, and a configuration three formats
    /// old then travels a path nothing has ever run.
    /// </remarks>
    public static void CarryUp(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var declared = configuration.FormatVersion;

        if (!IsUnderstood(declared))
        {
            throw new ConfigurationFormatRefusedException(declared, Current);
        }

        for (var rung = declared; rung < Current; rung++)
        {
            Rung(rung, configuration);
        }

        configuration.FormatVersion = Current;
    }

    /// <summary>
    /// The one rung that carries a configuration away from the format given.
    /// </summary>
    /// <param name="from">The format the configuration is in.</param>
    /// <param name="configuration">The configuration.</param>
    /// <exception cref="InvalidOperationException">There is no rung away from that format.</exception>
    /// <remarks>
    /// A switch rather than a table held in a static field, because the assembly carries no
    /// static state outside the plugin instance and <c>StaticStateTests</c> refuses one. The
    /// default arm is what a format added below <see cref="Current"/> without its migration
    /// meets, and it fails rather than silently leaving the configuration where it was.
    /// </remarks>
    private static void Rung(int from, PluginConfiguration configuration)
    {
        switch (from)
        {
            case Unversioned:
                FromUnversionedToOne(configuration);
                break;

            default:
                throw new InvalidOperationException(string.Format(
                    CultureInfo.InvariantCulture,
                    "There is no migration away from plugin configuration format {0}, so a configuration in it cannot be carried up to format {1}.",
                    from,
                    Current));
        }
    }

    /// <summary>
    /// Format 0 to format 1: the settings are unchanged and the number is what arrives.
    /// </summary>
    /// <param name="configuration">The configuration in format 0.</param>
    /// <remarks>
    /// Format 1 holds exactly the settings format 0 held. This rung therefore moves no value,
    /// and it is written out rather than skipped so that the ladder is walked from the format a
    /// file already on a disk declares rather than from the first format anybody designed. The
    /// stamp is not made here: <see cref="CarryUp"/> writes the number once, after the last
    /// rung, so a rung that throws leaves the configuration declaring the format it was in.
    /// </remarks>
    private static void FromUnversionedToOne(PluginConfiguration configuration)
    {
        _ = configuration;
    }
}
