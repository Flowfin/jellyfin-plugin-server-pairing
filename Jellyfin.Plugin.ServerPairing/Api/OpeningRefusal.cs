namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// Why an administrator's request to open an enrolment window was refused.
/// </summary>
/// <remarks>
/// A refusal is this plugin declining to open a window against a state it already holds, and
/// that is a different thing from a named problem: <see cref="AdministrativeProblem"/> is a
/// store that could not be read or a caller that could not be named, and the request would have
/// been carried out otherwise. Here the request as it stands is one this server will not carry
/// out, so the answer has its own word and its own status rather than borrowing those.
/// <para>
/// The first two are the refusals <see cref="Protocol.EnrolmentWindow"/> already holds, carried
/// to the wire under a word each rather than collapsed into one. The other two are the plane's
/// own: a configuration a setting was refused on is one this plugin does not pair on, which
/// <c>docs/configuration.md</c> fixes, and a server nobody has entered a peer address on has
/// nothing to open a window against.
/// </para>
/// </remarks>
public enum OpeningRefusal
{
    /// <summary>
    /// This server already holds a pairing with the configured peer.
    /// </summary>
    AlreadyPaired = 0,

    /// <summary>
    /// A window is already open against the configured peer.
    /// </summary>
    AlreadyOpen = 1,

    /// <summary>
    /// A setting was refused when the configuration was read, so this plugin will not pair until
    /// it is corrected.
    /// </summary>
    ConfigurationRefused = 2,

    /// <summary>
    /// No peer address has been entered, so there is nothing to open a window against.
    /// </summary>
    NoPeerAddress = 3,
}
