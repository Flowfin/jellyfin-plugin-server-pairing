using System;
using System.Collections.Generic;
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
    private ConfigurationReading(IReadOnlyList<SettingRefusal> refusals, PeerAddress? peer, bool cleartextAcknowledged)
    {
        Refusals = refusals;
        Peer = peer;
        CleartextAcknowledged = cleartextAcknowledged;
    }

    /// <summary>
    /// Gets the settings whose values were refused, in the order they are declared. Empty
    /// where every setting was accepted.
    /// </summary>
    public IReadOnlyList<SettingRefusal> Refusals { get; }

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

        return new ConfigurationReading(refusals, peer, cleartextAcknowledged);
    }

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
