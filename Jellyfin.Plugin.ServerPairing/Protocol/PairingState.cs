namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What this server believes about one pairing.
/// </summary>
/// <remarks>
/// Eight, per pairing, per side, and the list is <c>docs/protocol.md</c>. The peer holds its
/// own state and the two are not assumed to agree. A difference between this enumeration and
/// the table in that document is a defect in this file.
/// </remarks>
public enum PairingState
{
    /// <summary>
    /// No pairing record with this identifier exists here. Every identifier that was never
    /// enrolled is in this state, and so is one whose record has been deleted.
    /// </summary>
    Absent = 0,

    /// <summary>
    /// An administrator here opened an enrolment window against a peer address. This side's
    /// key pair exists; no peer key has arrived.
    /// </summary>
    Offered = 1,

    /// <summary>
    /// A peer key arrived inside the window and the fingerprint is on the dashboard. Neither
    /// operator has confirmed.
    /// </summary>
    Pending = 2,

    /// <summary>
    /// The administrator on this server compared and confirmed. The peer has not confirmed, or
    /// its confirmation has not arrived.
    /// </summary>
    ConfirmedHere = 3,

    /// <summary>
    /// The peer's confirmation arrived and verified. The administrator on this server has not
    /// confirmed.
    /// </summary>
    ConfirmedByPeer = 4,

    /// <summary>
    /// Both confirmations are in. This is the only state in which an exchange is answered,
    /// together with <see cref="Rotating"/>.
    /// </summary>
    Active = 5,

    /// <summary>
    /// Active, and a key rotation is inside its overlap window, so two of this side's keys
    /// verify. The overlap is issue #23.
    /// </summary>
    Rotating = 6,

    /// <summary>
    /// Terminal. Nothing moves out of it, and the record is kept rather than deleted so that a
    /// later request naming this identifier is refused rather than treated as new. Revocation
    /// is issue #24.
    /// </summary>
    Revoked = 7,
}
