namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// Why a request on the pairing plane was refused, as this server counts it for its own
/// administrator.
/// </summary>
/// <remarks>
/// This is not a second <see cref="RefusalCode"/> and it never reaches a caller. A code is
/// what a peer is told, and <c>docs/protocol.md</c> collapses many causes into
/// <see cref="RefusalCode.Refused"/> on purpose, so that what a stranger learns from an answer
/// is nothing. A cause is what this server writes down about itself, behind the host's
/// elevation policy, and is sent to nobody.
/// <para>
/// THE SEPARATION IS THE DECISION ISSUE #51 ASKED TO BE TAKEN BEFORE THE FIRST COUNTER WAS
/// WRITTEN, and it is taken here in this direction. A counter per code alone hands an operator
/// one bucket for every cause that collapses and answers none of the questions that issue opens
/// with - a clock problem, a peer sending too fast, a scanner on the wrong path. A counter per
/// cause alone would change what <c>refused</c> means to an operator reading two versions of
/// the payload, because a bucket that is split has a different total than the one it replaced.
/// So both are reported and only these are stored: a code's number is the sum of the causes
/// that map to it, which cannot move when a cause is added beside them.
/// </para>
/// <para>
/// THIS PARAGRAPH SAID EVERY MEMBER BELOW MAPS TO <see cref="RefusalCode.Refused"/>, BECAUSE
/// THAT WAS THE ONLY CODE ANY SITE IN THIS TREE PRODUCED. Three of them no longer do. The plane
/// judges freshness once a request has verified, so a caller that has already proved it holds
/// the pairing's key is told which of the three it met. The taxonomy in <c>docs/protocol.md</c>
/// is what allows that and bounds it: a distinguishable code to a caller holding the key, and
/// none to anyone else. Every other member still maps to <see cref="RefusalCode.Refused"/>, and
/// <see cref="RefusalCounters.CodeFor(RefusalCause)"/> is the one place that says which does
/// which. A member is added here when a site can distinguish it,
/// never ahead of one: a cause nothing produces is a number an operator reads as a measurement
/// and is not one. What each site refuses is <see cref="PeerPlane.Serve"/>, in the order that
/// method fixes, and that order is the security property rather than a style.
/// </para>
/// </remarks>
public enum RefusalCause
{
    /// <summary>
    /// The request was not one this plane serves: nothing arrived, or the path or the method
    /// was not the one the message belongs to. A scanner walking a server produces this and a
    /// peer does not.
    /// </summary>
    NotOnThisPlane = 0,

    /// <summary>
    /// The body was over the limit the message carries, so it was refused before a signature
    /// was computed. Separated from the member above because the two are repaired in opposite
    /// directions: this one is a peer that really is talking to this plane and is sending more
    /// than the specification allows.
    /// </summary>
    BodyOverItsLimit = 1,

    /// <summary>
    /// The identifier the request claimed has used its allowance for the window it is in.
    /// </summary>
    ArrivalAllowanceSpent = 2,

    /// <summary>
    /// Every identifier the arrival limit can count is in use, so this arrival could not be
    /// counted and was refused rather than admitted uncounted. <see cref="ArrivalOutcome"/>
    /// separates this from the member above for this surface by name, because a peer sending
    /// too fast and this server having run out of room to count are repaired in opposite
    /// directions.
    /// </summary>
    NoRoomToCountTheArrival = 3,

    /// <summary>
    /// The signature did not verify against a key this server holds for the pairing the
    /// request named. Every way that can happen is one outcome, which is
    /// <see cref="Protocol.VerificationOutcome"/>'s own decision and is not undone here: this
    /// cause is as fine-grained as the verification is, and no finer.
    /// </summary>
    DidNotVerify = 4,

    /// <summary>
    /// The signature verified and the message is not accepted in the state the pairing is in.
    /// On a server today every pairing is <see cref="Protocol.PairingState.Absent"/> on this
    /// plane, so this is what a peer holding a good key meets. THIS SENTENCE GAVE THE REASON AS
    /// NO RECORD STORE EXISTING, and one does: what nothing in this directory does is read it.
    /// That the code answered for it is <see cref="RefusalCode.Refused"/> rather than
    /// <see cref="RefusalCode.State"/> is issue #287 and is not decided here.
    /// </summary>
    NotAcceptedInThisState = 5,

    /// <summary>
    /// The signature verified and the timestamp is further from this server's clock than the
    /// tolerated skew allows, in either direction. A request from the future is as suspicious
    /// as one from the past, so both directions are this one cause.
    /// </summary>
    /// <remarks>
    /// This is the one distinction <c>docs/threat-model.md</c> keeps deliberately rather than
    /// collapsing. It hands a caller one bit, which is whether their timestamp was inside this
    /// server's window, and that bit is in the specification already; what it buys is an
    /// operator on two home servers reading a clock refusal instead of debugging a signature
    /// failure that is really a clock error. It is reached only after verification, so nobody
    /// without a verifying key ever sees it.
    /// </remarks>
    TimestampOutsideTheWindow = 6,

    /// <summary>
    /// The signature verified, the timestamp is inside the window, and this nonce has already
    /// been seen for this pairing. What this counts is a correctly signed request that was
    /// captured and sent again, so a number here that is not zero says something none of the
    /// others do.
    /// </summary>
    NonceAlreadySeen = 7,

    /// <summary>
    /// The signature verified, the request is fresh, and this pairing has no room left to
    /// remember another nonce, so it is refused rather than remembered. Separated from the
    /// member above for the reason <see cref="NoRoomToCountTheArrival"/> is separated from
    /// <see cref="ArrivalAllowanceSpent"/>: a peer replaying and this server having run out of
    /// room are repaired in opposite directions.
    /// </summary>
    NoRoomToRememberTheNonce = 8,
}
