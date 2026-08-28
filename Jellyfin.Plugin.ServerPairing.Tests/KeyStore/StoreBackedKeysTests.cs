using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// The join between the key store and the request path: a pairing that has a key can be
/// verified, and one that does not is refused the same way a bad signature is.
/// </summary>
/// <remarks>
/// Every case here puts its key in a real <see cref="FilePairingKeyStore"/> over a real file
/// first, rather than in a double. What is under test is the join, so a double standing in for
/// the store would be a case asserting that the join works against the thing it was written
/// beside.
/// <para>
/// Nothing here waits for real time. The instant is an argument on every read, so an overlap
/// that has run out is reached by passing a later value.
/// </para>
/// </remarks>
public sealed class StoreBackedKeysTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";
    private const string Nonce = "0123456789abcdef0123456789abcdef";

    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A request signed with the key a pairing holds verifies. This is the whole of what was
    /// missing: the store held the key, the request path asked a source that answered nothing,
    /// and the server refused a caller it could have authenticated.
    /// </summary>
    [Fact]
    public void ARequestSignedWithThePairingsCurrentKeyVerifies()
    {
        var store = FreshStore();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.From(key));

        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();

        Assert.Equal(
            VerificationOutcome.Verified,
            receiver.Verify(request, RequestAuthenticator.Sign(request, key), Noon));
    }

    /// <summary>
    /// The same request against the same store, before the key is in it and after. The
    /// assertion above is an assertion about the store only if the answer moves when the store
    /// does, and one receiver reads both, so nothing here is explained by two receivers.
    /// </summary>
    [Fact]
    public void TheAnswerMovesWithTheStoreRatherThanWithTheReceiver()
    {
        var store = FreshStore();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();
        var signature = RequestAuthenticator.Sign(request, key);

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(request, signature, Noon));

        store.Add(PairingId, KeyMaterial.From(key));

        Assert.Equal(VerificationOutcome.Verified, receiver.Verify(request, signature, Noon));
    }

    /// <summary>
    /// A peer still signing under the key a rotation replaced is understood until the overlap
    /// ends, and not after it. This is the case a join built on the store's current key alone
    /// gets wrong, and it gets it wrong only inside the overlap, which is the window nobody
    /// reaches by hand.
    /// </summary>
    /// <param name="secondsFromTheEnd">
    /// Where the request is judged, relative to the instant the superseded key stops.
    /// </param>
    /// <param name="expected">What the receiver answers there.</param>
    [Theory]
    [InlineData(-3600, VerificationOutcome.Verified)]
    [InlineData(-1, VerificationOutcome.Verified)]
    [InlineData(0, VerificationOutcome.Refused)]
    [InlineData(1, VerificationOutcome.Refused)]
    public void TheSupersededKeyVerifiesInsideTheOverlapAndNotAtItsEnd(
        int secondsFromTheEnd,
        VerificationOutcome expected)
    {
        var store = FreshStore();
        var superseded = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var replacement = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var endsAt = Noon.AddSeconds(7200);

        store.Add(PairingId, KeyMaterial.From(superseded));
        store.Replace(PairingId, KeyMaterial.From(replacement), endsAt);

        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();

        Assert.Equal(
            expected,
            receiver.Verify(
                request,
                RequestAuthenticator.Sign(request, superseded),
                endsAt.AddSeconds(secondsFromTheEnd)));
    }

    /// <summary>
    /// The replacement verifies on both sides of the same boundary, so the case above is about
    /// the superseded key running out rather than about the pairing having stopped verifying
    /// anything.
    /// </summary>
    /// <param name="secondsFromTheEnd">Where the request is judged.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(86400)]
    public void TheReplacementVerifiesOnBothSidesOfThatBoundary(int secondsFromTheEnd)
    {
        var store = FreshStore();
        var endsAt = Noon.AddSeconds(7200);
        var replacement = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Replace(PairingId, KeyMaterial.From(replacement), endsAt);

        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();

        Assert.Equal(
            VerificationOutcome.Verified,
            receiver.Verify(
                request,
                RequestAuthenticator.Sign(request, replacement),
                endsAt.AddSeconds(secondsFromTheEnd)));
    }

    /// <summary>
    /// A request naming a pairing this store does not hold is refused with the same outcome as
    /// one naming a pairing it does hold and signing it badly. The two are one value, so no
    /// caller can separate them by reading an answer.
    /// </summary>
    /// <remarks>
    /// This is the assertion <c>RequestAuthenticationTests.AnUnknownPairingAndABadSignatureAreOneOutcome</c>
    /// makes over a source written for the suite, made again over the source a server runs on.
    /// It is the property most easily lost when a source starts reading a store, because the
    /// obvious implementation returns early on a miss.
    /// </remarks>
    [Fact]
    public void AnUnknownPairingAndABadSignatureAreOneOutcome()
    {
        var store = FreshStore();

        store.Add(AnotherPairing, KeyMaterial.Fresh());

        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();
        var somebodyElsesSignature = RequestAuthenticator.Sign(
            request,
            RandomNumberGenerator.GetBytes(KeyMaterial.Length));

        Assert.Equal(VerificationOutcome.Refused, receiver.Verify(request, somebodyElsesSignature, Noon));

        var known = request.With(pairingId: AnotherPairing);

        Assert.Equal(
            VerificationOutcome.Refused,
            receiver.Verify(known, RequestAuthenticator.Sign(known, RandomNumberGenerator.GetBytes(KeyMaterial.Length)), Noon));
    }

    /// <summary>
    /// A pairing this store does not hold is judged against a key nobody outside this process
    /// has, so a request signed under the empty key is refused rather than accepted.
    /// </summary>
    /// <remarks>
    /// The case above holds whether an absent pairing is answered with a drawn key or with an
    /// empty one. This one does not: an empty key makes the tag a value any caller can compute
    /// for a request of their choosing, so an absent pairing answered that way would be one a
    /// stranger can satisfy.
    /// </remarks>
    [Fact]
    public void AnAbsentPairingIsNotVerifiableUnderTheEmptyKey()
    {
        var store = FreshStore();

        store.Add(AnotherPairing, KeyMaterial.Fresh());

        var receiver = new RequestAuthenticator(new StoreBackedKeys(store));
        var request = Exchange();

        Assert.Equal(
            VerificationOutcome.Refused,
            receiver.Verify(request, RequestAuthenticator.Sign(request, ReadOnlySpan<byte>.Empty), Noon));
    }

    /// <summary>
    /// A pairing the store does not hold answers with neither key, which is the shape the
    /// receiver's stand-ins are chosen against. Asserted on the source rather than through a
    /// verification, because a refusal reaches the same value by several routes.
    /// </summary>
    [Fact]
    public void APairingTheStoreDoesNotHoldAnswersWithNeitherKey()
    {
        var source = new StoreBackedKeys(FreshStore());

        var answered = source.ArrivingKeys(PairingId, Noon);

        Assert.True(answered.Current.IsEmpty);
        Assert.True(answered.Superseded.IsEmpty);
    }

    /// <summary>
    /// A pairing that is not mid-rotation answers with one key and an empty second slot, so
    /// the overlap cases above are about a rotation rather than about the second slot being
    /// filled at all times.
    /// </summary>
    [Fact]
    public void APairingThatIsNotRotatingAnswersWithOneKey()
    {
        var store = FreshStore();

        store.Add(PairingId, KeyMaterial.Fresh());

        var answered = new StoreBackedKeys(store).ArrivingKeys(PairingId, Noon);

        Assert.False(answered.Current.IsEmpty);
        Assert.True(answered.Superseded.IsEmpty);
    }

    /// <summary>
    /// A request carrying every covered field, which is what the cases above sign and verify.
    /// </summary>
    /// <returns>The request.</returns>
    private static PairingRequest Exchange()
        => new PairingRequest(
            "POST",
            "/ServerPairing/exchange",
            PairingId,
            "1",
            "1786000000",
            Nonce,
            Encoding.UTF8.GetBytes("{\"users\":1}"));

    /// <summary>
    /// A file store over a file in a directory of its own, removed when the class is disposed.
    /// </summary>
    /// <returns>The store.</returns>
    private FilePairingKeyStore FreshStore()
    {
        var directory = Path.Combine(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return new FilePairingKeyStore(Path.Combine(directory, KeyStorePath.FileName));
    }
}
