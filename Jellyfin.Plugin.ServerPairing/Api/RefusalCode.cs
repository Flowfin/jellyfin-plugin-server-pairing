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
/// deliberately so. THIS PARAGRAPH SAID <see cref="PeerPlane"/> PRODUCES <see cref="Refused"/>
/// AND NOTHING ELSE. It produces four codes now: the plane judges freshness once a request has
/// verified, so <see cref="Clock"/>, <see cref="Replay"/> and <see cref="Busy"/> are answered
/// to a caller that has proved it holds the pairing's key. What is unchanged is what an
/// unauthenticated caller gets, which is <see cref="Refused"/> and only that, because freshness
/// is judged after verification and never before it.
/// </para>
/// <para>
/// The <c>Absent</c> row of the transition table is still the undistinguished refusal for all
/// six messages, so a request that is fresh and verified is answered <see cref="Refused"/>
/// while nothing on this plane reads a pairing record. THIS SENTENCE SAID NO KEY STORE EXISTS
/// EITHER, and one does and is read on that path, which is issue #287. IT THEN WENT ON SAYING
/// NO RECORD STORE EXISTS, AND THAT HALF WAS WRONG IN THE SAME WAY AND FOR LONGER:
/// <see cref="Protocol.FilePairingRecordStore"/> ships and is registered, and what is absent is
/// a reader for it in this directory. One line drifted twice about two stores, which is why the
/// correction names the reader rather than the store. The rest are named here so the taxonomy
/// has one expression in code rather than a partial one that grows a second.
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
    /// caller holding a verifying key ever sees it. THIS SENTENCE SAID NO SITE PRODUCES IT AND
    /// THAT NOTHING ON THIS PLANE CONSULTS THE WINDOW. <see cref="PeerPlane.Serve"/> consults
    /// one, and this is what it answers where the timestamp is further from this server's clock
    /// than the tolerated skew allows.
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
    /// The signature verified, the timestamp is inside the window, and this nonce has already
    /// been seen for this pairing. Only a caller holding a verifying key ever sees it.
    /// <see cref="PeerPlane.Serve"/> produces it.
    /// </summary>
    Replay = 5,

    /// <summary>
    /// The signature verified, the request is fresh, and this pairing has no room left to
    /// remember another nonce. Only a caller holding a verifying key ever sees it.
    /// <see cref="PeerPlane.Serve"/> produces it.
    /// </summary>
    Busy = 6,
}
