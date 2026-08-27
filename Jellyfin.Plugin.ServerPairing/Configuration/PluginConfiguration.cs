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
/// A missing element deserialises to the type's default rather than to the value the
/// constructor set, so every setting's safe value is the one its type already has where that
/// is possible: <c>false</c> for a switch that weakens something, and an empty address for a
/// peer nobody has entered. A setting whose safe value is not its type's default is set in
/// the constructor and is documented as the value a fresh installation gets.
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
}
