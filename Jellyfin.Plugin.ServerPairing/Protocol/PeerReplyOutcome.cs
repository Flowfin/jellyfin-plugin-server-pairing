namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What came back from the one address a pairing is allowed to talk to.
/// </summary>
public enum PeerReplyOutcome
{
    /// <summary>
    /// The peer answered inside the bound, at the address the operator approved.
    /// </summary>
    Answered = 0,

    /// <summary>
    /// The peer answered with a redirect. It is refused rather than followed, because a
    /// redirect is a peer moving the conversation to somewhere no operator approved.
    /// </summary>
    Redirected = 1,

    /// <summary>
    /// The answer was larger than the limit for this message, and the rest of it was not
    /// read.
    /// </summary>
    TooLarge = 2,

    /// <summary>
    /// The peer could not be reached.
    /// </summary>
    Unreachable = 3,

    /// <summary>
    /// The peer held the request past the total timeout. It is refused so that a hostile
    /// peer cannot hold a request thread open.
    /// </summary>
    TimedOut = 4,
}
