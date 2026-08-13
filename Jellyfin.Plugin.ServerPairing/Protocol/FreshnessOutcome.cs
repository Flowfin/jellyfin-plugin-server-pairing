namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What judging a request's freshness produced.
/// </summary>
/// <remarks>
/// Freshness is judged after a request has verified, so every value here is only ever reached
/// by a caller holding the pairing's key. That is what makes it safe for them to differ: the
/// error taxonomy in <c>docs/protocol.md</c> allows a distinguishable code to a caller that
/// already proved it holds the key, and allows none to anyone else. Which code each of these
/// becomes on the wire is fixed by that taxonomy rather than here, and nothing in this tree
/// performs the mapping, because there is no endpoint that would.
/// </remarks>
public enum FreshnessOutcome
{
    /// <summary>
    /// Inside the window and not seen before for this pairing.
    /// </summary>
    Fresh = 0,

    /// <summary>
    /// The timestamp is not an unsigned decimal integer inside its digit limit. The shape
    /// check refuses this before a signature is computed, so reaching it here means a caller
    /// bypassed that; it is a value rather than an exception so that the ordering can be
    /// tested rather than assumed.
    /// </summary>
    Malformed = 1,

    /// <summary>
    /// The timestamp is further from this server's clock than the window allows, in either
    /// direction. A request from the future is as suspicious as one from the past.
    /// </summary>
    OutsideTheWindow = 2,

    /// <summary>
    /// Inside the window, and this nonce has already been seen for this pairing.
    /// </summary>
    AlreadySeen = 3,

    /// <summary>
    /// Inside the window, not seen before, and there is no room left to remember it. The
    /// request is refused rather than remembered, because the alternative is forgetting a
    /// nonce that is still inside the window, which turns a bounded store into a replay
    /// window an attacker opens by filling it.
    /// </summary>
    NoRoomToRemember = 4,
}
