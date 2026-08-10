namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Which of a pairing's live keys verified an arriving request.
/// </summary>
/// <remarks>
/// This separates two things that a plain accepted or refused cannot. A peer still signing
/// with the superseded key is a peer that has not caught up, and an operator has to be able to
/// see one before the overlap closes underneath it. What is written when that happens is the
/// rotation row of <c>docs/logging.md</c>.
/// </remarks>
public enum KeyInUse
{
    /// <summary>
    /// Neither live key verified the request.
    /// </summary>
    None = 0,

    /// <summary>
    /// The key this side is using for what it sends, which after a rotation is the
    /// replacement.
    /// </summary>
    Current = 1,

    /// <summary>
    /// The key the replacement supersedes, which verifies only while the overlap is open. A
    /// request reaching here is from a peer that has not started using the replacement.
    /// </summary>
    Superseded = 2,
}
