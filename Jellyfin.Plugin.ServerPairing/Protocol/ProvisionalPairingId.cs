using System;
using System.Security.Cryptography;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The identifier a pairing is held under while it is <see cref="PairingState.Offered"/> and
/// no peer key has arrived to derive the real one from.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> is where this is argued, under the local events table, and it is the
/// authority: a difference between that section and this file is a defect in this file. What is
/// here is the shape, because the shape is the whole of the safety.
/// <para>
/// A pairing identifier on the wire is exactly <see cref="FieldShape.HexFieldLength"/> lowercase
/// hex characters. A provisional one is <see cref="Prefix"/> followed by that many, so it is
/// longer than a wire identifier and carries a byte no hex field may hold. THE TWO NAMESPACES
/// ARE THEREFORE DISJOINT BY CONSTRUCTION rather than by anybody remembering to keep them apart:
/// an arriving request naming one of these is refused by the field shape before any store is
/// read, so no peer can ever reach a record held under one.
/// </para>
/// <para>
/// That is what makes the identifier moving affordable. A derived identifier is written once and
/// never moves, which is the guarantee a kept <see cref="PairingState.Revoked"/> record rests on.
/// What moves is this handle, which was never on the wire and which nothing outside this server
/// has ever seen.
/// </para>
/// <para>
/// The bytes come from <see cref="RandomNumberGenerator"/> rather than from a counter or a
/// clock, so two windows opened in the same second against the same address are two records
/// rather than one. Nothing about the value is secret and nothing rests on it being unguessable:
/// it is unique so that two half-built pairings do not collide, and it is unreachable because of
/// its shape rather than because of its entropy.
/// </para>
/// </remarks>
public static class ProvisionalPairingId
{
    /// <summary>
    /// What every provisional identifier starts with.
    /// </summary>
    /// <remarks>
    /// The hyphen is the load-bearing character. It is outside the hex alphabet, so a value
    /// carrying it cannot pass <see cref="FieldShape"/>'s hex test whatever its length, and a
    /// reader looking at a store file can tell the two kinds apart without counting characters.
    /// </remarks>
    public const string Prefix = "offered-";

    /// <summary>
    /// Mints an identifier for a window that has just opened.
    /// </summary>
    /// <returns>The identifier.</returns>
    public static string Mint()
    {
        // Half a hex character per byte, so the count is halved rather than written twice.
        var bytes = RandomNumberGenerator.GetBytes(FieldShape.HexFieldLength / 2);

        return Prefix + Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Whether an identifier is a provisional one rather than a derived one.
    /// </summary>
    /// <param name="pairingId">The identifier.</param>
    /// <returns>True where it is provisional.</returns>
    /// <remarks>
    /// This answers the shape and never the store. An identifier that is provisional is one no
    /// peer could have sent; whether a record is held under it is a question for the store.
    /// </remarks>
    public static bool Is(string? pairingId) =>
        pairingId is not null && pairingId.StartsWith(Prefix, StringComparison.Ordinal);
}
