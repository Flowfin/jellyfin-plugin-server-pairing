namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The six request types the pairing plane carries.
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

    /// <summary>
    /// The peer's operator has finished with the pairing and asks this side to end it too.
    /// Accepted in exactly the states <see cref="Revoke"/> is accepted in, and for the same
    /// reason: from <see cref="PairingState.Pending"/> onwards this side holds the peer's key
    /// and can verify the request, and in <see cref="PairingState.Offered"/> it cannot. The
    /// receiving side completes its own side and asks its operator for nothing, because the
    /// pairing is already gone from the other end. What separates the two here is the cause
    /// written on the record, which names the message, so an operator can tell a peer that
    /// unpaired from a peer that revoked. What separates them on the sending side is the
    /// order, which issue #56 fixes: unpairing asks the peer first and revoking asks the peer
    /// for nothing.
    /// </summary>
    Unpair = 5,
}
