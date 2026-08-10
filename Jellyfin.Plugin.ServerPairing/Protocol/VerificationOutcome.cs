namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What verifying a request produced.
/// </summary>
/// <remarks>
/// Two values and no more. Every way a request can fail to authenticate is one outcome,
/// because a refusal that says which way tells an unauthenticated caller something: whether
/// the pairing exists, whether the signature was the wrong length, whether a field was
/// malformed. The refusal codes a caller that already verified may see are a different
/// question and are issue #28.
/// </remarks>
public enum VerificationOutcome
{
    /// <summary>
    /// The request did not authenticate. Every cause produces this value.
    /// </summary>
    Refused = 0,

    /// <summary>
    /// The request was made by a holder of the pairing's key and none of the covered fields
    /// moved on the way.
    /// </summary>
    Verified = 1,
}
