using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using MediaBrowser.Common.Configuration;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The pairing record store a server runs on, over a real file.
/// </summary>
/// <remarks>
/// Issue #311. Until this store landed, every implementation of
/// <see cref="IPairingRecordStore"/> was a fixture inside this project, so
/// <see cref="PairingStateMachine"/> was proved and unreachable: nothing a server can build
/// could hold a pairing at all.
/// <para>
/// The cases that matter here are the ones a fixture cannot make. A record surviving the object
/// that wrote it is the whole point of a file, so the assertions read a SECOND store over the
/// same path rather than the one that wrote, and the two states with opposite answers - the row
/// that must go and the row that must stay - are driven through the state machine and then read
/// off the file rather than off the machine's own bookkeeping.
/// </para>
/// </remarks>
public sealed class FilePairingRecordStoreTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Peer = "peer";
    private const string Administrator = "administrator";
    private const string Address = "https://peer.example";

    private static readonly DateTimeOffset _at = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <summary>
    /// Files that are there and are not a record store. Each parses as JSON, so none of them is
    /// caught by the parser failing, and none is the shape a write of this store leaves.
    /// </summary>
    /// <remarks>
    /// The last two are the ones a reader is tempted to let through. A document carrying no
    /// format number is an OLD file to the key store and is damage here, because this store has
    /// never written one without an envelope; and a record naming a state this build has no name
    /// for would deserialise to the default of the enumeration, which is
    /// <see cref="PairingState.Absent"/>, so a revoked pairing would come back as one that was
    /// never enrolled.
    /// <para>
    /// THE ONES CARRYING A NUMBER CARRY THE CURRENT ONE, AND THAT MATTERS SINCE THERE ARE TWO.
    /// A fixture pinned to the first format would ask these questions of a document on its way
    /// through a migration rather than of the document this build writes, and the shape a write of
    /// this store leaves is the subject. The first format is the subject of its own cases, where a
    /// document that is intact in it is read rather than refused.
    /// </para>
    /// </remarks>
    public static TheoryData<string, string> ParsesAndIsNotARecordStore => new TheoryData<string, string>
    {
        { "the literal null", "null" },
        { "an array", "[]" },
        { "a number", "17" },
        { "a string", "\"records\"" },
        { "no records member", "{\"format\":2}" },
        { "records as an array", "{\"format\":2,\"records\":[]}" },
        { "records as null", "{\"format\":2,\"records\":null}" },
        { "a record that is a number", "{\"format\":2,\"records\":{\"p\":5}}" },
        { "no records member in the first format", "{\"format\":1}" },
        { "records as an array in the first format", "{\"format\":1,\"records\":[]}" },
        { "no format member", "{\"records\":{}}" },
        { "a format that is not a number", "{\"format\":\"one\",\"records\":{}}" },
        { "a format below the earliest one", "{\"format\":0,\"records\":{}}" },
        { "a state this build has no name for", "{\"format\":2,\"records\":{\"p\":{\"state\":99,\"cameFrom\":0,\"cause\":\"Revoke\",\"actor\":\"peer\",\"at\":1}}}" },
        { "a state this build has no name for in the first format", "{\"format\":1,\"records\":{\"p\":{\"state\":99,\"cameFrom\":0,\"cause\":\"Revoke\",\"actor\":\"peer\",\"at\":1}}}" },
    };

    /// <summary>
    /// Files that do not parse at all, which is what truncation and a partial overwrite actually
    /// look like on disk.
    /// </summary>
    public static TheoryData<string, string> DoesNotParse => new TheoryData<string, string>
    {
        { "an empty file", string.Empty },
        { "whitespace", "   \n" },
        { "a truncated document", "{\"format\":2,\"records\":{\"" },
        { "text that is not JSON", "<html>not a record store</html>" },
    };

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
    /// A record written by one instance is read back by a second one over the same file, with
    /// every field it was written with. What this proves that a fixture cannot is that the record
    /// survives the object rather than the call: the second store shares nothing with the first
    /// except the path.
    /// </summary>
    [Fact]
    public void ARecordWrittenByOneInstanceIsReadBackByAnother()
    {
        var file = FileInATemporaryDirectory();

        new FilePairingRecordStore(file).Write(new PairingRecord(
            PairingId,
            PairingState.Active,
            PairingState.ConfirmedByPeer,
            "Confirm",
            Peer,
            _at,
            Address));

        var read = new FilePairingRecordStore(file).Read(PairingId);

        Assert.NotNull(read);
        Assert.Equal(PairingId, read.PairingId);
        Assert.Equal(PairingState.Active, read.State);
        Assert.Equal(PairingState.ConfirmedByPeer, read.CameFrom);
        Assert.Equal("Confirm", read.Cause);
        Assert.Equal(Peer, read.Actor);
        Assert.Equal(_at, read.At);
    }

    /// <summary>
    /// A pairing nothing has written is absent rather than an error, and one written and then
    /// deleted is absent again. This is the floor under every case below: without it a store that
    /// answered null to everything would pass the refusals and prove nothing.
    /// </summary>
    [Fact]
    public void APairingNothingWroteIsAbsentAndOneDeletedIsAbsentAgain()
    {
        var file = FileInATemporaryDirectory();
        var store = new FilePairingRecordStore(file);

        Assert.Null(store.Read(PairingId));
        Assert.Empty(store.Pairings());

        store.Write(Revoked(PairingId));

        Assert.NotNull(store.Read(PairingId));
        Assert.Equal(new[] { PairingId }, store.Pairings());

        store.Delete(PairingId);

        Assert.Null(new FilePairingRecordStore(file).Read(PairingId));
        Assert.Empty(new FilePairingRecordStore(file).Pairings());
    }

    /// <summary>
    /// Deleting a pairing leaves the others where they were. A store that answered a delete by
    /// rewriting the file from one record would pass the case above and lose every other pairing
    /// on a server that has two.
    /// </summary>
    [Fact]
    public void DeletingOnePairingLeavesTheOthers()
    {
        var file = FileInATemporaryDirectory();
        var store = new FilePairingRecordStore(file);
        var second = ProvisionalPairingId.Mint();

        store.Write(Revoked(PairingId));
        store.Write(Revoked(second));

        store.Delete(PairingId);

        Assert.Equal(new[] { second }, new FilePairingRecordStore(file).Pairings());
    }

    /// <summary>
    /// The two rows with opposite answers, driven through the state machine and then read off the
    /// store rather than off the machine.
    /// </summary>
    /// <remarks>
    /// <see cref="PairingState.Absent"/> is defined by there being no record, and
    /// <see cref="PairingState.Revoked"/> keeps its record on purpose, so that a later request
    /// naming that identifier is refused rather than treated as new. Asserting either against
    /// <see cref="PairingStateMachine.StateOf"/> would assert the machine's reading of its own
    /// write; this reads the file through a store the machine never touched.
    /// </remarks>
    [Fact]
    public void TheAbsentRowDeletesAndTheRevokedRowKeeps()
    {
        var file = FileInATemporaryDirectory();
        var expired = ProvisionalPairingId.Mint();

        // Seeded through the store the machine is given, because there is no enrolment to drive
        // either pairing into these states: that is issue #19 and it is why the seam is here.
        var seed = new FilePairingRecordStore(file);

        seed.Write(new PairingRecord(expired, PairingState.Pending, PairingState.Offered, "Hello", Peer, _at, Address));
        seed.Write(new PairingRecord(PairingId, PairingState.Active, PairingState.ConfirmedByPeer, "Confirm", Peer, _at, Address));

        var machine = new PairingStateMachine(seed, new InMemoryUserMappings());

        Assert.Equal(PairingState.Absent, machine.Apply(expired, LocalEvent.WindowExpired, Administrator, _at).To);
        Assert.Equal(PairingState.Revoked, machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, _at).To);

        var onDisk = new FilePairingRecordStore(file);

        Assert.Null(onDisk.Read(expired));
        Assert.Equal(new[] { PairingId }, onDisk.Pairings());

        var kept = onDisk.Read(PairingId);

        Assert.NotNull(kept);
        Assert.Equal(PairingState.Revoked, kept.State);
        Assert.Equal(PairingState.Active, kept.CameFrom);
        Assert.Equal(Administrator, kept.Actor);
    }

    /// <summary>
    /// A record is held under a provisional identifier exactly as readily as under a derived one,
    /// which is what building this store to the answer <c>docs/protocol.md</c> takes for
    /// <see cref="PairingState.Offered"/> amounts to.
    /// </summary>
    /// <remarks>
    /// The two shapes are asserted to be different as well as both storable. A store that
    /// normalised, truncated or lower-cased its key would answer this case correctly for one of
    /// the two and silently merge two pairings for the other.
    /// </remarks>
    [Fact]
    public void AProvisionalIdentifierHoldsARecordAndIsNotAWireIdentifier()
    {
        var file = FileInATemporaryDirectory();
        var provisional = ProvisionalPairingId.Mint();

        Assert.True(ProvisionalPairingId.Is(provisional));
        Assert.False(ProvisionalPairingId.Is(PairingId));
        Assert.False(FieldShape.IsHexField(provisional));
        Assert.True(FieldShape.IsHexField(PairingId));

        var store = new FilePairingRecordStore(file);

        store.Write(new PairingRecord(provisional, PairingState.Offered, PairingState.Absent, "WindowOpened", Administrator, _at, Address));
        store.Write(Revoked(PairingId));

        var onDisk = new FilePairingRecordStore(file);

        Assert.Equal(PairingState.Offered, onDisk.Read(provisional)?.State);
        Assert.Equal(PairingState.Revoked, onDisk.Read(PairingId)?.State);
        Assert.Equal(2, onDisk.Pairings().Count);
    }

    /// <summary>
    /// Two windows opened one after the other are two records rather than one. The identifier is
    /// minted from random bytes for exactly this, and a mint built from a clock or a counter over
    /// a value this class does not hold would collide here.
    /// </summary>
    [Fact]
    public void TwoMintedIdentifiersAreTwoRecords()
    {
        var file = FileInATemporaryDirectory();
        var store = new FilePairingRecordStore(file);

        var first = ProvisionalPairingId.Mint();
        var second = ProvisionalPairingId.Mint();

        Assert.NotEqual(first, second);

        store.Write(new PairingRecord(first, PairingState.Offered, PairingState.Absent, "WindowOpened", Administrator, _at, Address));
        store.Write(new PairingRecord(second, PairingState.Offered, PairingState.Absent, "WindowOpened", Administrator, _at, Address));

        Assert.Equal(2, new FilePairingRecordStore(file).Pairings().Count);
    }

    /// <summary>
    /// A file that parses and is not a record store is refused rather than answered as an empty
    /// one. This is the key store's answer read one file over: an empty answer is what a fresh
    /// installation gives, so an operator meeting one pairs afresh over a state that is still on
    /// the disk in front of them.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(ParsesAndIsNotARecordStore))]
    public void AFileThatParsesAndIsNotARecordStoreIsRefused(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingRecordStore(file).Pairings());

        Assert.Equal(file, refusal.File);
    }

    /// <summary>
    /// A file that does not parse is refused with the same answer rather than with whatever the
    /// serialiser happens to throw.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(DoesNotParse))]
    public void AFileThatDoesNotParseIsRefused(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        Assert.Throws<StoreDamagedException>(() => new FilePairingRecordStore(file).Pairings());
    }

    /// <summary>
    /// Every operation refuses, not only the one the cases above happen to call. Each reads the
    /// file, so a store that refused on one path and answered on another would be a plugin that
    /// reports no pairings and writes into a damaged file anyway.
    /// </summary>
    [Fact]
    public void EveryOperationRefusesADamagedFile()
    {
        var store = new FilePairingRecordStore(WithContent("not a record store"));

        Assert.Throws<StoreDamagedException>(() => store.Pairings());
        Assert.Throws<StoreDamagedException>(() => store.Read(PairingId));
        Assert.Throws<StoreDamagedException>(() => store.Write(Revoked(PairingId)));
        Assert.Throws<StoreDamagedException>(() => store.Delete(PairingId));
    }

    /// <summary>
    /// The refusal names the file and tells an operator not to pair over it, and it names THIS
    /// store rather than the key store beside it. A sentence naming the wrong file sends somebody
    /// to look at a file that is fine.
    /// </summary>
    [Fact]
    public void TheRefusalNamesThisStoreAndItsFile()
    {
        var file = WithContent("not a record store");

        var refusal = Assert.Throws<StoreDamagedException>(() => new FilePairingRecordStore(file).Pairings());

        Assert.Contains(file, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(StoreDamagedException.RecordStoreName, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StoreDamagedException.KeyStoreName, refusal.Message, StringComparison.Ordinal);
        Assert.Contains("aside", refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A file in a newer format is refused as a rolled-back plugin rather than as damage, which
    /// is the distinction the two exceptions exist for: one is fixed by installing the newer
    /// plugin again and the other is not fixed by installing anything.
    /// </summary>
    [Fact]
    public void AFileInANewerFormatIsRefusedAsARollback()
    {
        var file = WithContent("{\"format\":" + (RecordStoreFormat.Current + 1).ToString(CultureInfo.InvariantCulture) + ",\"records\":{}}");

        var refusal = Assert.Throws<StoreFormatRefusedException>(() => new FilePairingRecordStore(file).Pairings());

        Assert.Equal(RecordStoreFormat.Current + 1, refusal.Found);
        Assert.Equal(RecordStoreFormat.Current, refusal.Understood);
        Assert.Contains(StoreDamagedException.RecordStoreName, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A write that fails between the temporary file and the move leaves the previous record
    /// readable. The record store carries the same seam the key store does, and for the same
    /// reason: nothing outside the class can arrange that failure.
    /// </summary>
    [Fact]
    public void AWriteThatFailsLeavesThePreviousRecordReadable()
    {
        var file = FileInATemporaryDirectory();

        new FilePairingRecordStore(file).Write(Revoked(PairingId));

        var breaks = new FilePairingRecordStore(
            file,
            (_, _) => throw new IOException("the move failed"));

        Assert.Throws<IOException>(() => breaks.Write(new PairingRecord(
            PairingId,
            PairingState.Active,
            PairingState.ConfirmedByPeer,
            "Confirm",
            Peer,
            _at,
            Address)));

        Assert.Equal(PairingState.Revoked, new FilePairingRecordStore(file).Read(PairingId)?.State);
    }

    /// <summary>
    /// The file the store runs on is under the directory the key store owns and is not the key
    /// store's own file. Two stores writing one file is the collision this name exists against.
    /// </summary>
    [Fact]
    public void TheStoreFileIsBesideTheKeyStoreAndIsNotIt()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(Path.Join(Path.GetTempPath(), "server-pairing-path"));

        var records = RecordStorePath.FileFor(paths);
        var keys = KeyStorePath.FileFor(paths);

        Assert.Equal(Path.GetDirectoryName(keys), Path.GetDirectoryName(records));
        Assert.NotEqual(keys, records);
        Assert.Equal(RecordStorePath.FileName, Path.GetFileName(records));
    }

    private static PairingRecord Revoked(string pairingId) => new PairingRecord(
        pairingId,
        PairingState.Revoked,
        PairingState.Active,
        "Revoke",
        Peer,
        _at,
        Address);

    private string WithContent(string bytes)
    {
        var file = FileInATemporaryDirectory();

        File.WriteAllText(file, bytes);

        return file;
    }

    private string FileInATemporaryDirectory() => Path.Join(TemporaryDirectory(), RecordStorePath.FileName);

    /// <summary>
    /// A directory the store would accept as its own.
    /// </summary>
    /// <returns>The directory, which exists.</returns>
    /// <remarks>
    /// Made through <see cref="StorePermissions.PrepareDirectory"/> rather than through
    /// <see cref="Directory.CreateDirectory(string)"/>, because on a platform that expresses a
    /// Unix mode the store refuses a directory wider than its own and one made at the process
    /// umask is wider.
    /// </remarks>
    private string TemporaryDirectory()
    {
        var directory = Path.Join(
            Path.GetTempPath(),
            "server-pairing-records-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _directories.Add(directory);

        StorePermissions.PrepareDirectory(directory);

        return directory;
    }
}
