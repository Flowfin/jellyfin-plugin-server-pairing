namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// What happened to a request to map a user.
/// </summary>
/// <remarks>
/// Separate values rather than a boolean, because an administrator who is told only that
/// something failed goes looking in the wrong place. Each one names what was wrong.
/// </remarks>
public enum MappingOutcome
{
    /// <summary>
    /// The mapping is held.
    /// </summary>
    Mapped = 0,

    /// <summary>
    /// No pairing with that identifier exists here, so there is nothing for a mapping to
    /// belong to. A mapping cannot exist without a pairing, and this is that rule refusing.
    /// </summary>
    NoSuchPairing = 1,

    /// <summary>
    /// The pairing is revoked. It keeps its record on purpose so a later request naming it is
    /// refused rather than treated as new, and this is one of those requests.
    /// </summary>
    PairingIsOver = 2,

    /// <summary>
    /// This local user is already mapped under this pairing. The mapping that is there is
    /// left exactly as it was.
    /// </summary>
    /// <remarks>
    /// Refused rather than replaced, and the difference is the whole of it. A replacement
    /// looks like a correction and is not one: everything that already arrived under the old
    /// mapping stays on the user it arrived on, so an administrator who has been silently
    /// given a new mapping has been told a repair happened. Removing the mapping and making
    /// the new one is two acts, which is what it actually is.
    /// </remarks>
    LocalUserAlreadyMapped = 3,

    /// <summary>
    /// This peer user is already mapped, to a different local user, under this pairing.
    /// </summary>
    /// <remarks>
    /// The other direction of the same rule, and the one that is easy to leave out. A table
    /// that refuses a second peer user for one local user and accepts a second local user for
    /// one peer user sends two people's data to one account, which is the failure this whole
    /// model exists against arriving through the side nobody guarded.
    /// </remarks>
    PeerUserAlreadyMapped = 4,
}
