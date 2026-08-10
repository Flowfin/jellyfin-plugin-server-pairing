namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What proposing a replacement key produced.
/// </summary>
/// <remarks>
/// Every value other than <see cref="Rotated"/> leaves the pairing exactly as it was, on the
/// key it was already using and still able to talk. A rotation that cannot start is a rotation
/// that did not happen, never a pairing left holding nothing.
/// </remarks>
public enum RotationOutcome
{
    /// <summary>
    /// The replacement is in use for what this side sends, and the superseded key goes on
    /// verifying what arrives until the overlap closes.
    /// </summary>
    Rotated = 0,

    /// <summary>
    /// A rotation is already inside its overlap. A second one is refused rather than accepted,
    /// because accepting it either extends the overlap past its maximum or leaves three keys
    /// live, and both are the thing the maximum exists to prevent.
    /// </summary>
    AlreadyRotating = 1,

    /// <summary>
    /// The instant the superseded key stops verifying is not after the instant the rotation
    /// starts, or is further ahead than the maximum overlap allows.
    /// </summary>
    OutsideTheMaximum = 2,

    /// <summary>
    /// The replacement is not the length of a key, so there is nothing to rotate to.
    /// </summary>
    Malformed = 3,

    /// <summary>
    /// The replacement is the key already in use. Accepting it would spend the one overlap on
    /// nothing and leave the pairing believing it had rotated.
    /// </summary>
    NotAReplacement = 4,
}
