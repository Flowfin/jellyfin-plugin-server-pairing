using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// What a write to the key store leaves behind when it goes wrong, and what a reader sees
/// while one is going right.
/// </summary>
/// <remarks>
/// The failure this is written against is the one that shows up once a month and is never
/// reproducible from the report: a rotation writing a new key while a request handler reads
/// the old one, on a server that is also doing something else. Every case here drives that
/// deliberately rather than waiting for it.
/// </remarks>
public sealed class AtomicKeyStoreWriteTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

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
    /// A failure between writing the temporary file and putting it in place leaves the store
    /// exactly as it was. This is the window the whole mechanism exists to close: a write over
    /// the file in place would leave a file that is neither the old contents nor the new ones,
    /// and for a key store that is a pairing whose key is half of one key and half of another.
    /// </summary>
    [Fact]
    public void AFailureBetweenTheTemporaryWriteAndTheMoveLeavesThePreviousStateIntact()
    {
        var file = FileInAFreshDirectory();
        var first = KeyMaterial.Fresh();

        new FilePairingKeyStore(file).Add(PairingId, first);

        var before = File.ReadAllText(file);

        var failing = new FilePairingKeyStore(file, (_, _) => throw new IOException("the disk went away"));

        Assert.Throws<IOException>(() => failing.Replace(PairingId, KeyMaterial.Fresh(), Noon.AddHours(1)));

        Assert.Equal(before, File.ReadAllText(file));

        var after = new FilePairingKeyStore(file).Both(PairingId, Noon)!;

        Assert.True(after.Current.SameAs(first));
        Assert.Null(after.Superseded);
    }

    /// <summary>
    /// The failure reaches the caller rather than being swallowed. A store that reported
    /// success on a write that did not happen would leave a rotation the peer believes in and
    /// this server has not made.
    /// </summary>
    [Fact]
    public void AWriteFailureReachesTheCallerRatherThanBeingSwallowed()
    {
        var file = FileInAFreshDirectory();
        var failing = new FilePairingKeyStore(file, (_, _) => throw new UnauthorizedAccessException("read only"));

        Assert.Throws<UnauthorizedAccessException>(() => failing.Add(PairingId, KeyMaterial.Fresh()));
        Assert.False(File.Exists(file));

        new FilePairingKeyStore(file).Add(PairingId, KeyMaterial.Fresh());

        Assert.Throws<UnauthorizedAccessException>(() => failing.Destroy(PairingId));
        Assert.Single(new FilePairingKeyStore(file).Pairings());
    }

    /// <summary>
    /// A failed write leaves no temporary file lying beside the store. One left behind is not
    /// read by anything, and the next write overwrites it, but a directory that collects them
    /// is a directory nobody can look at and tell whether something is wrong.
    /// </summary>
    [Fact]
    public void AFailedWriteLeavesNoTemporaryBehind()
    {
        var file = FileInAFreshDirectory();
        var failing = new FilePairingKeyStore(file, (_, _) => throw new IOException("the disk went away"));

        Assert.Throws<IOException>(() => failing.Add(PairingId, KeyMaterial.Fresh()));

        Assert.False(File.Exists(file + AtomicWrite.TemporarySuffix));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(file)!));
    }

    /// <summary>
    /// A temporary left behind by a process that died is not read as the store, and the next
    /// write puts the real file in place beside it rather than being confused by it.
    /// </summary>
    [Fact]
    public void ATemporaryLeftBehindIsNotReadAsTheStore()
    {
        var file = FileInAFreshDirectory();

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file + AtomicWrite.TemporarySuffix, "{\"not\":\"a store\"}");

        var store = new FilePairingKeyStore(file);

        Assert.Empty(store.Pairings());

        var key = KeyMaterial.Fresh();
        store.Add(PairingId, key);

        Assert.True(new FilePairingKeyStore(file).Live(PairingId, Noon)!.SameAs(key));
    }

    /// <summary>
    /// Readers running against a rotation see a consistent pair of keys every time, never one
    /// key from before a rotation beside one from after it, and never a file caught halfway
    /// through being written.
    /// </summary>
    /// <remarks>
    /// The legal answers are enumerated rather than described: after the nth rotation the
    /// current key is the nth key and the superseded one is the key before it, so a reader
    /// that finds a current key at index i and a superseded key anywhere but i - 1 has seen a
    /// state no sequence of rotations produces.
    /// </remarks>
    [Fact]
    public async Task ReadersRunningAgainstARotationNeverSeeAnInconsistentPair()
    {
        var file = FileInAFreshDirectory();
        var keys = Enumerable.Range(0, 24).Select(_ => KeyMaterial.Fresh()).ToArray();
        var store = new FilePairingKeyStore(file);

        store.Add(PairingId, keys[0]);

        using var rotationsDone = new CancellationTokenSource();

        var rotating = Task.Run(() =>
        {
            for (var i = 1; i < keys.Length; i++)
            {
                store.Replace(PairingId, keys[i], Noon.AddHours(1));
            }

            rotationsDone.Cancel();
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var seen = 0;

            while (!rotationsDone.IsCancellationRequested || seen == 0)
            {
                var both = store.Both(PairingId, Noon);

                Assert.NotNull(both);

                var current = IndexOf(keys, both!.Current);

                Assert.True(current >= 0, "a reader saw a current key no rotation ever wrote");

                if (current == 0)
                {
                    Assert.Null(both.Superseded);
                }
                else
                {
                    Assert.NotNull(both.Superseded);
                    Assert.Equal(current - 1, IndexOf(keys, both.Superseded!));
                }

                seen++;
            }

            return seen;
        })).ToArray();

        await rotating.ConfigureAwait(true);

        var counts = await Task.WhenAll(readers).ConfigureAwait(true);

        // The floor under the case: readers that ran zero times would pass every assertion
        // above by never making one.
        Assert.All(counts, count => Assert.True(count > 0));
        Assert.True(IndexOf(keys, store.Live(PairingId, Noon)!) == keys.Length - 1);
    }

    /// <summary>
    /// The same, over the in-memory store, so the guarantee is the interface's rather than the
    /// file implementation's.
    /// </summary>
    [Fact]
    public async Task TheInMemoryStoreIsSafeUnderTheSameConcurrency()
    {
        var keys = Enumerable.Range(0, 64).Select(_ => KeyMaterial.Fresh()).ToArray();
        var store = new InMemoryPairingKeyStore();

        store.Add(PairingId, keys[0]);

        using var rotationsDone = new CancellationTokenSource();

        var rotating = Task.Run(() =>
        {
            for (var i = 1; i < keys.Length; i++)
            {
                store.Replace(PairingId, keys[i], Noon.AddHours(1));
            }

            rotationsDone.Cancel();
        });

        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            var seen = 0;

            while (!rotationsDone.IsCancellationRequested || seen == 0)
            {
                var both = store.Both(PairingId, Noon)!;
                var current = IndexOf(keys, both.Current);

                Assert.True(current >= 0, "a reader saw a current key no rotation ever wrote");

                if (current > 0)
                {
                    Assert.Equal(current - 1, IndexOf(keys, both.Superseded!));
                }

                seen++;
            }

            return seen;
        })).ToArray();

        await rotating.ConfigureAwait(true);

        var counts = await Task.WhenAll(readers).ConfigureAwait(true);

        Assert.All(counts, count => Assert.True(count > 0));
    }

    /// <summary>
    /// Writers running against each other leave every pairing they wrote, rather than the last
    /// writer's view of the store overwriting the others.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritersDoNotOverwriteEachOther()
    {
        var file = FileInAFreshDirectory();
        var store = new FilePairingKeyStore(file);

        var identifiers = Enumerable.Range(0, 16)
            .Select(i => i.ToString("x2", System.Globalization.CultureInfo.InvariantCulture).PadLeft(32, '0'))
            .ToArray();

        await Task.WhenAll(identifiers.Select(id => Task.Run(() => store.Add(id, KeyMaterial.Fresh())))).ConfigureAwait(true);

        var held = new FilePairingKeyStore(file).Pairings();

        Assert.Equal(identifiers.Length, held.Count);
        Assert.All(identifiers, id => Assert.Contains(id, held));
    }

    /// <summary>
    /// The same over the in-memory store. Its collection is not one that tolerates a
    /// concurrent writer, so a lost entry or a torn walk is what an unserialised add produces
    /// there rather than a file overwriting another file.
    /// </summary>
    [Fact]
    public async Task ConcurrentWritersDoNotOverwriteEachOtherInMemoryEither()
    {
        var store = new InMemoryPairingKeyStore();

        var identifiers = Enumerable.Range(0, 256)
            .Select(i => i.ToString("x4", System.Globalization.CultureInfo.InvariantCulture).PadLeft(32, '0'))
            .ToArray();

        await Task.WhenAll(identifiers.Select(id => Task.Run(() => store.Add(id, KeyMaterial.Fresh())))).ConfigureAwait(true);

        var held = store.Pairings();

        Assert.Equal(identifiers.Length, held.Count);
        Assert.All(identifiers, id => Assert.Contains(id, held));
    }

    private static int IndexOf(KeyMaterial[] keys, KeyMaterial key)
    {
        for (var i = 0; i < keys.Length; i++)
        {
            if (keys[i].SameAs(key))
            {
                return i;
            }
        }

        return -1;
    }

    private string FileInAFreshDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return Path.Combine(directory, KeyStorePath.FileName);
    }
}
