namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The codes a refusal on the pairing plane may carry.
/// </summary>
/// <remarks>
/// The error taxonomy in <c>docs/protocol.md</c> is the authority for this list and for who
/// may ever see each member. Every refusal is the same HTTP status with the same one-member
/// body, and only the code varies, so a member added here without a row there is a code no
/// peer can interpret.
/// <para>
/// Which members this tree can currently produce is a smaller set than this enumeration, and
/// deliberately so. <see cref="PeerPlane"/> produces <see cref="Refused"/> and nothing else,
/// because every pairing is <see cref="Protocol.PairingState.Absent"/> while no key store and
/// no record store exist, and the <c>Absent</c> row of the transition table is the
/// undistinguished refusal for all five messages. The rest are named here so the taxonomy has
/// one expression in code rather than a partial one that grows a second.
/// </para>
/// </remarks>
public enum RefusalCode
{
    /// <summary>
    /// The undistinguished refusal. Anyone may see it, and every cause that produces it
    /// produces the same bytes, which is what makes probing useless.
    /// </summary>
    Refused = 0,

    /// <summary>
    /// The signature verified and the timestamp is outside the freshness window. Only a
    /// caller holding a verifying key ever sees it. No site produces it yet; the freshness
    /// window is landed and nothing on this plane consults it.
    /// </summary>
    Clock = 1,

    /// <summary>
    /// No version in common. A caller inside an open enrolment window, or one holding a
    /// verifying key, may see it. No site produces it yet.
    /// </summary>
    Version = 2,

    /// <summary>
    /// The signature verified and the message is not accepted in this state. Only a caller
    /// holding a verifying key ever sees it. No site produces it yet.
    /// </summary>
    State = 3,

    /// <summary>
    /// The signature verified and the body does not parse, or a field is outside its limit.
    /// Only a caller holding a verifying key ever sees it. No site produces it yet, because
    /// nothing on this plane parses a body.
    /// </summary>
    Malformed = 4,

    /// <summary>
    /// The signature verified, the request is fresh, and this nonce has already been seen for
    /// this pairing. Only a caller holding a verifying key ever sees it. No site produces it
    /// yet.
    /// </summary>
    Replay = 5,

    /// <summary>
    /// The signature verified, the request is fresh, and this pairing has no room left to
    /// remember another nonce. Only a caller holding a verifying key ever sees it. No site
    /// produces it yet.
    /// </summary>
    Busy = 6,
}
