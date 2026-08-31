using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// What a revoked key stops being able to do, over the path a peer's request actually takes.
/// </summary>
/// <remarks>
/// Revocation destroys key material, which <see cref="IPairingKeyStore.Destroy"/> declares of
/// itself, and the property that matters afterwards is on the arriving side: a caller still
/// holding the destroyed key is refused, and refused in the shape every other stranger is
/// refused in. Every case here drives a real <see cref="FilePairingKeyStore"/> over a real
/// file through <see cref="StoreBackedKeys"/>, <see cref="RequestAuthenticator"/> and
/// <see cref="PeerPlane"/>, because what is under test is the join rather than any one of
/// them.
/// <para>
/// WHAT IS STIPULATED HERE IS THE REVOCATION ITSELF. No operation in this plugin composes the
/// destruction with the transition into <c>Revoked</c>: there is no record store
/// implementation for <see cref="PairingStateMachine"/> to write through, and nothing outbound
/// to attempt the courtesy notification with. So these cases destroy the key directly and
/// assert what a peer meets afterwards, which is the half of issue #24 the tree can carry.
/// </para>
/// <para>
/// Nothing here waits for real time. Every read on the store takes the instant it is judged
/// at, so a rotation overlap is reached by passing a later value.
/// </para>
/// </remarks>
public sealed class RevocationTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";
    private const string Nonce = "0123456789abcdef0123456789abcdef";
    private const string Version = "1";
    private const string Timestamp = "1786000000";

    private static readonly DateTimeOffset At = DateTimeOffset.FromUnixTimeSeconds(1786000000);

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
    /// The same request, the same signature and the same server, on either side of the key
    /// being destroyed. It is understood before and is not understood after, so the answer
    /// moves with the revocation rather than with anything the caller did.
    /// </summary>
    /// <remarks>
    /// This is the second done condition of issue #24. It is asserted on one plane rather than
    /// on two so that nothing here is explained by a second server having been built, and the
    /// request is kept byte for byte so that nothing is explained by a second signature.
    /// </remarks>
    [Fact]
    public void ARequestSignedWithTheRevokedKeyIsRefusedOnceThatKeyIsDestroyed()
    {
        var store = FreshStore();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.From(key));

        var plane = PlaneOver(store);
        var arriving = Signed(PairingMessage.Revoke, key);

        Assert.True(plane.Serve(PairingMessage.Revoke, arriving, At).BodyWasHandedOn);

        store.Destroy(PairingId);

        Assert.False(plane.Serve(PairingMessage.Revoke, arriving, At).BodyWasHandedOn);
    }

    /// <summary>
    /// Revocation takes effect on the file rather than at the next restart: the case above,
    /// re-read through a second store over the same file, answers the same way. A key removed
    /// in memory and left on disk would pass that case and lose the property on the next boot.
    /// </summary>
    [Fact]
    public void TheDestroyedKeyIsGoneFromTheFileAndNotOnlyFromTheStoreThatDestroyedIt()
    {
        var file = FreshFile();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        var first = new FilePairingKeyStore(file);

        first.Add(PairingId, KeyMaterial.From(key));
        first.Destroy(PairingId);

        var second = new FilePairingKeyStore(file);

        Assert.DoesNotContain(PairingId, second.Pairings());
        Assert.False(PlaneOver(second).Serve(PairingMessage.Revoke, Signed(PairingMessage.Revoke, key), At).BodyWasHandedOn);
    }

    /// <summary>
    /// A revocation that arrives without a signature that verifies is not acted on, and a
    /// signed one is. The two answers are separable here because the server holds a key: on a
    /// server holding none, every request is refused and a case asserting that an
    /// unauthenticated revocation was ignored cannot be told from one asserting that
    /// everything is.
    /// </summary>
    /// <remarks>
    /// This is the third done condition of issue #24, and what makes it an assertion rather
    /// than a restatement of <c>PeerPlaneTests</c> is the comparison inside one server: three
    /// unauthentic revocations and one authentic one against one store, so the refusals are
    /// about the signature rather than about a plane that refuses whatever arrives.
    /// </remarks>
    [Fact]
    public void AnUnauthenticatedRevocationIsIgnoredWhereASignedOneIsUnderstood()
    {
        var store = FreshStore();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.From(key));

        var plane = PlaneOver(store);
        var body = Encoding.ASCII.GetBytes("{\"reason\":\"revoked\"}");

        var unauthentic = new string?[]
        {
            null,
            "not base64 at all",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(RequestAuthenticator.SignatureLength)),
            RequestAuthenticator.Sign(
                Signable(PairingMessage.Revoke, PairingId, body),
                RandomNumberGenerator.GetBytes(KeyMaterial.Length)),
        };

        foreach (var signature in unauthentic)
        {
            var outcome = plane.Serve(PairingMessage.Revoke, Arriving(PairingMessage.Revoke, PairingId, signature, body), At);

            Assert.Equal(RefusalCode.Refused, outcome.Code);
            Assert.False(outcome.BodyWasHandedOn);
            Assert.True(outcome.VerifiedBody.IsEmpty);
        }

        var authentic = plane.Serve(PairingMessage.Revoke, Signed(PairingMessage.Revoke, key, body), At);

        Assert.True(authentic.BodyWasHandedOn);
        Assert.Equal(body, authentic.VerifiedBody.ToArray());
    }

    /// <summary>
    /// A pairing revoked mid-rotation takes both of its keys with it. A peer still signing
    /// under the key a rotation replaced verifies until the overlap ends, so a revocation
    /// destroying the current key alone would leave that peer able to reach this server for
    /// the rest of the overlap, which is the window nobody reaches by hand.
    /// </summary>
    /// <param name="secondsFromTheEnd">Where the request after the revocation is judged.</param>
    [Theory]
    [InlineData(-3600)]
    [InlineData(-1)]
    public void RevokingDuringARotationDestroysTheSupersededKeyAsWell(int secondsFromTheEnd)
    {
        var store = FreshStore();
        var superseded = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var replacement = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var endsAt = At.AddSeconds(7200);
        var judgedAt = endsAt.AddSeconds(secondsFromTheEnd);

        store.Add(PairingId, KeyMaterial.From(superseded));
        store.Replace(PairingId, KeyMaterial.From(replacement), endsAt);

        var plane = PlaneOver(store);

        // Stamped at the instant it is judged at, which is what a peer whose clock agrees with
        // this server sends. The rotation this case is about ends two hours out, so a request
        // still carrying the fixed timestamp every other case uses would be refused for the
        // clock long before the key it is signed with was reached.
        var stamp = StampAt(judgedAt);
        var underTheOldKey = Signed(PairingMessage.Revoke, superseded, carries: FreshNonce(), stamp: stamp);

        Assert.True(plane.Serve(PairingMessage.Revoke, underTheOldKey, judgedAt).BodyWasHandedOn);

        store.Destroy(PairingId);

        Assert.False(plane.Serve(PairingMessage.Revoke, underTheOldKey, judgedAt).BodyWasHandedOn);
        Assert.False(plane
            .Serve(
                PairingMessage.Revoke,
                Signed(PairingMessage.Revoke, replacement, carries: FreshNonce(), stamp: stamp),
                judgedAt)
            .BodyWasHandedOn);
    }

    /// <summary>
    /// Revocation is unilateral about one relationship and reaches no other. A second pairing
    /// this server holds a key for goes on verifying across the first one being revoked.
    /// </summary>
    [Fact]
    public void RevokingOnePairingLeavesEveryOtherPairingVerifying()
    {
        var store = FreshStore();
        var revoked = RandomNumberGenerator.GetBytes(KeyMaterial.Length);
        var kept = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.From(revoked));
        store.Add(AnotherPairing, KeyMaterial.From(kept));

        var plane = PlaneOver(store);

        // Two requests rather than one sent twice. A peer sending a second request carries a
        // fresh nonce, and re-serving the same bytes would be a replay, which is refused for a
        // reason that has nothing to do with the pairing this case is about.
        Assert.True(plane.Serve(PairingMessage.Exchange, FromTheOther(kept), At).BodyWasHandedOn);

        store.Destroy(PairingId);

        Assert.True(plane.Serve(PairingMessage.Exchange, FromTheOther(kept), At).BodyWasHandedOn);
        Assert.Equal(new[] { AnotherPairing }, store.Pairings());
    }

    /// <summary>
    /// A caller holding the revoked key is answered exactly as a caller naming a pairing this
    /// server has never held a key for. A revocation answered differently would tell a
    /// stranger which identifiers had once been paired here, which is the thing an
    /// undistinguished refusal exists to withhold.
    /// </summary>
    [Fact]
    public void ARevokedPairingIsAnsweredAsOneThisServerNeverHeldAKeyFor()
    {
        var store = FreshStore();
        var key = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        store.Add(PairingId, KeyMaterial.From(key));

        var plane = PlaneOver(store);

        store.Destroy(PairingId);

        var revoked = plane.Serve(PairingMessage.Revoke, Signed(PairingMessage.Revoke, key), At);
        var neverHeld = plane.Serve(
            PairingMessage.Revoke,
            Arriving(
                PairingMessage.Revoke,
                AnotherPairing,
                RequestAuthenticator.Sign(Signable(PairingMessage.Revoke, AnotherPairing, Array.Empty<byte>()), key),
                Array.Empty<byte>()),
            At);

        Assert.Equal(neverHeld.Code, revoked.Code);
        Assert.Equal(neverHeld.BodyWasHandedOn, revoked.BodyWasHandedOn);
        Assert.Equal(neverHeld.VerifiedBody.ToArray(), revoked.VerifiedBody.ToArray());
    }

    /// <summary>
    /// The request a case signs, in the form the signature is computed over.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="pairingId">The identifier the request claims.</param>
    /// <param name="body">The body.</param>
    /// <param name="carries">The nonce it carries.</param>
    /// <param name="stamp">The timestamp it carries.</param>
    /// <returns>The request.</returns>
    private static PairingRequest Signable(
        PairingMessage message,
        string pairingId,
        byte[] body,
        string? carries = null,
        string stamp = Timestamp)
        => new PairingRequest(
            PeerPlane.Method,
            PeerPlane.PathFor(message),
            pairingId,
            Version,
            stamp,
            carries ?? Nonce,
            body);

    /// <summary>
    /// A request as it arrives on the plane, carrying whatever signature a case chose.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="pairingId">The identifier the request claims.</param>
    /// <param name="signature">The signature presented, which may be nothing at all.</param>
    /// <param name="body">The body.</param>
    /// <param name="carries">The nonce it carries.</param>
    /// <param name="stamp">The timestamp it carries.</param>
    /// <returns>The arriving request.</returns>
    private static ArrivingRequest Arriving(
        PairingMessage message,
        string pairingId,
        string? signature,
        byte[] body,
        string? carries = null,
        string stamp = Timestamp)
        => new ArrivingRequest(
            PeerPlane.PathFor(message),
            PeerPlane.Method,
            pairingId,
            Version,
            stamp,
            carries ?? Nonce,
            signature,
            body,
            false);

    /// <summary>
    /// A request signed under a key, which is what a peer holding that key sends.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="key">The key the peer signs with.</param>
    /// <param name="body">The body, empty where a case does not care about one.</param>
    /// <returns>The arriving request.</returns>
    private static ArrivingRequest Signed(
        PairingMessage message,
        byte[] key,
        byte[]? body = null,
        string? carries = null,
        string stamp = Timestamp)
    {
        var bytes = body ?? Array.Empty<byte>();

        return Arriving(
            message,
            PairingId,
            RequestAuthenticator.Sign(Signable(message, PairingId, bytes, carries, stamp), key),
            bytes,
            carries,
            stamp);
    }

    /// <summary>
    /// A nonce no other request in a case carries, of the shape the specification fixes.
    /// </summary>
    /// <returns>The nonce.</returns>
    private static string FreshNonce() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(FieldShape.HexFieldLength / 2)).ToLowerInvariant();

    /// <summary>
    /// The timestamp a peer whose clock agrees with this server's puts on a request judged at
    /// this instant.
    /// </summary>
    /// <param name="at">The instant the request is judged at.</param>
    /// <returns>The timestamp, as it is spelled on the wire.</returns>
    private static string StampAt(DateTimeOffset at) =>
        at.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// A request from the second pairing, signed under its own key and carrying a nonce no
    /// other request in the case carries.
    /// </summary>
    /// <param name="key">The key that pairing holds.</param>
    /// <returns>The arriving request.</returns>
    private static ArrivingRequest FromTheOther(byte[] key)
    {
        var carries = FreshNonce();

        return Arriving(
            PairingMessage.Exchange,
            AnotherPairing,
            RequestAuthenticator.Sign(
                Signable(PairingMessage.Exchange, AnotherPairing, Array.Empty<byte>(), carries),
                key),
            Array.Empty<byte>(),
            carries);
    }

    /// <summary>
    /// The plane a server runs, over the store it was given.
    /// </summary>
    /// <param name="store">The key store.</param>
    /// <returns>The plane.</returns>
    private static PeerPlane PlaneOver(IPairingKeyStore store)
        => new PeerPlane(new RequestAuthenticator(new StoreBackedKeys(store)), new ArrivalLimit(), new FreshnessWindow());

    /// <summary>
    /// A path to a store file in a directory of its own, removed when the class is disposed.
    /// </summary>
    /// <returns>The path.</returns>
    private string FreshFile()
    {
        var directory = Path.Join(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return Path.Join(directory, KeyStorePath.FileName);
    }

    /// <summary>
    /// A file store over a file of its own.
    /// </summary>
    /// <returns>The store.</returns>
    private FilePairingKeyStore FreshStore() => new FilePairingKeyStore(FreshFile());
}
