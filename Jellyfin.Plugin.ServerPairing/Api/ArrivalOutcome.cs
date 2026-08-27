namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// What <see cref="ArrivalLimit"/> says about one arriving request.
/// </summary>
/// <remarks>
/// The caller is told one thing on the wire whichever of the two refusing members comes back.
/// They are separated here because an operator reading a diagnostics surface has to be able to
/// tell a peer that is sending too fast from this server having run out of room to count, and
/// those are repaired in opposite directions.
/// </remarks>
public enum ArrivalOutcome
{
    /// <summary>
    /// The request is inside the allowance for the identifier it claims, and it has been
    /// counted against it.
    /// </summary>
    Admitted = 0,

    /// <summary>
    /// The identifier this request claims has used its allowance for the window it is in.
    /// </summary>
    TooMany = 1,

    /// <summary>
    /// Every identifier this type can count is in use by a window that has not elapsed, so
    /// this one cannot be counted. A request that cannot be counted is refused rather than
    /// admitted uncounted, and no counted identifier is displaced to make room for it.
    /// </summary>
    NoRoomToCount = 2,
}
