namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What a revocation did on this server.
/// </summary>
/// <remarks>
/// <see cref="NothingToRevoke"/> is the default on purpose. A value nobody set says that no
/// revocation is known to have happened, which is the direction a caller can act on safely; a
/// default reading as <see cref="Revoked"/> would let an uninitialised outcome be reported to
/// an operator as a link that has been stopped.
/// </remarks>
public enum RevocationOutcome
{
    /// <summary>
    /// The pairing was already <see cref="PairingState.Revoked"/>, or this server never held it
    /// at all, so no transition was recorded. The key store was swept either way.
    /// </summary>
    NothingToRevoke = 0,

    /// <summary>
    /// The key material is destroyed and the pairing is recorded as
    /// <see cref="PairingState.Revoked"/>.
    /// </summary>
    Revoked = 1,
}
