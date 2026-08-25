using System;
using System.Security.Cryptography;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// A key, as its own type rather than as an array of bytes.
/// </summary>
/// <remarks>
/// The rules issue #32 exists for cannot be written against <c>byte[]</c>: an array prints its
/// type name into a log without complaint, serialises wherever a serialiser meets it, and is
/// indistinguishable from every other array in a reflection walk. A type can be refused by
/// name in all three places, which is why the store is built against this from the start
/// rather than being retrofitted onto arrays later.
/// <para>
/// What this type does NOT do is protect the bytes from the process that holds them. Managed
/// memory is copied by the garbage collector, so <see cref="Destroy"/> narrows the window
/// rather than closing it, and <c>docs/keystore.md</c> says so in those words rather than
/// implying otherwise.
/// </para>
/// </remarks>
public sealed class KeyMaterial
{
    /// <summary>
    /// The length of a key, which is the HKDF output length <c>docs/crypto.md</c> fixes and
    /// the length <see cref="KeyOverlap"/> already refuses anything else against.
    /// </summary>
    public const int Length = KeyOverlap.KeyLength;

    private readonly byte[] _bytes;

    private bool _destroyed;

    private KeyMaterial(byte[] bytes)
    {
        _bytes = bytes;
    }

    /// <summary>
    /// Gets a value indicating whether this key has been destroyed.
    /// </summary>
    public bool IsDestroyed => _destroyed;

    /// <summary>
    /// Gets the bytes, for the cryptography that consumes them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The key has been destroyed.</exception>
    /// <remarks>
    /// A span rather than an array, so a caller cannot keep the storage this type owns, and so
    /// a caller that only wants to display something has nothing here it can use. There is no
    /// accessor that hands out a copy, deliberately.
    /// </remarks>
    public ReadOnlySpan<byte> Span
        => _destroyed
            ? throw new InvalidOperationException("This key has been destroyed and its bytes are gone.")
            : _bytes;

    /// <summary>
    /// A key from bytes that already exist.
    /// </summary>
    /// <param name="bytes">The bytes, which must be exactly a key long.</param>
    /// <returns>The key.</returns>
    /// <exception cref="ArgumentException">The bytes are not the length of a key.</exception>
    public static KeyMaterial From(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length)
        {
            throw new ArgumentException(
                "Key material is exactly the length docs/crypto.md fixes, and something else is not a key.",
                nameof(bytes));
        }

        return new KeyMaterial(bytes.ToArray());
    }

    /// <summary>
    /// A fresh key from the random source <c>docs/crypto.md</c> pins.
    /// </summary>
    /// <returns>The key.</returns>
    public static KeyMaterial Fresh() => new KeyMaterial(RandomNumberGenerator.GetBytes(Length));

    /// <summary>
    /// Whether two keys are the same, compared in fixed time.
    /// </summary>
    /// <param name="other">The other key.</param>
    /// <returns>True where the bytes are the same.</returns>
    /// <remarks>
    /// Fixed time because <c>docs/crypto.md</c> makes it a rule on every value where being
    /// wrong in the first byte and being wrong in the last byte must cost the same. This type
    /// deliberately does not override equality: an operator somebody reaches for by habit is
    /// exactly the comparison that rule refuses.
    /// </remarks>
    public bool SameAs(KeyMaterial? other)
    {
        if (other is null || _destroyed || other._destroyed)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    }

    /// <summary>
    /// Overwrites the bytes and makes this key unusable.
    /// </summary>
    /// <remarks>
    /// Destroying twice is not an error, because a store that destroys a key on the way out
    /// and a caller that destroys the same key are both doing the right thing.
    /// </remarks>
    public void Destroy()
    {
        CryptographicOperations.ZeroMemory(_bytes);
        _destroyed = true;
    }

    /// <summary>
    /// What a careless interpolation into a log line or an exception message produces.
    /// </summary>
    /// <returns>A placeholder carrying none of the bytes.</returns>
    public override string ToString() => "[key material, not shown]";
}
