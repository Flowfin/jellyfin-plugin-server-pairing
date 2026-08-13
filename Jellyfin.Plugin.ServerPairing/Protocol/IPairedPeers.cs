namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Whether this server already has a pairing with the peer at an address.
/// </summary>
/// <remarks>
/// An enrolment window may not be opened against a peer this server is already paired with,
/// and reaching that answer by pairing identifier does not work: the identifier is derived
/// from both public keys, so a peer offering a different key produces a different identifier,
/// the existing pairing is not found, and a fresh window opens beside a live relationship.
/// That is the displacement the rule exists against, so the question is asked by address.
/// <para>
/// This is the narrow view of the enumeration issue #30 owes its callers, declared where it is
/// read so that <see cref="EnrolmentWindow"/> can be built and proved before a store exists.
/// It answers one question and returns no record, no key material and no count.
/// </para>
/// <para>
/// <see cref="PairingRecord"/> carries no peer address today, so an implementation over the
/// landed record cannot answer this. Issue #18 claims that field rather than leaving it owed
/// by nobody, and the field lands with the store that has to read it.
/// </para>
/// </remarks>
public interface IPairedPeers
{
    /// <summary>
    /// Whether a pairing is held here for the peer at an address.
    /// </summary>
    /// <param name="address">The address an administrator entered.</param>
    /// <returns>True where a pairing is held, in any state a record survives in.</returns>
    /// <remarks>
    /// A revoked pairing keeps its record on purpose, and this answers true for one. Opening a
    /// window against a revoked peer is re-pairing, which means new key material and therefore
    /// a different identifier, and it is not something an enrolment window does silently.
    /// </remarks>
    bool HasPairingWith(PeerAddress address);
}
