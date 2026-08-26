using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// What the plugin does with the store at the two moments it is not answering anybody:
/// starting and stopping.
/// </summary>
/// <remarks>
/// The store outlives an uninstall, so a reinstall comes up paired with whatever it was paired
/// with before, and <c>docs/lifecycle.md</c> argues both halves of that. These cases are the
/// half that says so, and the half that says a normal shutdown takes nothing away.
/// <para>
/// Nothing here starts a server. The two methods of the hosted service are called directly,
/// which is what the host does to it, and no display, container or elevation is involved.
/// </para>
/// </remarks>
public sealed class StoreAtStartupTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";

    private readonly List<string> _directories = new List<string>();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// A plugin starting against a store that already holds pairings says so, once per
    /// pairing, naming each one. This is the surprise <c>docs/lifecycle.md</c> describes:
    /// an operator who uninstalled to clear a problem and reinstalled gets the same pairings
    /// back, and nothing else on the server would tell them.
    /// </summary>
    [Fact]
    public async Task StartingAgainstAStoreWithPairingsReportsEachOfThem()
    {
        var file = FileInAFreshDirectory();
        var store = new FilePairingKeyStore(file);

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Add(AnotherPairing, KeyMaterial.Fresh());

        var log = new CapturingLogger();

        await new StoreAtStartup(store, log).StartAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, log.Written.Count);
        Assert.All(log.Written, written => Assert.Equal(LogLevel.Information, written.Level));
        Assert.Contains(log.Written, written => written.Text.Contains(PairingId, StringComparison.Ordinal));
        Assert.Contains(log.Written, written => written.Text.Contains(AnotherPairing, StringComparison.Ordinal));
    }

    /// <summary>
    /// A store holding nothing is silent, and looking at it does not bring it into existence.
    /// The second half is the one that matters: the store is created lazily and with its own
    /// permissions, and a reader that creates an empty file at every boot would undo that on
    /// every server that has never paired anything.
    /// </summary>
    [Fact]
    public async Task StartingAgainstNoStoreSaysNothingAndCreatesNothing()
    {
        var file = FileInAFreshDirectory();
        var log = new CapturingLogger();

        await new StoreAtStartup(new FilePairingKeyStore(file), log).StartAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(log.Written);
        Assert.False(File.Exists(file));
        Assert.False(Directory.Exists(Path.GetDirectoryName(file)));
    }

    /// <summary>
    /// A normal shutdown deletes nothing from the store. The bytes on disk and what the store
    /// answers with are both compared across it, because a sweep that removed a pairing and a
    /// rewrite that kept them all are different failures and only one of them shows up in the
    /// identifiers.
    /// </summary>
    [Fact]
    public async Task ANormalShutdownDeletesNothingFromTheStore()
    {
        var file = FileInAFreshDirectory();
        var store = new FilePairingKeyStore(file);

        store.Add(PairingId, KeyMaterial.Fresh());
        store.Add(AnotherPairing, KeyMaterial.Fresh());

        var onDisk = await File.ReadAllBytesAsync(file).ConfigureAwait(true);
        var held = store.Pairings().OrderBy(id => id, StringComparer.Ordinal).ToArray();

        var startup = new StoreAtStartup(store, new CapturingLogger());

        await startup.StartAsync(CancellationToken.None).ConfigureAwait(true);
        await startup.StopAsync(CancellationToken.None).ConfigureAwait(true);

        Assert.True(File.Exists(file));
        Assert.Equal(onDisk, await File.ReadAllBytesAsync(file).ConfigureAwait(true));
        Assert.Equal(held, store.Pairings().OrderBy(id => id, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// A store that cannot be read leaves the server running and says so at Error. A hosted
    /// service whose start throws stops the host, so a key store file that does not parse
    /// would otherwise take the whole server down at boot - and a store that does not parse is
    /// the case issue #33 says nothing answers for yet.
    /// </summary>
    [Fact]
    public async Task AStoreThatCannotBeReadLeavesTheServerRunningAndSaysSo()
    {
        var log = new CapturingLogger();

        await new StoreAtStartup(new UnreadableStore(), log).StartAsync(CancellationToken.None).ConfigureAwait(true);

        var written = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Error, written.Level);
        Assert.IsType<InvalidDataException>(written.Fault);
    }

    /// <summary>
    /// The same case against the real file store, over a file that is not JSON at all, so the
    /// exception is the one a server would actually meet rather than one a substitute chose.
    /// </summary>
    [Fact]
    public async Task AFileStoreOverBytesThatAreNotJsonIsTheSameCase()
    {
        var file = FileInAFreshDirectory();

        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "this is not a key store").ConfigureAwait(true);

        var log = new CapturingLogger();

        await new StoreAtStartup(new FilePairingKeyStore(file), log).StartAsync(CancellationToken.None).ConfigureAwait(true);

        var written = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Error, written.Level);
        Assert.NotNull(written.Fault);
    }

    /// <summary>
    /// Nothing written at startup carries key material. The store is filled with keys this
    /// case generated, so it knows every byte it is looking for, and it looks for each of them
    /// in the base64 the file holds them as.
    /// </summary>
    [Fact]
    public async Task NothingWrittenAtStartupCarriesKeyMaterial()
    {
        var file = FileInAFreshDirectory();
        var store = new FilePairingKeyStore(file);

        var first = KeyMaterial.Fresh();
        var second = KeyMaterial.Fresh();

        store.Add(PairingId, first);
        store.Add(AnotherPairing, second);

        var log = new CapturingLogger();

        await new StoreAtStartup(store, log).StartAsync(CancellationToken.None).ConfigureAwait(true);

        var captured = string.Join("\n", log.Written.Select(written => written.Text));

        foreach (var secret in new[] { first, second })
        {
            Assert.DoesNotContain(Convert.ToBase64String(secret.Span), captured, StringComparison.Ordinal);
        }
    }

    private string FileInAFreshDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "server-pairing-tests-" + Guid.NewGuid().ToString("n"));

        _directories.Add(directory);

        return Path.Combine(directory, KeyStorePath.FileName);
    }

    /// <summary>
    /// A store whose read throws, so the catch is reached without depending on what a
    /// particular malformed file happens to produce.
    /// </summary>
    private sealed class UnreadableStore : IPairingKeyStore
    {
        public KeyMaterial? Live(string pairingId, DateTimeOffset at) => throw new InvalidDataException();

        public PairingKeys? Both(string pairingId, DateTimeOffset at) => throw new InvalidDataException();

        public void Add(string pairingId, KeyMaterial current) => throw new InvalidDataException();

        public void Replace(string pairingId, KeyMaterial replacement, DateTimeOffset supersededStopsAt)
            => throw new InvalidDataException();

        public void Destroy(string pairingId) => throw new InvalidDataException();

        public IReadOnlyList<string> Pairings() => throw new InvalidDataException();
    }

    private sealed class CapturingLogger : ILogger<StoreAtStartup>
    {
        public List<(LogLevel Level, string Text, Exception? Fault)> Written { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Written.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
