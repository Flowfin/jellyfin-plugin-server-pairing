namespace Jellyfin.Plugin.ServerPairing.Matching;

/// <summary>
/// What the matcher decided. The five values are the rows of the outcome table in
/// <c>docs/matching.md</c>, and the reason there are five rather than two is that the four
/// ways of not matching are four different problems for whoever has to look at them.
/// </summary>
public enum MatchOutcome
{
    /// <summary>
    /// No local candidate shares a matched provider with the peer item, and none of them
    /// contradicts it either. The far side has a film this side does not.
    /// </summary>
    NoCandidate = 0,

    /// <summary>
    /// One local item, or several that agree with each other, carry an identifier that
    /// agrees with the peer item.
    /// </summary>
    Matched = 1,

    /// <summary>
    /// More than one local item matches the peer item and they disagree with each other.
    /// Nothing is chosen.
    /// </summary>
    Ambiguous = 2,

    /// <summary>
    /// Nothing matched, and at least one candidate gives a different value for a provider
    /// the peer item also carries. The metadata on the two servers contradicts itself.
    /// </summary>
    Disagreement = 3,

    /// <summary>
    /// The peer item carries nothing the matcher is allowed to compare. Not a statement
    /// about the local library.
    /// </summary>
    NoIdentifiers = 4
}
