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
}
