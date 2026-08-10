namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Where a pairing's state is kept between one request and the next.
/// </summary>
/// <remarks>
/// The store behind this is the same one the key material uses, which is M4, and until that
/// exists the only implementation is the one the suite substitutes. That is the same seam
/// <see cref="IPairingKeySource"/> takes, and for the same reason: the state machine can be
/// built and proved against the specification before anything durable exists.
/// <para>
/// <see cref="Write"/> is all or nothing. A write that fails leaves the previous record
/// readable, so a transition interrupted between the write and its commit leaves the pairing
/// in the state it was in rather than in one nobody planned for. Making that true of a file on
/// disk is issue #34; what is required of an implementation is stated here so that a caller
/// may rely on it and a test may drive its failure.
/// </para>
/// </remarks>
public interface IPairingRecordStore
{
    /// <summary>
    /// The record held for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>The record, or null where no pairing with that identifier is held.</returns>
    PairingRecord? Read(string pairingId);

    /// <summary>
    /// Puts a record in place of whatever was held, durably, or leaves the previous one
    /// readable and throws.
    /// </summary>
    /// <param name="record">The record to write.</param>
    void Write(PairingRecord record);

    /// <summary>
    /// Removes the record held for a pairing, if there is one, durably, or leaves it readable
    /// and throws.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <remarks>
    /// This is what reaching <see cref="PairingState.Absent"/> means. That state is defined by
    /// there being no record, so a pairing that reaches it by a window expiring or by a second
    /// hello with a different key leaves nothing behind. <see cref="PairingState.Revoked"/> is
    /// the opposite case and keeps its record on purpose.
    /// </remarks>
    void Delete(string pairingId);
}
