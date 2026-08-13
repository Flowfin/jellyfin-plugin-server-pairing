namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What presenting a hello at an enrolment window produced.
/// </summary>
/// <remarks>
/// Two values and no more, for the reason <see cref="VerificationOutcome"/> has two. Whoever
/// presents a hello has authenticated nothing yet, so a window that was never opened, one that
/// has closed on its timer, one that has already been used, and one that has been given more
/// failures than it allows all produce the same value through the same return. A caller cannot
/// learn from it whether an administrator here has ever heard of them.
/// </remarks>
public enum WindowUse
{
    /// <summary>
    /// Refused. Every cause produces this value.
    /// </summary>
    Refused = 0,

    /// <summary>
    /// Accepted, and the window is closed by that acceptance. What the hello carries goes on
    /// from here to the state machine and the key store; the window's part is over.
    /// </summary>
    Accepted = 1,
}
