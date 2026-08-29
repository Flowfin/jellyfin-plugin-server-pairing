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
/// Every member below maps to <see cref="RefusalCode.Refused"/> today, because that is the only
/// code any site in this tree produces. A member is added here when a site can distinguish it,
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
    /// On a server today every pairing is <see cref="Protocol.PairingState.Absent"/>, because
    /// no record store exists, so this is what a peer holding a good key meets. That the code
    /// answered for it is <see cref="RefusalCode.Refused"/> rather than
    /// <see cref="RefusalCode.State"/> is issue #287 and is not decided here.
    /// </summary>
    NotAcceptedInThisState = 5,
}
