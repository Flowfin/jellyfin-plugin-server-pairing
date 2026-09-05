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
/// AND NOTHING ELSE, THEN THAT IT PRODUCES FOUR CODES, THEN FIVE. It produces six: the plane
/// judges the declared version, then freshness, then the body against its member table, once a
/// request has verified, so <see cref="Version"/>, <see cref="Clock"/>, <see cref="Replay"/>,
/// <see cref="Busy"/> and <see cref="Malformed"/> are answered to a caller that has proved it
/// holds the pairing's key. What is unchanged is what an unauthenticated caller gets, which is
/// <see cref="Refused"/> and only that, because every one of those judgements is made after
/// verification and never before it.
/// <para>
/// <see cref="State"/> is the one member no site produces. Which code the undistinguished
/// refusal below should become once a pairing record is read on this plane is issue #287.
/// </para>
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
    /// verifying key, may see it. THIS SENTENCE SAID NO SITE PRODUCES IT.
    /// <see cref="PeerPlane.Serve"/> produces it for the second of those two callers: a request
    /// whose signature verified and whose declared version is outside
    /// <see cref="Protocol.SupportedVersions.Range"/> is answered this, with the range in the
    /// body. THAT SENTENCE WENT ON TO SAY THE OTHER CAUSE OF THIS CODE - a <c>hello</c> whose
    /// range does not overlap this server's - NEEDED A BODY TO BE PARSED AND HAD NO SITE. One
    /// parses a body, and <see cref="PeerPlane.Serve"/> answers this for that cause as well.
    /// <para>
    /// The two CAUSES both have a site; the two CALLERS do not, and that is the distinction to
    /// keep. Both sites are reached after verification, so what sees this code today is a caller
    /// holding a verifying key. A caller inside an open enrolment window is admitted by nothing,
    /// because a <c>hello</c> proves possession of the key it offers and no route here verifies
    /// that.
    /// </para>
    /// </summary>
    Version = 2,

    /// <summary>
    /// The signature verified and the message is not accepted in this state. Only a caller
    /// holding a verifying key ever sees it. No site produces it yet.
    /// </summary>
    State = 3,

    /// <summary>
    /// The signature verified and the body does not parse, or a field is outside its limit.
    /// Only a caller holding a verifying key ever sees it. THIS SENTENCE SAID NO SITE PRODUCES
    /// IT, BECAUSE NOTHING ON THIS PLANE PARSED A BODY. <see cref="PeerPlane.Serve"/> produces
    /// it: <see cref="Protocol.ArrivingBody"/> reads a body that has verified against the member
    /// table in <c>docs/protocol.md</c>, and this is the answer where it is not the body that
    /// table fixes.
    /// <para>
    /// Two bodies cannot reach it, and that is the document rather than an omission. A
    /// <c>rotate</c> body has a member table and no reader here yet, and an <c>exchange</c> body
    /// is opaque to this layer, so neither is judged and neither is answered this.
    /// </para>
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
