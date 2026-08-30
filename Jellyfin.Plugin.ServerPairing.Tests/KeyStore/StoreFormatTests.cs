using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// The format number the key store's file carries, the refusal of a file newer than this
/// build, and the ladder that carries an older file up.
/// </summary>
/// <remarks>
/// The migration cases run against a file committed to this repository rather than against a
/// document built here. That is issue #55's own rule and it is the whole point: a case that
/// constructs the old shape from the current types is a case about the current types, and it
/// goes on passing after the shape it was written for stops being what an older build wrote.
/// <para>
/// The fixture was produced by running the store at <c>e35f4e5</c>, which is the commit before
/// the envelope existed, rather than typed out. Its two pairings are the two states a pairing
/// can be in: one that has never rotated, whose superseded key is absent and whose overlap
/// instant is the default one that value serialises to, and one that has, carrying both keys
/// and a real instant.
/// </para>
/// </remarks>
public sealed class StoreFormatTests : IDisposable
{
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    private const string NeverRotated = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Rotated = "0011223344556677889900aabbccddee";

    private const string NeverRotatedKey =
        "00112233445566778899aabbccddeeff00112233445566778899aabbccddeeff";

    private const string RotatedCurrentKey =
        "0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0";

    private const string RotatedSupersededKey =
        "ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100";

    private static readonly DateTimeOffset _overlapEnds = DateTimeOffset.FromUnixTimeSeconds(1786003600);
    private static readonly DateTimeOffset _beforeOverlapEnds = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(candidate => System.IO.Directory.Exists(candidate)))
        {
            System.IO.Directory.Delete(directory, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The first write of a store this build makes carries the format number. This is the
    /// clause issue #55 exists for: a file already in the field under no envelope is the case
    /// that costs the most to repair, and the number is what stops the next one arriving.
    /// </summary>
    [Fact]
    public void AStoreThisBuildWritesCarriesTheFormatNumberFromItsFirstWrite()
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);

        new FilePairingKeyStore(file).Add(NeverRotated, KeyMaterial.Fresh());

        var document = Document(file);

        Assert.Equal(StoreFormat.Current, StoreFormat.Read(document));
        Assert.Equal(new[] { NeverRotated }, StoreFormat.Pairings(document).Select(pairing => pairing.Key));
    }

    /// <summary>
    /// The committed fixture really is in the older format. Without this the migration cases
    /// below would pass over a file that had never needed migrating.
    /// </summary>
    [Fact]
    public void TheCommittedFixtureIsInTheFormatItIsNamedFor()
    {
        var document = (JsonObject)JsonNode.Parse(System.IO.File.ReadAllText(Fixture()))!;

        Assert.Equal(StoreFormat.Unversioned, StoreFormat.Read(document));
        Assert.False(document.ContainsKey(StoreFormat.FormatMember));
        Assert.False(document.ContainsKey(StoreFormat.PairingsMember));
    }

    /// <summary>
    /// The harness: the committed fixture goes up every rung, in order, and what comes out is
    /// in the current format with every pairing and every member it arrived with.
    /// </summary>
    [Fact]
    public void TheCommittedFixtureIsCarriedUpEveryRungToTheCurrentFormat()
    {
        var before = (JsonObject)JsonNode.Parse(System.IO.File.ReadAllText(Fixture()))!;
        var names = before.Select(pairing => pairing.Key).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        var after = StoreFormat.Migrate((JsonObject)before.DeepClone(), "fixture");

        Assert.Equal(StoreFormat.Current, StoreFormat.Read(after));

        var pairings = StoreFormat.Pairings(after);

        Assert.Equal(names, pairings.Select(pairing => pairing.Key).OrderBy(name => name, StringComparer.Ordinal));

        foreach (var name in names)
        {
            Assert.Equal(before[name]!.ToJsonString(), pairings[name]!.ToJsonString());
        }
    }

    /// <summary>
    /// A document already in the current format is not carried anywhere, so a store that has
    /// been migrated once is not migrated again on every read afterwards.
    /// </summary>
    [Fact]
    public void ADocumentAlreadyInTheCurrentFormatIsLeftWhereItIs()
    {
        var document = StoreFormat.Wrap(new JsonObject());

        var carried = StoreFormat.Migrate(document, "fixture");

        Assert.Same(document, carried);
    }

    /// <summary>
    /// A member the rung never named survives the way up. A migration that deserialised into
    /// this build's own type would drop it, which is the failure the ladder works on the parsed
    /// document to avoid.
    /// </summary>
    [Fact]
    public void AMemberTheRungDoesNotKnowSurvivesTheWayUp()
    {
        var document = new JsonObject
        {
            [NeverRotated] = new JsonObject
            {
                ["current"] = NeverRotatedKey,
                ["somethingAnotherBuildWrote"] = "kept",
            },
        };

        var pairing = (JsonObject)StoreFormat.Pairings(StoreFormat.Migrate(document, "fixture"))[NeverRotated]!;

        Assert.Equal("kept", (string?)pairing["somethingAnotherBuildWrote"]);
    }

    /// <summary>
    /// In format 0 every member is a pairing identifier, so a pairing named like the format
    /// member does not make the file look versioned. The value's kind is what separates them.
    /// </summary>
    [Fact]
    public void AFormatZeroFileHoldingAPairingNamedLikeTheFormatMemberIsStillFormatZero()
    {
        var document = new JsonObject
        {
            [StoreFormat.FormatMember] = new JsonObject { ["current"] = NeverRotatedKey },
        };

        Assert.Equal(StoreFormat.Unversioned, StoreFormat.Read(document));
    }

    /// <summary>
    /// End to end: the store reads a file written by the older build and answers from it, with
    /// both keys and the overlap the rotation left.
    /// </summary>
    [Fact]
    public void AStoreOpenedOnTheOlderFormatAnswersFromWhatItHeld()
    {
        var store = new FilePairingKeyStore(WithFixture());

        Assert.Equal(
            new[] { Rotated, NeverRotated },
            store.Pairings().OrderBy(name => name, StringComparer.Ordinal));

        Assert.True(store.Live(NeverRotated, _beforeOverlapEnds)!.SameAs(Key(NeverRotatedKey)));

        var both = store.Both(Rotated, _beforeOverlapEnds)!;

        Assert.True(both.Current.SameAs(Key(RotatedCurrentKey)));
        Assert.True(both.Superseded!.SameAs(Key(RotatedSupersededKey)));
        Assert.Equal(_overlapEnds, both.SupersededStopsAt);
    }

    /// <summary>
    /// Reading it puts it in the current format on disk, so the migration happens once rather
    /// than on every call for the rest of the store's life.
    /// </summary>
    [Fact]
    public void ReadingTheOlderFormatPutsTheStoreInTheCurrentOneOnDisk()
    {
        var file = WithFixture();

        new FilePairingKeyStore(file).Pairings();

        Assert.Equal(StoreFormat.Current, StoreFormat.Read(Document(file)));
    }

    /// <summary>
    /// The copy of the pre-migration file is beside the store, named for the format it is in,
    /// and holds exactly the bytes that were there.
    /// </summary>
    [Fact]
    public void MigratingLeavesTheFileItMigratedFromBesideTheStore()
    {
        var file = WithFixture();

        new FilePairingKeyStore(file).Pairings();

        var copy = file + StoreFormat.BackupSuffix(StoreFormat.Unversioned);

        Assert.True(System.IO.File.Exists(copy));
        Assert.Equal(System.IO.File.ReadAllBytes(Fixture()), System.IO.File.ReadAllBytes(copy));
    }

    /// <summary>
    /// A file in a format this build does not know is refused, and the refusal says what it
    /// found and what this build understands rather than being an unexplained failure.
    /// </summary>
    [Fact]
    public void AStoreInANewerFormatIsRefusedAndTheRefusalNamesBothNumbers()
    {
        var file = WithFuture();

        var refusal = Assert.Throws<StoreFormatRefusedException>(() => new FilePairingKeyStore(file).Pairings());

        Assert.Equal(StoreFormat.Current + 1, refusal.Found);
        Assert.Equal(StoreFormat.Current, refusal.Understood);
        Assert.Equal(file, refusal.File);
    }

    /// <summary>
    /// Every operation refuses, not only the one that happens to be first. A store that
    /// refused a read and accepted a write would answer a pairing out of a file it had already
    /// said it could not read.
    /// </summary>
    [Fact]
    public void NoOperationOnAStoreInANewerFormatAnswers()
    {
        var store = new FilePairingKeyStore(WithFuture());

        Assert.Throws<StoreFormatRefusedException>(() => store.Pairings());
        Assert.Throws<StoreFormatRefusedException>(() => store.Live(NeverRotated, _beforeOverlapEnds));
        Assert.Throws<StoreFormatRefusedException>(() => store.Both(NeverRotated, _beforeOverlapEnds));
        Assert.Throws<StoreFormatRefusedException>(() => store.Add(NeverRotated, KeyMaterial.Fresh()));
        Assert.Throws<StoreFormatRefusedException>(
            () => store.Replace(NeverRotated, KeyMaterial.Fresh(), _overlapEnds));
        Assert.Throws<StoreFormatRefusedException>(() => store.Destroy(NeverRotated));
    }

    /// <summary>
    /// The refusal changes nothing on disk. A downgrade that damaged the file on its way to
    /// refusing would take the newer plugin's store with it.
    /// </summary>
    [Fact]
    public void ARefusedStoreIsLeftExactlyAsItWas()
    {
        var file = WithFuture();
        var was = System.IO.File.ReadAllBytes(file);

        Assert.Throws<StoreFormatRefusedException>(() => new FilePairingKeyStore(file).Destroy(NeverRotated));

        Assert.Equal(was, System.IO.File.ReadAllBytes(file));
        Assert.Equal(
            new[] { Path.GetFileName(file) },
            System.IO.Directory.GetFiles(Path.GetDirectoryName(file)!).Select(Path.GetFileName));
    }

    /// <summary>
    /// A migration that fails at the write leaves the file it was migrating from exactly as it
    /// was, and the plugin refusing rather than running on half of a migration.
    /// </summary>
    /// <remarks>
    /// The failure is driven through the seam that puts a written file in place, which is the
    /// only point at which a migration can fail after the temporary file exists. What is
    /// asserted afterwards is not only that the bytes are unchanged but that a store with no
    /// such seam then reads them: a file that is intact and unreadable would satisfy a byte
    /// comparison and fail an operator.
    /// </remarks>
    [Fact]
    public void AFailedMigrationLeavesTheOriginalReadableAndTheStoreRefusing()
    {
        var file = WithFixture();

        var store = new FilePairingKeyStore(
            file,
            (temporary, destination) => throw new IOException("the disk is full"));

        Assert.Throws<IOException>(() => store.Pairings());

        Assert.Equal(System.IO.File.ReadAllBytes(Fixture()), System.IO.File.ReadAllBytes(file));
        Assert.Equal(StoreFormat.Unversioned, StoreFormat.Read(Document(file)));

        Assert.Throws<IOException>(() => store.Live(NeverRotated, _beforeOverlapEnds));

        Assert.True(new FilePairingKeyStore(file).Live(NeverRotated, _beforeOverlapEnds)!.SameAs(Key(NeverRotatedKey)));
    }

    /// <summary>
    /// Nothing half written is left where the store is. The temporary the failed write made is
    /// removed, and the copy of the pre-migration file is the only other thing in the
    /// directory - which is deliberate: it is a copy of the original rather than of anything
    /// the failed migration produced.
    /// </summary>
    [Fact]
    public void AFailedMigrationLeavesNothingHalfWrittenBesideTheStore()
    {
        var file = WithFixture();

        var store = new FilePairingKeyStore(
            file,
            (temporary, destination) => throw new IOException("the disk is full"));

        Assert.Throws<IOException>(() => store.Pairings());

        Assert.Equal(
            new[]
            {
                Path.GetFileName(file),
                Path.GetFileName(file) + StoreFormat.BackupSuffix(StoreFormat.Unversioned),
            }.OrderBy(name => name, StringComparer.Ordinal),
            System.IO.Directory.GetFiles(Path.GetDirectoryName(file)!)
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            System.IO.File.ReadAllBytes(Fixture()),
            System.IO.File.ReadAllBytes(file + StoreFormat.BackupSuffix(StoreFormat.Unversioned)));
    }

    /// <summary>
    /// A migration is the one thing this store does that nobody asked it for, so it says so.
    /// An operator who is not told finds a second file holding key material beside their store
    /// with nothing saying where it came from.
    /// </summary>
    [Fact]
    public void MigratingSaysSoAndNamesBothFormatsAndTheCopy()
    {
        var file = WithFixture();
        var written = new CapturingLogger();

        new FilePairingKeyStore(file, null, written).Pairings();

        var line = Assert.Single(written.Written);

        Assert.Equal(LogLevel.Information, line.Level);
        Assert.Contains(StoreFormat.Unversioned.ToString(System.Globalization.CultureInfo.InvariantCulture), line.Text, StringComparison.Ordinal);
        Assert.Contains(StoreFormat.Current.ToString(System.Globalization.CultureInfo.InvariantCulture), line.Text, StringComparison.Ordinal);
        Assert.Contains(file + StoreFormat.BackupSuffix(StoreFormat.Unversioned), line.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The line carries no key material, in any of the three encodings a key can reach a log
    /// through. The store's file is full of them at the moment this is written, so a line naming
    /// the file's contents rather than its name would carry every key the store holds.
    /// </summary>
    [Fact]
    public void TheLineAboutAMigrationCarriesNoKeyMaterial()
    {
        var file = WithFixture();
        var written = new CapturingLogger();

        new FilePairingKeyStore(file, null, written).Pairings();

        var line = Assert.Single(written.Written).Text;

        foreach (var hex in new[] { NeverRotatedKey, RotatedCurrentKey, RotatedSupersededKey })
        {
            Assert.DoesNotContain(hex, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(Convert.ToBase64String(Convert.FromHexString(hex)), line, StringComparison.Ordinal);
        }

        Assert.DoesNotContain(System.IO.File.ReadAllText(Fixture()), line, StringComparison.Ordinal);
    }

    /// <summary>
    /// A store that needs no migration says nothing. A line written every time the file is read
    /// is a line an operator learns to skip, and this file is read on every call.
    /// </summary>
    [Fact]
    public void AStoreThatNeedsNoMigrationSaysNothing()
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);
        var written = new CapturingLogger();
        var store = new FilePairingKeyStore(file, null, written);

        store.Pairings();
        store.Add(NeverRotated, KeyMaterial.Fresh());
        store.Pairings();

        Assert.Empty(written.Written);
    }

    /// <summary>
    /// A migration that fails says nothing either. A line saying the store was carried up,
    /// written by a run that then failed to carry it, names a file that is still to be migrated
    /// and an operator reads it as done.
    /// </summary>
    [Fact]
    public void AFailedMigrationSaysNothing()
    {
        var file = WithFixture();
        var written = new CapturingLogger();

        var store = new FilePairingKeyStore(
            file,
            (temporary, destination) => throw new IOException("the disk is full"),
            written);

        Assert.Throws<IOException>(() => store.Pairings());

        Assert.Empty(written.Written);
    }

    private static KeyMaterial Key(string hex) => KeyMaterial.From(Convert.FromHexString(hex));

    private static JsonObject Document(string file) =>
        (JsonObject)JsonNode.Parse(System.IO.File.ReadAllText(file))!;

    private static string Fixture() => Path.Join(
        RepositoryRoot(),
        "Jellyfin.Plugin.ServerPairing.Tests",
        "KeyStore",
        "Fixtures",
        "keys.format-0.json");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !System.IO.File.Exists(Path.Join(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the key store fixture has no root to be read from.")
            : directory.FullName;
    }

    /// <summary>
    /// A directory the store would accept as its own.
    /// </summary>
    /// <returns>The directory, which exists.</returns>
    /// <remarks>
    /// Made through <see cref="StorePermissions.PrepareDirectory"/> rather than through
    /// <see cref="System.IO.Directory.CreateDirectory(string)"/>, because on a platform that
    /// expresses a Unix mode the store refuses a directory wider than its own and a directory
    /// made at the process umask is wider. The cases here need one that exists before the store
    /// does, to put the fixture in, so they make it the way the store would.
    /// </remarks>
    private string TemporaryDirectory()
    {
        var directory = Path.Join(
            Path.GetTempPath(),
            "server-pairing-format-" + Guid.NewGuid().ToString("N", System.Globalization.CultureInfo.InvariantCulture));

        _directories.Add(directory);

        StorePermissions.PrepareDirectory(directory);

        return directory;
    }

    private string WithFixture()
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);

        System.IO.File.Copy(Fixture(), file);

        return file;
    }

    private string WithFuture()
    {
        var file = Path.Join(TemporaryDirectory(), KeyStorePath.FileName);

        System.IO.File.WriteAllText(
            file,
            "{\"" + StoreFormat.FormatMember + "\":" + (StoreFormat.Current + 1) + ",\""
                + StoreFormat.PairingsMember + "\":{}}");

        return file;
    }

    private sealed class CapturingLogger : ILogger<FilePairingKeyStore>
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
