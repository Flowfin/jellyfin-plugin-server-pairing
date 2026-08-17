namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What selecting a protocol version for a pairing produced.
/// </summary>
/// <remarks>
/// Which code each of these becomes on the wire is fixed by the error taxonomy in
/// <c>docs/protocol.md</c> rather than here, and nothing in this tree performs that mapping,
/// because there is no endpoint that would. The taxonomy gives no version in common the
/// <c>version</c> code, and gives it only to a caller inside an open enrolment window or one
/// holding a verifying key. To anyone else it is the one undistinguished refusal, like
/// everything else.
/// </remarks>
public enum VersionOutcome
{
    /// <summary>
    /// The two ranges do not overlap. There is no version both sides speak, so there is
    /// nothing to fall back to: a message a server does not understand is one it cannot make a
    /// security decision about, so it is refused rather than parsed as best it can be.
    /// </summary>
    NoVersionInCommon = 0,

    /// <summary>
    /// A version both sides speak was selected. It is fixed for the life of the pairing from
    /// here, and line 2 of the canonical form binds it to every later request.
    /// </summary>
    Selected = 1,
}
