namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Why a candidate peer address was accepted or refused.
/// </summary>
/// <remarks>
/// Each refusal names the rule that refused it rather than reporting one invalid-address
/// answer for every cause. The value never reaches a peer: it is what an operator reads on
/// the dashboard after typing an address, and an operator who is told only that an address
/// is wrong retypes the same address.
/// </remarks>
public enum PeerAddressOutcome
{
    /// <summary>
    /// The candidate is one of the forms this plugin talks to.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// Nothing was entered.
    /// </summary>
    Empty = 1,

    /// <summary>
    /// Longer than the specification allows.
    /// </summary>
    TooLong = 2,

    /// <summary>
    /// A character that has no place in an address, or a form no absolute URI parse accepts.
    /// </summary>
    NotAnAbsoluteUri = 3,

    /// <summary>
    /// A scheme other than the one the pairing plane runs over.
    /// </summary>
    SchemeNotAllowed = 4,

    /// <summary>
    /// A user or a password in front of the host.
    /// </summary>
    UserInfoPresent = 5,

    /// <summary>
    /// A host that is neither a plain ASCII domain name, an IPv4 literal, nor a bracketed
    /// IPv6 literal.
    /// </summary>
    HostFormNotAllowed = 6,

    /// <summary>
    /// A port outside the range a port may take.
    /// </summary>
    PortNotAllowed = 7,

    /// <summary>
    /// A path. The pairing plane owns its own paths and appends them to the address.
    /// </summary>
    PathPresent = 8,

    /// <summary>
    /// A query string.
    /// </summary>
    QueryPresent = 9,

    /// <summary>
    /// A fragment.
    /// </summary>
    FragmentPresent = 10,
}
