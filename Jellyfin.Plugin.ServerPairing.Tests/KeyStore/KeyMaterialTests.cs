using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// The type key material travels in.
/// </summary>
/// <remarks>
/// The length and the comparison are read out of <c>docs/crypto.md</c>: 32 bytes from the HKDF
/// output length, and fixed-time comparison on every value where being wrong in the first byte
/// and being wrong in the last must cost the same.
/// </remarks>
public class KeyMaterialTests
{
    /// <summary>
    /// A key is exactly the length the key derivation produces, and the length is the one
    /// <see cref="KeyOverlap"/> already refuses anything else against, rather than a second
    /// number that could drift from it.
    /// </summary>
    [Fact]
    public void AKeyIsTheLengthTheKeyDerivationProduces()
    {
        Assert.Equal(32, KeyMaterial.Length);
        Assert.Equal(KeyOverlap.KeyLength, KeyMaterial.Length);
        Assert.Equal(KeyMaterial.Length, KeyMaterial.Fresh().Span.Length);
    }

    /// <summary>
    /// Bytes that are not the length of a key are refused rather than padded or truncated,
    /// one byte either side of the boundary and at zero.
    /// </summary>
    [Fact]
    public void SomethingThatIsNotTheLengthOfAKeyIsRefused()
    {
        foreach (var length in new[] { 0, 1, KeyMaterial.Length - 1, KeyMaterial.Length + 1, 64 })
        {
            var bytes = new byte[length];

            Assert.Throws<ArgumentException>(() => KeyMaterial.From(bytes));
        }

        Assert.Equal(KeyMaterial.Length, KeyMaterial.From(new byte[KeyMaterial.Length]).Span.Length);
    }

    /// <summary>
    /// Two fresh keys differ. A source that answered with the same bytes twice would make
    /// every pairing on a server share one key, and nothing else here would notice.
    /// </summary>
    [Fact]
    public void TwoFreshKeysAreNotTheSameKey()
    {
        Assert.False(KeyMaterial.Fresh().SameAs(KeyMaterial.Fresh()));
    }

    /// <summary>
    /// The bytes survive the round trip, compared through the type's own comparison rather
    /// than by reading them back out.
    /// </summary>
    [Fact]
    public void TheBytesSurviveTheRoundTrip()
    {
        var bytes = new byte[KeyMaterial.Length];

        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i + 1);
        }

        Assert.True(KeyMaterial.From(bytes).SameAs(KeyMaterial.From(bytes)));
    }

    /// <summary>
    /// What a careless interpolation produces carries none of the key, in any of the
    /// encodings somebody would recognise it in: the bytes as text, as hex, and as base64.
    /// This is the case that fails if the type ever gets a default string conversion back.
    /// </summary>
    [Fact]
    public void TheStringConversionCarriesNoneOfTheBytes()
    {
        var bytes = Enumerable.Range(0, KeyMaterial.Length).Select(i => (byte)(i + 1)).ToArray();
        var key = KeyMaterial.From(bytes);

        var printed = string.Create(CultureInfo.InvariantCulture, $"a log line: {key}");

        Assert.DoesNotContain(Convert.ToBase64String(bytes), printed, StringComparison.Ordinal);
        Assert.DoesNotContain(Convert.ToHexString(bytes), printed, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Encoding.Latin1.GetString(bytes), printed, StringComparison.Ordinal);

        foreach (var b in bytes)
        {
            Assert.DoesNotContain(b.ToString("x2", CultureInfo.InvariantCulture), printed, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A destroyed key has nothing left to hand out, and asking for it is an error rather
    /// than an empty answer, so a caller that kept one past its destruction finds out.
    /// </summary>
    [Fact]
    public void ADestroyedKeyHandsOutNothing()
    {
        var key = KeyMaterial.Fresh();

        Assert.False(key.IsDestroyed);

        key.Destroy();

        Assert.True(key.IsDestroyed);
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = key.Span.Length;
        });
    }

    /// <summary>
    /// Destroying twice is not an error, because a store that destroys a key on the way out
    /// and a caller that destroys the same key are both doing the right thing.
    /// </summary>
    [Fact]
    public void DestroyingTwiceIsNotAnError()
    {
        var key = KeyMaterial.Fresh();

        key.Destroy();
        key.Destroy();

        Assert.True(key.IsDestroyed);
    }

    /// <summary>
    /// A destroyed key is not the same as anything, including as itself. The bytes are gone,
    /// so the only answer that is not a lie is no.
    /// </summary>
    [Fact]
    public void ADestroyedKeyIsNotTheSameAsAnything()
    {
        var bytes = new byte[KeyMaterial.Length];
        var destroyed = KeyMaterial.From(bytes);
        var alive = KeyMaterial.From(bytes);

        destroyed.Destroy();

        Assert.False(destroyed.SameAs(alive));
        Assert.False(alive.SameAs(destroyed));
        Assert.False(destroyed.SameAs(destroyed));
        Assert.False(alive.SameAs(null));
    }

    /// <summary>
    /// The type does not carry equality of its own, so the comparison a reader reaches for by
    /// habit is not the one this type answers. That is deliberate: an inherited reference
    /// comparison says two keys with the same bytes are different, which is wrong in the safe
    /// direction, and an overridden one would be the byte comparison the cryptography document
    /// refuses.
    /// </summary>
    [Fact]
    public void TheTypeDeclaresNoEqualityOfItsOwn()
    {
        var declared = typeof(KeyMaterial).GetMethods(
            System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.DoesNotContain(declared, method => string.Equals(method.Name, "Equals", StringComparison.Ordinal));
        Assert.DoesNotContain(declared, method => string.Equals(method.Name, "GetHashCode", StringComparison.Ordinal));

        var bytes = new byte[KeyMaterial.Length];

        Assert.False(KeyMaterial.From(bytes).Equals(KeyMaterial.From(bytes)));
        Assert.True(KeyMaterial.From(bytes).SameAs(KeyMaterial.From(bytes)));
    }
}
