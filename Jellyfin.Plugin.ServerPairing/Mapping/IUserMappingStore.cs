using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// Where the mappings for a pairing are kept between one request and the next.
/// </summary>
/// <remarks>
/// This lives with the pairing state rather than in the plugin configuration, for the same
/// reason the key store does: the configuration is a file an operator edits by hand and the
/// host rewrites as plaintext XML, and a table that decides where one person's data goes is
/// not something to leave in it.
/// <para>
/// Every operation is keyed by pairing first, because a mapping outside a pairing is the one
/// thing this model does not allow. There is deliberately no way to ask this store for every
/// mapping it holds regardless of pairing: a caller that wants them all walks the pairings.
/// </para>
/// <para>
/// Adding is not on this interface by accident. Writes go through
/// <see cref="UserMappings"/>, which is the only type that may decide a mapping exists,
/// because it is the only one that checks a pairing is there to hold it.
/// </para>
/// </remarks>
public interface IUserMappingStore
{
    /// <summary>
    /// The mappings held for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>The mappings, in no particular order, empty where the pairing holds none.</returns>
    IReadOnlyList<UserMapping> For(string pairingId);

    /// <summary>
    /// Puts a mapping in place of whatever was held for the same pairing and local user.
    /// </summary>
    /// <param name="mapping">The mapping to keep.</param>
    /// <remarks>
    /// Called by <see cref="UserMappings"/> and by nothing else. A caller reaching this
    /// directly has skipped the check that a pairing exists, which is the property the whole
    /// model rests on.
    /// <para>
    /// THE REPLACEMENT THIS METHOD WILL PERFORM IS NOT REACHABLE THROUGH THAT ONE CALLER, and
    /// the wording above is kept as it is on purpose. <see cref="UserMappings.Map"/> refuses a
    /// second mapping for a local user or for a peer user rather than passing it here, so a
    /// store implementing this by overwriting is correct and is never asked to. The wording
    /// stays because it is what an implementer has to do when it IS asked - a partial write
    /// leaving both rows is worse than either outcome.
    /// </para>
    /// </remarks>
    void Put(UserMapping mapping);

    /// <summary>
    /// Removes the mapping held for one local user under one pairing, if there is one.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="localUserId">The user on this server.</param>
    void Remove(string pairingId, string localUserId);

    /// <summary>
    /// Removes every mapping held for a pairing, and everything cached beside them.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <remarks>
    /// This is what a pairing ending means for the mapping table. It is driven by the
    /// transition into <see cref="Protocol.PairingState.Revoked"/> or
    /// <see cref="Protocol.PairingState.Absent"/> rather than by a record being deleted,
    /// because a revoked pairing keeps its record on purpose and its mappings still have to
    /// go.
    /// </remarks>
    void RemoveEvery(string pairingId);
}
