using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// The configuration options.
/// </summary>
public enum SomeOptions
{
    /// <summary>
    /// Option one.
    /// </summary>
    OneOption,

    /// <summary>
    /// Second option.
    /// </summary>
    AnotherOption
}

/// <summary>
/// Plugin configuration.
/// </summary>
/// <remarks>
/// The host writes this out with the XML serialiser, so every setting here is plaintext on
/// the server's filesystem and in every backup of it. Nothing secret belongs on it, which
/// <c>ConfigurationKeyMaterialTests</c> refuses rather than leaves to a reader, and
/// <c>docs/keystore.md</c> is where key material lives instead.
/// <para>
/// A missing element keeps the value this constructor set. The serialiser builds the object
/// through the parameterless constructor and then assigns only the members the document
/// carries, so a configuration file written by an older build, which mentions none of the
/// settings added since, comes up on the defaults below rather than on zeroes. That was
/// asserted the wrong way round here until it was measured:
/// <c>ConfigurationReadingTests.AMissingElementKeepsTheValueTheConstructorSet</c> is the
/// measurement, and it is a test rather than this sentence because the sentence is what was
/// wrong.
/// </para>
/// <para>
/// So a safe value is written in the constructor whether or not it happens to match what the
/// type would produce on its own: <c>false</c> for a switch that weakens something, an empty
/// address for a peer nobody has entered, and the allowance the peer plane runs on for a
/// count. A count left to the type's own default would be zero, and zero is not a small
/// allowance, it is a plane that refuses everything.
/// </para>
/// <para>
/// What a bad value does is <see cref="ConfigurationReading"/> rather than a setter here. A
/// property that threw would be a property the host's deserialiser throws out of, which takes
/// the plugin down at load instead of leaving it loaded and refusing to pair.
/// </para>
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        // set default options here
        Options = SomeOptions.AnotherOption;
        TrueFalseSetting = true;
        AnInteger = 2;
        AString = "string";

        PeerAddress = string.Empty;

        EnrolmentWindowSeconds = EnrolmentWindow.LifetimeSeconds;

        PeerPlaneWindowSeconds = ArrivalLimit.WindowSeconds;
        PeerPlaneArrivalsPerPairing = ArrivalLimit.ArrivalsPerPairing;
        PeerPlaneArrivalsPerEnrolment = ArrivalLimit.ArrivalsPerEnrolment;
    }

    /// <summary>
    /// Gets or sets a value indicating whether some true or false setting is enabled..
    /// </summary>
    public bool TrueFalseSetting { get; set; }

    /// <summary>
    /// Gets or sets an integer setting.
    /// </summary>
    public int AnInteger { get; set; }

    /// <summary>
    /// Gets or sets a string setting.
    /// </summary>
    public string AString { get; set; }

    /// <summary>
    /// Gets or sets an enum option.
    /// </summary>
    public SomeOptions Options { get; set; }

    /// <summary>
    /// Gets or sets the one address this server will send a pairing request to.
    /// </summary>
    /// <remarks>
    /// Empty on a fresh installation, which is a server that pairs with nobody. The forms
    /// that are accepted are <see cref="Protocol.PeerAddress"/>'s and are refused at load
    /// rather than at the moment a request is built, so an operator who mistyped one reads
    /// which rule refused it instead of watching an enrolment fail.
    /// </remarks>
    public string PeerAddress { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the operator has acknowledged what a cleartext
    /// peer address costs.
    /// </summary>
    /// <remarks>
    /// False on a fresh installation, and false again where the element is missing, because
    /// <c>false</c> is what the type produces for an absent element. Setting it to true
    /// permits an <c>http</c> peer address, and what that gives up is that request and
    /// response bodies, including the mapping table, are readable by anything on the path
    /// between the two servers. The setting exists rather than the permissive behaviour being
    /// silent, which is what the answer to decision 3 on issue #1 fixes: an operator who runs
    /// cleartext has read the sentence saying so and set this themselves.
    /// </remarks>
    public bool AcknowledgeCleartextTransport { get; set; }

    /// <summary>
    /// Gets or sets how long an enrolment window stays open, in seconds.
    /// </summary>
    /// <remarks>
    /// The window is the only moment this server answers a party it has not authenticated, so
    /// this is the setting on the configuration that decides how long that moment lasts. It is
    /// long enough by default for two operators who are already talking to each other to read
    /// an address to one side and press a button on the other. The argument for the default
    /// and for the maximum is at the constants in <see cref="EnrolmentWindow"/> rather than
    /// here, because that is where the bound is held.
    /// </remarks>
    public int EnrolmentWindowSeconds { get; set; }

    /// <summary>
    /// Gets or sets how long the peer plane counts an arrival allowance over, in seconds.
    /// </summary>
    /// <remarks>
    /// The window is fixed rather than sliding, so it starts at the first arrival counted into
    /// it and every arrival inside it counts against the same allowance. The argument for the
    /// default and for the bound is at the constants in <see cref="ArrivalLimit"/> rather than
    /// here, because that is where the behaviour is.
    /// </remarks>
    public int PeerPlaneWindowSeconds { get; set; }

    /// <summary>
    /// Gets or sets how many requests one pairing identifier may put on the peer plane inside
    /// a window.
    /// </summary>
    /// <remarks>
    /// This bounds the work a stranger who knows a pairing's identifier can make this server
    /// do. Raising it does not make a pairing faster; it makes a flood claiming that
    /// identifier cheaper.
    /// </remarks>
    public int PeerPlaneArrivalsPerPairing { get; set; }

    /// <summary>
    /// Gets or sets how many requests may arrive inside a window claiming the enrolment
    /// identifier, or claiming nothing the protocol can read an identifier out of.
    /// </summary>
    /// <remarks>
    /// The harder of the two, because it is the one a stranger reaches without knowing
    /// anything, and it is refused where an operator sets it above the other rather than
    /// quietly becoming the softer limit.
    /// </remarks>
    public int PeerPlaneArrivalsPerEnrolment { get; set; }
}
