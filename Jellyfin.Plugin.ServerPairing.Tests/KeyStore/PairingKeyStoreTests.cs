using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// What the key store means, asserted against both implementations rather than against one.
/// </summary>
/// <remarks>
/// Every case runs twice, once over the in-memory store the suite drives and once over the
/// file store a server runs on. A rule that holds on one and not the other is a difference
/// between them, and it shows up here as a failing case rather than on somebody's server.
/// <para>
/// Nothing here waits for real time. Every read takes the instant it is judged at, which is
/// the answer taken on issue #30 on 2026-08-24, so an overlap that has run out is reached by
/// passing a later instant.
/// </para>
/// </remarks>
public sealed class PairingKeyStoreTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";

    private static readonly DateTimeOffset Noon = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <summary>
    /// The two implementations, named so a failure says which one it was.
    /// </summary>
    /// <returns>The names.</returns>
    public static TheoryData<string> BothStores() => new TheoryData<string> { "memory", "file" };

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(candidate => Directory.Exists(candidate)))
        {
            Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A key that was added is the key that comes back.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void AKeyThatWasAddedIsTheKeyThatComesBack(string which)
    {
        var store = Store(which);
        var key = KeyMaterial.Fresh();

        store.Add(PairingId, key);

        Assert.True(store.Live(PairingId, Noon)!.SameAs(key));
    }

    /// <summary>
    /// A pairing nobody added is answered with nothing rather than with an empty key, so a
    /// caller cannot mistake an unknown pairing for one holding a key of zeros.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void APairingNobodyAddedIsAnsweredWithNothing(string which)
    {
        var store = Store(which);

        Assert.Null(store.Live(PairingId, Noon));
        Assert.Null(store.Both(PairingId, Noon));
        Assert.Empty(store.Pairings());
    }

    /// <summary>
    /// Adding over a pairing that already holds a key is refused. The two things a caller
    /// could mean by it are enrolling and rotating, and taking either silently is how a
    /// rotation loses the key it was supposed to supersede.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void AddingOverAnExistingPairingIsRefused(string which)
    {
        var store = Store(which);
        var first = KeyMaterial.Fresh();

        store.Add(PairingId, first);

        Assert.Throws<InvalidOperationException>(() => store.Add(PairingId, KeyMaterial.Fresh()));
        Assert.True(store.Live(PairingId, Noon)!.SameAs(first));
    }

    /// <summary>
    /// Rotating a pairing that holds nothing is refused, because there is nothing for a
    /// replacement to supersede and quietly adding it would enrol a pairing nobody enrolled.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void RotatingAPairingThatHoldsNothingIsRefused(string which)
    {
        var store = Store(which);

        Assert.Throws<InvalidOperationException>(
            () => store.Replace(PairingId, KeyMaterial.Fresh(), Noon.AddHours(1)));

        Assert.Empty(store.Pairings());
    }

    /// <summary>
    /// A rotation leaves both keys readable while the overlap is open: the replacement is
    /// what the pairing is on, and the key it replaced is still there to verify a peer that
    /// has not caught up.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void ARotationLeavesBothKeysReadableWhileTheOverlapIsOpen(string which)
    {
        var store = Store(which);
        var first = KeyMaterial.Fresh();
        var replacement = KeyMaterial.Fresh();
        var stopsAt = Noon.AddHours(1);

        store.Add(PairingId, first);
        store.Replace(PairingId, replacement, stopsAt);

        var both = store.Both(PairingId, Noon)!;

        Assert.True(both.Current.SameAs(replacement));
        Assert.NotNull(both.Superseded);
        Assert.True(both.Superseded!.SameAs(first));
        Assert.Equal(stopsAt, both.SupersededStopsAt);
        Assert.True(store.Live(PairingId, Noon)!.SameAs(replacement));
    }

    /// <summary>
    /// The superseded key is gone at the instant it stops and not before. The two cases sit
    /// either side of that instant and the margin is nothing, which is the boundary an
    /// off-by-one would move.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void TheSupersededKeyIsGoneAtTheInstantItStops(string which)
    {
        var store = Store(which);
        var first = KeyMaterial.Fresh();
        var replacement = KeyMaterial.Fresh();
        var stopsAt = Noon.AddHours(1);

        store.Add(PairingId, first);
        store.Replace(PairingId, replacement, stopsAt);

        Assert.NotNull(store.Both(PairingId, stopsAt.AddSeconds(-1))!.Superseded);
        Assert.Null(store.Both(PairingId, stopsAt)!.Superseded);
        Assert.Null(store.Both(PairingId, stopsAt.AddSeconds(1))!.Superseded);

        // The pairing itself is untouched by the overlap ending.
        Assert.True(store.Live(PairingId, stopsAt.AddYears(1))!.SameAs(replacement));
    }

    /// <summary>
    /// Destroying a pairing takes everything it held, including a superseded key that was
    /// still inside its overlap. A revocation that left one of the two behind would leave a
    /// key that still verifies for a pairing that is over.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void DestroyingAPairingTakesBothOfItsKeys(string which)
    {
        var store = Store(which);

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Replace(PairingId, KeyMaterial.Fresh(), Noon.AddHours(1));
        store.Destroy(PairingId);

        Assert.Null(store.Live(PairingId, Noon));
        Assert.Null(store.Both(PairingId, Noon));
        Assert.DoesNotContain(PairingId, store.Pairings());
    }

    /// <summary>
    /// Destroying what is not there is not an error, so a revocation that arrives twice does
    /// not have to ask first, and neither does one racing an operator doing it by hand.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void DestroyingWhatIsNotThereIsNotAnError(string which)
    {
        var store = Store(which);

        store.Destroy(PairingId);
        store.Add(PairingId, KeyMaterial.Fresh());
        store.Destroy(PairingId);
        store.Destroy(PairingId);

        Assert.Empty(store.Pairings());
    }

    /// <summary>
    /// Destroying one pairing leaves the others alone. This is the case that fails if a store
    /// ever writes the whole file from one pairing's view of it.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void DestroyingOnePairingLeavesTheOthersAlone(string which)
    {
        var store = Store(which);
        var kept = KeyMaterial.Fresh();

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Add(AnotherPairing, kept);
        store.Destroy(PairingId);

        Assert.Equal(new[] { AnotherPairing }, store.Pairings());
        Assert.True(store.Live(AnotherPairing, Noon)!.SameAs(kept));
    }

    /// <summary>
    /// The enumeration answers with identifiers and nothing else, which is what makes it the
    /// accessor a dashboard may use. Nothing on what it returns can reach a key.
    /// </summary>
    /// <param name="which">Which implementation.</param>
    [Theory]
    [MemberData(nameof(BothStores))]
    public void TheEnumerationCarriesIdentifiersAndNothingElse(string which)
    {
        var store = Store(which);

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Add(AnotherPairing, KeyMaterial.Fresh());

        var listed = store.Pairings();

        Assert.Equal(2, listed.Count);
        Assert.Contains(PairingId, listed);
        Assert.Contains(AnotherPairing, listed);
        Assert.All(listed, entry => Assert.IsType<string>(entry));
    }

    /// <summary>
    /// The file store answers the same after a restart, including inside an open overlap. A
    /// store that persisted only the current key would drop the superseded one here, and the
    /// peer that had not caught up would stop being understood, which is exactly the failure
    /// the overlap exists to prevent.
    /// </summary>
    [Fact]
    public void TheFileStoreAnswersTheSameAfterARestart()
    {
        var file = FileInAFreshDirectory();
        var first = KeyMaterial.Fresh();
        var replacement = KeyMaterial.Fresh();
        var stopsAt = Noon.AddHours(1);

        var before = new FilePairingKeyStore(file);
        before.Add(PairingId, first);
        before.Replace(PairingId, replacement, stopsAt);

        var after = new FilePairingKeyStore(file);
        var both = after.Both(PairingId, Noon)!;

        Assert.True(both.Current.SameAs(replacement));
        Assert.True(both.Superseded!.SameAs(first));
        Assert.Equal(stopsAt, both.SupersededStopsAt);
        Assert.Equal(new[] { PairingId }, after.Pairings());
    }

    /// <summary>
    /// The file store writes nothing until it is asked to hold something, and a store whose
    /// file is not there yet reads as empty rather than throwing.
    /// </summary>
    [Fact]
    public void TheFileStoreReadsAsEmptyBeforeItHasWrittenAnything()
    {
        var file = FileInAFreshDirectory();
        var store = new FilePairingKeyStore(file);

        Assert.Empty(store.Pairings());
        Assert.Null(store.Live(PairingId, Noon));
        Assert.False(System.IO.File.Exists(file));
    }

    /// <summary>
    /// A destroyed pairing is gone from the file rather than only from a copy in memory, so a
    /// revocation survives the restart that follows it.
    /// </summary>
    [Fact]
    public void ADestroyedPairingIsGoneFromTheFile()
    {
        var file = FileInAFreshDirectory();

        var before = new FilePairingKeyStore(file);
        before.Add(PairingId, KeyMaterial.Fresh());
        before.Destroy(PairingId);

        Assert.Empty(new FilePairingKeyStore(file).Pairings());
    }

    private IPairingKeyStore Store(string which) => which switch
    {
        "memory" => new InMemoryPairingKeyStore(),
        "file" => new FilePairingKeyStore(FileInAFreshDirectory()),
        _ => throw new ArgumentOutOfRangeException(nameof(which)),
    };

    private string FileInAFreshDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return Path.Combine(directory, KeyStorePath.FileName);
    }
}
