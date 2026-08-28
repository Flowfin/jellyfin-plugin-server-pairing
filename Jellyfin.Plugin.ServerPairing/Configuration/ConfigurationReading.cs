using System;
using System.Collections.Generic;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// What the plugin makes of the configuration it was handed: the settings it accepted, and
/// the ones it refused with the setting named.
/// </summary>
/// <remarks>
/// Reading is separated from holding on purpose. <see cref="PluginConfiguration"/> is a bag
/// of properties the host's XML serialiser fills, and a serialiser that throws on a bad value
/// takes the plugin out at load, which is the outcome an operator cannot repair from the
/// dashboard because the dashboard is what the plugin serves. So nothing here throws on a
/// value: a refused setting produces a <see cref="SettingRefusal"/> and the plugin stays
/// loaded.
/// <para>
/// Nothing is clamped. A value outside its range is refused and named, because a clamp hands
/// the operator a behaviour they did not ask for and no reason to look for one - the failure
/// issue #50 is written against is an operator setting a timeout to zero meaning unlimited.
/// </para>
/// <para>
/// A refused configuration is one this server does not pair on: <see cref="MayPair"/> is
/// false, and <see cref="Peer"/> is null, so there is no address for an enrolment to be
/// opened against. That is the whole of the refusal today, and it is stated as a bound rather
/// than as an assurance: no administrative endpoint opens an enrolment window in this tree at
/// all, which issue #49 is where it arrives.
/// </para>
/// </remarks>
public sealed class ConfigurationReading
{
    private ConfigurationReading(
        IReadOnlyList<SettingRefusal> refusals,
        int formatVersion,
        PeerAddress? peer,
        bool cleartextAcknowledged,
        int enrolmentWindowSeconds,
        int timestampWindowSeconds,
        int peerPlaneWindowSeconds,
        int peerPlaneArrivalsPerPairing,
        int peerPlaneArrivalsPerEnrolment)
    {
        Refusals = refusals;
        FormatVersion = formatVersion;
        Peer = peer;
        CleartextAcknowledged = cleartextAcknowledged;
        EnrolmentWindowSeconds = enrolmentWindowSeconds;
        TimestampWindowSeconds = timestampWindowSeconds;
        PeerPlaneWindowSeconds = peerPlaneWindowSeconds;
        PeerPlaneArrivalsPerPairing = peerPlaneArrivalsPerPairing;
        PeerPlaneArrivalsPerEnrolment = peerPlaneArrivalsPerEnrolment;
    }

    /// <summary>
    /// Gets the settings whose values were refused, in the order they are declared. Empty
    /// where every setting was accepted.
    /// </summary>
    public IReadOnlyList<SettingRefusal> Refusals { get; }

    /// <summary>
    /// Gets the format the configuration declared, which is
    /// <see cref="ConfigurationFormat.Unversioned"/> where it declared none.
    /// </summary>
    /// <remarks>
    /// The value as it was read, not the value it would be carried up to. Carrying up happens
    /// on the way to the file rather than on the way out of it, so this answers what was on
    /// disk, which is the question a refusal here is about.
    /// </remarks>
    public int FormatVersion { get; }

    /// <summary>
    /// Gets the peer this server may pair with, or null where none was entered or the one
    /// that was entered was refused.
    /// </summary>
    public PeerAddress? Peer { get; }

    /// <summary>
    /// Gets a value indicating whether the operator has acknowledged what a cleartext peer
    /// address costs.
    /// </summary>
    public bool CleartextAcknowledged { get; }

    /// <summary>
    /// Gets how long an enrolment window stays open, in seconds.
    /// </summary>
    public int EnrolmentWindowSeconds { get; }

    /// <summary>
    /// Gets how far an arriving request's timestamp may be from this server's clock, in
    /// seconds, in either direction.
    /// </summary>
    public int TimestampWindowSeconds { get; }

    /// <summary>
    /// Gets how long the peer plane counts an arrival allowance over, in seconds.
    /// </summary>
    public int PeerPlaneWindowSeconds { get; }

    /// <summary>
    /// Gets how many requests one pairing identifier may put on the peer plane inside a window.
    /// </summary>
    public int PeerPlaneArrivalsPerPairing { get; }

    /// <summary>
    /// Gets how many may arrive claiming the enrolment identifier, or claiming nothing the
    /// protocol can read an identifier out of.
    /// </summary>
    public int PeerPlaneArrivalsPerEnrolment { get; }

    /// <summary>
    /// Gets a value indicating whether this configuration is one the plugin will pair on.
    /// </summary>
    /// <remarks>
    /// A configuration with nothing wrong on it and no peer address entered is one this
    /// server pairs on and has nobody to pair with, which are two different states and are
    /// kept as two: <see cref="Peer"/> answers the second.
    /// </remarks>
    public bool MayPair => Refusals.Count == 0;

    /// <summary>
    /// Reads a configuration object the way the plugin reads it at load.
    /// </summary>
    /// <param name="configuration">What the host deserialised, or what a fresh installation
    /// constructs where the file holds nothing.</param>
    /// <returns>The settings that were accepted and the ones that were refused.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="configuration"/> is null.</exception>
    public static ConfigurationReading Of(PluginConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var refusals = new List<SettingRefusal>();
        var cleartextAcknowledged = configuration.AcknowledgeCleartextTransport;
        PeerAddress? peer = null;

        // A format this build cannot do anything with, at either end. Above the highest is a
        // configuration a newer build wrote: the host has already deserialised it into this
        // build's type, so every member that format added is gone by the time anything here sees
        // it, and what did survive is being read under rules the build that wrote it did not
        // have. Below the lowest is a number no build of this plugin has ever written, so it was
        // edited by hand. Both are refused rather than read, and both are refused here rather
        // than thrown, because nothing on this path may throw: what stops either reaching the
        // file is the write.
        if (!ConfigurationFormat.IsUnderstood(configuration.FormatVersion))
        {
            refusals.Add(new SettingRefusal(
                nameof(PluginConfiguration.FormatVersion),
                WhyTheFormatWasRefused(configuration.FormatVersion)));
        }

        // An address nobody has entered is the state a fresh installation is in, so it is
        // accepted and pairs with nobody. Anything else goes through the same parse a peer's
        // own claim about its address goes through, under the scheme policy the
        // acknowledgement above sets.
        if (!string.IsNullOrEmpty(configuration.PeerAddress))
        {
            var outcome = PeerAddress.Parse(configuration.PeerAddress, cleartextAcknowledged, out var parsed);

            if (outcome == PeerAddressOutcome.Accepted)
            {
                peer = parsed;
            }
            else
            {
                refusals.Add(new SettingRefusal(
                    nameof(PluginConfiguration.PeerAddress),
                    WhyTheAddressWasRefused(outcome, cleartextAcknowledged)));
            }
        }

        // How long the one moment this server answers a stranger lasts. Refused above its
        // maximum rather than clamped down to it: a window an operator asked to be a day long
        // and silently got half an hour of is a window they will not go looking for.
        var enrolmentWindowSeconds = Bounded(
            configuration.EnrolmentWindowSeconds,
            nameof(PluginConfiguration.EnrolmentWindowSeconds),
            1,
            EnrolmentWindow.MaximumLifetimeSeconds,
            EnrolmentWindow.LifetimeSeconds,
            "seconds",
            refusals);

        // The tolerated skew, which is also how long a captured request stays useful. Refused
        // above its bound rather than narrowed to it, for the same reason as everything else
        // here: an operator whose pairing then fails on a clock they widened the window for
        // has been given a number they did not choose.
        var timestampWindowSeconds = Bounded(
            configuration.TimestampWindowSeconds,
            nameof(PluginConfiguration.TimestampWindowSeconds),
            1,
            FreshnessWindow.MaximumWindowSeconds,
            FreshnessWindow.WindowSeconds,
            "seconds",
            refusals);

        // The three allowances the peer plane runs on. A value outside its bounds is refused
        // and the plane is built on the allowance a server nobody configured runs on, because
        // a plane whose limit was refused is not a plane with no limit. Which value is in
        // force is therefore readable here rather than inferable from whether anything was
        // refused.
        var windowSeconds = Bounded(
            configuration.PeerPlaneWindowSeconds,
            nameof(PluginConfiguration.PeerPlaneWindowSeconds),
            1,
            ArrivalLimit.MaximumWindowSeconds,
            ArrivalLimit.WindowSeconds,
            "seconds",
            refusals);

        var perPairing = Bounded(
            configuration.PeerPlaneArrivalsPerPairing,
            nameof(PluginConfiguration.PeerPlaneArrivalsPerPairing),
            1,
            ArrivalLimit.MaximumArrivals,
            ArrivalLimit.ArrivalsPerPairing,
            "arrivals",
            refusals);

        var perEnrolment = Bounded(
            configuration.PeerPlaneArrivalsPerEnrolment,
            nameof(PluginConfiguration.PeerPlaneArrivalsPerEnrolment),
            1,
            ArrivalLimit.MaximumArrivals,
            ArrivalLimit.ArrivalsPerEnrolment,
            "arrivals",
            refusals);

        // The enrolment allowance is the harder of the two because it is the one a stranger
        // reaches without knowing anything, and an operator who raises it above the other has
        // turned that argument off without meeting it. Both fall back rather than one, so the
        // pair that is in force is a pair somebody argued.
        if (perEnrolment > perPairing)
        {
            refusals.Add(new SettingRefusal(
                nameof(PluginConfiguration.PeerPlaneArrivalsPerEnrolment),
                "The enrolment allowance is the harder of the two and is never larger than '"
                + nameof(PluginConfiguration.PeerPlaneArrivalsPerPairing) + "', which is " + perPairing
                + ". It is the allowance a stranger reaches without knowing anything about this server."));

            perPairing = ArrivalLimit.ArrivalsPerPairing;
            perEnrolment = ArrivalLimit.ArrivalsPerEnrolment;
        }

        return new ConfigurationReading(
            refusals,
            configuration.FormatVersion,
            peer,
            cleartextAcknowledged,
            enrolmentWindowSeconds,
            timestampWindowSeconds,
            windowSeconds,
            perPairing,
            perEnrolment);
    }

    /// <summary>
    /// An enrolment window with the lifetime this reading accepted.
    /// </summary>
    /// <param name="paired">What this server is already paired with, which a window is refused
    /// against.</param>
    /// <returns>A window that closes after the configured lifetime.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="paired"/> is null.</exception>
    /// <remarks>
    /// NOTHING IN THIS PLUGIN CALLS THIS YET, and that is a bound rather than an oversight. A
    /// window is opened by an administrator and by nobody else, so the thing that builds one is
    /// the administrative surface, which is issue #49 and does not exist. Until it does, the
    /// only caller is the test that proves the lifetime reaches the window, and the setting is
    /// a number the server refuses out of range and hands to nothing.
    /// </remarks>
    public EnrolmentWindow NewEnrolmentWindow(IPairedPeers paired)
        => new EnrolmentWindow(paired, EnrolmentWindowSeconds);

    /// <summary>
    /// A freshness window with the tolerated skew this reading accepted.
    /// </summary>
    /// <returns>A window that refuses a timestamp further out than the configured skew.</returns>
    /// <remarks>
    /// NOTHING ON THE PEER PLANE CONSULTS A FRESHNESS WINDOW YET, which the refusal taxonomy
    /// says of the <c>clock</c> code in as many words. So the skew is refused out of range and
    /// reaches a window only here and in the test that proves it does. Wiring the plane to a
    /// freshness window is issue #21.
    /// </remarks>
    public FreshnessWindow NewFreshnessWindow() => new FreshnessWindow(TimestampWindowSeconds);

    /// <summary>
    /// The arrival limit the peer plane runs on under this reading.
    /// </summary>
    /// <returns>A limit built on the allowances above.</returns>
    /// <remarks>
    /// One of these is built per server rather than per caller, which the registrator holds to
    /// by registering it once: a limit held per caller hands every flood a fresh allowance.
    /// </remarks>
    public ArrivalLimit NewArrivalLimit()
        => new ArrivalLimit(PeerPlaneWindowSeconds, PeerPlaneArrivalsPerPairing, PeerPlaneArrivalsPerEnrolment);

    /// <summary>
    /// One whole-number setting, judged against its bounds.
    /// </summary>
    /// <param name="value">What the operator set.</param>
    /// <param name="setting">The setting's name, as it is spelled on the configuration.</param>
    /// <param name="least">The smallest accepted value.</param>
    /// <param name="most">The largest accepted value.</param>
    /// <param name="fallback">What is used where the value is refused.</param>
    /// <param name="unit">What the number counts, for the sentence the operator reads.</param>
    /// <param name="refusals">Where a refusal is collected.</param>
    /// <returns>The value where it is inside its bounds, otherwise the fallback.</returns>
    /// <remarks>
    /// The fallback is not a clamp. A clamp puts the value at the boundary it crossed, so an
    /// operator who asked for a day gets an hour and reads neither; this puts the plane back on
    /// the value it runs on when nobody has chosen, says which setting was refused, and leaves
    /// the operator's own value in the file untouched.
    /// </remarks>
    private static int Bounded(
        int value,
        string setting,
        int least,
        int most,
        int fallback,
        string unit,
        List<SettingRefusal> refusals)
    {
        if (value >= least && value <= most)
        {
            return value;
        }

        refusals.Add(new SettingRefusal(
            setting,
            "It is between " + least + " and " + most + " " + unit + ". Nothing was corrected: the peer plane runs on "
            + fallback + " " + unit + " until the value is one this accepts."));

        return fallback;
    }

    /// <summary>
    /// The sentence an operator reads for a format this build does not understand.
    /// </summary>
    /// <param name="declared">The format the configuration declares.</param>
    /// <returns>The reason, naming which end it fell off rather than reporting one
    /// wrong-format answer for both.</returns>
    private static string WhyTheFormatWasRefused(int declared) => ConfigurationFormat.IsFromANewerBuild(declared)
        ? "The configuration is in format " + declared + " and this build understands format " + ConfigurationFormat.Current
            + " at the highest, so it was written by a newer plugin than this one. Nothing was read out of it and nothing was corrected. Install the newer plugin again, or move the configuration file aside and set this plugin up afresh."
        : "The configuration declares format " + declared + ", and no build of this plugin has ever written a format below "
            + ConfigurationFormat.Unversioned
            + ", so that number was put there by hand. Nothing was read out of it and nothing was corrected. Set it to "
            + ConfigurationFormat.Unversioned + ", or remove the element, and this build will carry the file up on the next save.";

    /// <summary>
    /// The sentence an operator reads for one refused address.
    /// </summary>
    /// <param name="outcome">Which rule refused it.</param>
    /// <param name="cleartextAcknowledged">Whether cleartext was permitted for this reading.</param>
    /// <returns>The reason, naming the rule rather than reporting one invalid-address answer
    /// for every cause.</returns>
    private static string WhyTheAddressWasRefused(PeerAddressOutcome outcome, bool cleartextAcknowledged) => outcome switch
    {
        PeerAddressOutcome.TooLong =>
            "An address is at most " + PeerAddress.LengthLimit + " characters.",
        PeerAddressOutcome.NotAnAbsoluteUri =>
            "An address is an absolute URI and carries no character outside printable ASCII.",
        PeerAddressOutcome.SchemeNotAllowed when !cleartextAcknowledged =>
            "The pairing plane runs over '" + PeerAddress.AllowedScheme
            + "'. An '" + PeerAddress.CleartextScheme + "' address is accepted only where '"
            + nameof(PluginConfiguration.AcknowledgeCleartextTransport)
            + "' is set, and what that gives up is that request and response bodies, the mapping table among them, are readable by anything on the path between the two servers.",
        PeerAddressOutcome.SchemeNotAllowed =>
            "The pairing plane runs over '" + PeerAddress.AllowedScheme + "' or, with the cleartext acknowledgement set, over '"
            + PeerAddress.CleartextScheme + "'. No other scheme is accepted.",
        PeerAddressOutcome.UserInfoPresent =>
            "An address carries no user or password in front of the host.",
        PeerAddressOutcome.HostFormNotAllowed =>
            "A host is a plain ASCII domain name, an IPv4 literal, or a bracketed IPv6 literal.",
        PeerAddressOutcome.PortNotAllowed =>
            "The port is outside the range a port may take.",
        PeerAddressOutcome.PathPresent =>
            "An address carries no path. The pairing plane owns its own paths and appends them.",
        PeerAddressOutcome.QueryPresent =>
            "An address carries no query string.",
        PeerAddressOutcome.FragmentPresent =>
            "An address carries no fragment.",
        _ =>
            "The address is not one of the forms this plugin talks to.",
    };
}
