namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What asking for an enrolment window produced.
/// </summary>
/// <remarks>
/// These values are read by an administrator on the dashboard rather than by a peer. Nothing
/// unauthenticated reaches this: a window is opened by the operator who owns the server, and a
/// stranger cannot ask for one. So naming the reason costs nothing here, which is the opposite
/// of the position <see cref="WindowUse"/> takes.
/// </remarks>
public enum WindowOpening
{
    /// <summary>
    /// The window is open and this server will answer a hello naming that address until it
    /// closes.
    /// </summary>
    Opened = 0,

    /// <summary>
    /// A window is already open against that address. It is not reopened and its lifetime does
    /// not move, because a window that can be extended is a window that never closes. Closing
    /// it and opening a fresh one is the operator's to do and is visible on the dashboard.
    /// </summary>
    AlreadyOpen = 1,

    /// <summary>
    /// A pairing with that peer is already held here. A fresh window beside a live relationship
    /// is how one is displaced, so it is refused rather than opened.
    /// </summary>
    AlreadyPaired = 2,
}
