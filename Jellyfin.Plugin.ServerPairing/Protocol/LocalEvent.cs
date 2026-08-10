namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The things that happen on this side and move a pairing without any message arriving.
/// </summary>
/// <remarks>
/// The list is the local events table in <c>docs/protocol.md</c>. An administrator confirming
/// appears once here and twice there, because the document gives it two rows to name its two
/// destinations; which one applies follows from the state it is applied to.
/// </remarks>
public enum LocalEvent
{
    /// <summary>
    /// An administrator opens an enrolment window against a peer address.
    /// </summary>
    WindowOpened = 0,

    /// <summary>
    /// An administrator compares the fingerprint and confirms.
    /// </summary>
    FingerprintConfirmed = 1,

    /// <summary>
    /// The enrolment window expires.
    /// </summary>
    WindowExpired = 2,

    /// <summary>
    /// An administrator revokes.
    /// </summary>
    AdministratorRevoked = 3,

    /// <summary>
    /// The rotation overlap closes.
    /// </summary>
    RotationOverlapClosed = 4,
}
