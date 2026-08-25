using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// Where a pairing's key material is kept, which is never the plugin configuration.
/// </summary>
/// <remarks>
/// The host writes a plugin's configuration object to disk as plaintext XML and serves it back
/// to the dashboard through its own configuration endpoint, so a key on that object is
/// readable by anything that can read one file and visible to anyone who can open the settings
/// page. That is what the host does with a configuration rather than a preference about it,
/// and it is why this is a separate thing with its own interface, its own file and its own
/// rules.
/// <para>
/// Every read takes the instant it is judged at, which is the answer taken on issue #30 on
/// 2026-08-24. A superseded key whose overlap has run out is not a key any more, and a store
/// that returned one because nobody had swept it would hand a caller something the rotation
/// already ended.
/// </para>
/// <para>
/// Nothing here returns a key to code that only wants to display something.
/// <see cref="Pairings"/> is the accessor for that, and it answers with identifiers alone.
/// </para>
/// </remarks>
public interface IPairingKeyStore
{
    /// <summary>
    /// The key a pairing signs with and accepts.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="at">The instant this is judged at.</param>
    /// <returns>The current key, or null where no pairing with that identifier is held.</returns>
    KeyMaterial? Live(string pairingId, DateTimeOffset at);

    /// <summary>
    /// Everything a pairing's key state is, including a superseded key while an overlap is
    /// open.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="at">The instant this is judged at.</param>
    /// <returns>
    /// The keys, with <see cref="PairingKeys.Superseded"/> null where no overlap is open or
    /// where the one that was open has run out by <paramref name="at"/>, or null where no
    /// pairing with that identifier is held.
    /// </returns>
    PairingKeys? Both(string pairingId, DateTimeOffset at);

    /// <summary>
    /// Puts a key in place for a pairing that has none.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="current">The key.</param>
    /// <exception cref="InvalidOperationException">A key is already held for that pairing.</exception>
    /// <remarks>
    /// Adding over an existing pairing is refused rather than overwriting, because the two
    /// things a caller could mean by it are enrolling a new pairing and rotating an existing
    /// one, and the second is <see cref="Replace"/>. Silently taking either is how a rotation
    /// loses the superseded key.
    /// </remarks>
    void Add(string pairingId, KeyMaterial current);

    /// <summary>
    /// Rotates a pairing onto a replacement key and opens the overlap the superseded one lives
    /// in.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <param name="replacement">The replacement key.</param>
    /// <param name="supersededStopsAt">When the key being replaced stops verifying.</param>
    /// <exception cref="InvalidOperationException">No key is held for that pairing.</exception>
    void Replace(string pairingId, KeyMaterial replacement, DateTimeOffset supersededStopsAt);

    /// <summary>
    /// Removes everything held for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <remarks>
    /// This is what a revocation destroys. Destroying what is not there is not an error, so a
    /// revocation that arrives twice does not have to ask first. The record of the pairing is
    /// a different thing and is kept on purpose, which is <see cref="Protocol.IPairingRecordStore"/>.
    /// </remarks>
    void Destroy(string pairingId);

    /// <summary>
    /// The identifiers of every pairing this store holds a key for.
    /// </summary>
    /// <returns>The identifiers, and nothing else.</returns>
    IReadOnlyList<string> Pairings();
}
