using System;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// A pairing the harness put into both stores, and the secret it put there.
/// </summary>
/// <remarks>
/// THE KEY IS HANDED BACK ON PURPOSE, and this is the one place the harness keeps a secret
/// alive rather than destroying it. An assertion that a surface does not carry key material
/// has to hold the key material to look for it, and a case searching for a value that was
/// never created passes by asserting the absence of nothing - which is the shape #13 names
/// and the reason it names it.
/// <para>
/// Absence is only ever asserted against the encodings somebody enumerated, so the two here
/// are a floor rather than a proof. <see cref="AsHex"/> and <see cref="AsBase64"/> are what
/// the corpus writes key bytes as; a secret escaping through a third encoding passes a case
/// built on these. That gap is real rather than an oversight, and it is the same one
/// <c>docs/logging.md</c> states about the test #13 asks for.
/// </para>
/// </remarks>
internal sealed class SeededPairing
{
    private readonly byte[] _key;

    /// <summary>
    /// Initializes a new instance of the <see cref="SeededPairing"/> class.
    /// </summary>
    /// <param name="pairingId">The identifier both sides hold the key under.</param>
    /// <param name="key">The key bytes both stores were given.</param>
    public SeededPairing(string pairingId, byte[] key)
    {
        PairingId = pairingId;
        _key = key;
    }

    /// <summary>
    /// Gets the identifier both sides hold the key under.
    /// </summary>
    /// <remarks>
    /// Not a secret. <c>docs/logging.md</c> permits a pairing identifier in a log and this
    /// surface reports one, so a case must not look for this among the values it expects to
    /// be absent.
    /// </remarks>
    public string PairingId { get; }

    /// <summary>
    /// Gets the key as lowercase hexadecimal.
    /// </summary>
    public string AsHex => Convert.ToHexString(_key).ToLowerInvariant();

    /// <summary>
    /// Gets the key as uppercase hexadecimal, because a leak writes whichever the nearest
    /// call produced and the two are different strings to a search.
    /// </summary>
    public string AsUpperHex => Convert.ToHexString(_key);

    /// <summary>
    /// Gets the key as base64.
    /// </summary>
    public string AsBase64 => Convert.ToBase64String(_key);
}
