namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The five request types the pairing plane carries.
/// </summary>
/// <remarks>
/// The list and the path each one arrives on are <c>docs/protocol.md</c>. This enumeration
/// names them so the transition table can be written as a total function over state and
/// message rather than as a chain of conditions.
/// </remarks>
public enum PairingMessage
{
    /// <summary>
    /// The first message of an enrolment, carrying the sender's public key. It is the one
    /// message that cannot name a real pairing identifier.
    /// </summary>
    Hello = 0,

    /// <summary>
    /// The peer's operator compared the fingerprint and confirmed.
    /// </summary>
    Confirm = 1,

    /// <summary>
    /// A replacement key, with the instant the old one stops verifying.
    /// </summary>
    Rotate = 2,

    /// <summary>
    /// The peer ends the pairing. Unilateral, and accepted in every state where this side
    /// holds the peer's key. <see cref="PairingState.Offered"/> is the exception: a record
    /// exists there and no peer key has arrived, so an arriving revoke carries no signature
    /// this side could verify and is refused.
    /// </summary>
    Revoke = 3,

    /// <summary>
    /// Traffic for a consumer. Opaque to this layer.
    /// </summary>
    Exchange = 4,
}
