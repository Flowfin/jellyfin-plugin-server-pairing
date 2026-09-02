using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The mapping store a server runs on, over a real file.
/// </summary>
/// <remarks>
/// Issue #36. Until this store landed, every implementation of <see cref="IUserMappingStore"/>
/// was a fixture inside this project, so <see cref="PairingStateMachine"/> could not be
/// registered on a server at all: it requires a mapping store, and a registration the container
/// cannot satisfy is a plugin that fails to load. The model was proved and unreachable.
/// <para>
/// The cases that matter here are the ones a fixture cannot make. A mapping surviving the object
/// that wrote it is the whole point of a file, so the assertions read a SECOND store over the
/// same path rather than the one that wrote; and the sweep, which is the property the model
/// rests on, is driven through the state machine over two real files and then read off the disk
/// rather than off the machine's own bookkeeping.
/// </para>
/// </remarks>
public sealed class FileUserMappingStoreTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";
    private const string LocalUser = "local-user-1";
    private const string AnotherLocalUser = "local-user-2";
    private const string PeerUser = "peer-user-1";
    private const string Administrator = "administrator";
    private const string Peer = "peer";

    private static readonly DateTimeOffset _at = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    private readonly List<string> _directories = new List<string>();

    /// <summary>
    /// Files that are there and are not a mapping store. Each parses as JSON, so none of them is
    /// caught by the parser failing, and none is the shape a write of this store leaves.
    /// </summary>
    /// <remarks>
    /// The last four are the rows this store refuses that the pairing record store has no
    /// equivalent of. A mapping is built by a constructor that refuses a blank identifier and a
    /// blank actor, so a row missing one of those is a row this build cannot turn into a mapping:
    /// letting it through would either throw that constructor's own exception out of a read or
    /// answer a table quietly shorter than its file, and a mapping table missing a row sends one
    /// person's data nowhere or to somebody else.
    /// </remarks>
    public static TheoryData<string, string> ParsesAndIsNotAMappingStore => new TheoryData<string, string>
    {
        { "the literal null", "null" },
        { "an array", "[]" },
        { "a number", "17" },
        { "a string", "\"mappings\"" },
        { "no mappings member", "{\"format\":1}" },
        { "mappings as an array", "{\"format\":1,\"mappings\":[]}" },
        { "mappings as null", "{\"format\":1,\"mappings\":null}" },
        { "a pairing that is a number", "{\"format\":1,\"mappings\":{\"p\":5}}" },
        { "no format member", "{\"mappings\":{}}" },
        { "a format that is not a number", "{\"format\":\"one\",\"mappings\":{}}" },
        { "a blank pairing", "{\"format\":1,\"mappings\":{\" \":{\"u\":{\"peerUserId\":\"v\",\"peerDisplayName\":\"\",\"actor\":\"a\",\"at\":1}}}}" },
        { "a blank local user", "{\"format\":1,\"mappings\":{\"p\":{\" \":{\"peerUserId\":\"v\",\"peerDisplayName\":\"\",\"actor\":\"a\",\"at\":1}}}}" },
        { "a blank peer user", "{\"format\":1,\"mappings\":{\"p\":{\"u\":{\"peerUserId\":\" \",\"peerDisplayName\":\"\",\"actor\":\"a\",\"at\":1}}}}" },
        { "a blank actor", "{\"format\":1,\"mappings\":{\"p\":{\"u\":{\"peerUserId\":\"v\",\"peerDisplayName\":\"\",\"actor\":\" \",\"at\":1}}}}" },
        { "no display name at all", "{\"format\":1,\"mappings\":{\"p\":{\"u\":{\"peerUserId\":\"v\",\"actor\":\"a\",\"at\":1}}}}" },
    };

    /// <summary>
    /// Files that do not parse at all, which is what truncation and a partial overwrite actually
    /// look like on disk.
    /// </summary>
    public static TheoryData<string, string> DoesNotParse => new TheoryData<string, string>
    {
        { "an empty file", string.Empty },
        { "whitespace", "   \n" },
        { "a truncated document", "{\"format\":1,\"mappings\":{\"" },
        { "text that is not JSON", "<html>not a mapping store</html>" },
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
    /// A mapping written by one instance is read back by a second one over the same file, with
    /// every field it was written with. What this proves that a fixture cannot is that the row
    /// survives the object rather than the call: the second store shares nothing with the first
    /// except the path.
    /// </summary>
    [Fact]
    public void AMappingWrittenByOneInstanceIsReadBackByAnother()
    {
        var file = FileInATemporaryDirectory();

        new FileUserMappingStore(file).Put(new UserMapping(
            PairingId,
            LocalUser,
            PeerUser,
            "Anna Example",
            Administrator,
            _at));

        var read = Assert.Single(new FileUserMappingStore(file).For(PairingId));

        Assert.Equal(PairingId, read.PairingId);
        Assert.Equal(LocalUser, read.LocalUserId);
        Assert.Equal(PeerUser, read.PeerUserId);
        Assert.Equal("Anna Example", read.PeerDisplayName);
        Assert.Equal(Administrator, read.Actor);
        Assert.Equal(_at, read.At);
    }

    /// <summary>
    /// A display name a peer never sent survives as the empty string rather than as an absence.
    /// The field is allowed to be empty and is not allowed to be missing, and a store that wrote
    /// an empty one as nothing would read its own file back as damaged.
    /// </summary>
    [Fact]
    public void AnEmptyDisplayNameSurvivesAWriteAndARead()
    {
        var file = FileInATemporaryDirectory();

        new FileUserMappingStore(file).Put(MappingFor(LocalUser, PeerUser, string.Empty));

        Assert.Equal(string.Empty, Assert.Single(new FileUserMappingStore(file).For(PairingId)).PeerDisplayName);
    }

    /// <summary>
    /// A pairing nothing wrote holds no mappings, and one whose mapping was removed holds none
    /// again. This is the floor under every case below: without it a store that answered an empty
    /// table to everything would pass the refusals and prove nothing.
    /// </summary>
    [Fact]
    public void APairingNothingWroteHoldsNoneAndOneRemovedHoldsNoneAgain()
    {
        var file = FileInATemporaryDirectory();
        var store = new FileUserMappingStore(file);

        Assert.Empty(store.For(PairingId));

        store.Put(MappingFor(LocalUser, PeerUser));

        Assert.Single(new FileUserMappingStore(file).For(PairingId));

        store.Remove(PairingId, LocalUser);

        Assert.Empty(new FileUserMappingStore(file).For(PairingId));
    }

    /// <summary>
    /// Removing one mapping leaves the others, under the same pairing and under a different one.
    /// A store that answered a removal by rewriting the file from what it was asked about would
    /// pass the case above and lose every other row on a server that has two.
    /// </summary>
    [Fact]
    public void RemovingOneMappingLeavesTheOthers()
    {
        var file = FileInATemporaryDirectory();
        var store = new FileUserMappingStore(file);

        store.Put(MappingFor(LocalUser, PeerUser));
        store.Put(MappingFor(AnotherLocalUser, "peer-user-2"));
        store.Put(new UserMapping(AnotherPairing, LocalUser, PeerUser, "Anna", Administrator, _at));

        store.Remove(PairingId, LocalUser);

        var onDisk = new FileUserMappingStore(file);

        Assert.Equal(AnotherLocalUser, Assert.Single(onDisk.For(PairingId)).LocalUserId);
        Assert.Single(onDisk.For(AnotherPairing));
    }

    /// <summary>
    /// A second mapping for the same pairing and the same local user replaces the first rather
    /// than leaving both. Nothing in this plugin asks for that today, because
    /// <see cref="UserMappings.Map"/> refuses a second mapping rather than passing it here, and
    /// the obligation is on the interface anyway: a partial write leaving two rows for one user
    /// is worse than either outcome, because a reader picking one of the two picks a person.
    /// </summary>
    [Fact]
    public void PuttingASecondMappingForOneLocalUserReplacesTheFirst()
    {
        var file = FileInATemporaryDirectory();
        var store = new FileUserMappingStore(file);

        store.Put(MappingFor(LocalUser, PeerUser));
        store.Put(MappingFor(LocalUser, "peer-user-9"));

        var read = Assert.Single(new FileUserMappingStore(file).For(PairingId));

        Assert.Equal("peer-user-9", read.PeerUserId);
    }

    /// <summary>
    /// The sweep, which is the property the whole model rests on, driven through the state
    /// machine over two real files and then read off the disk.
    /// </summary>
    /// <remarks>
    /// Both rows that end a pairing are walked, because they are opposite in the record store and
    /// identical here: reaching <see cref="PairingState.Absent"/> deletes the record and reaching
    /// <see cref="PairingState.Revoked"/> keeps it on purpose, and the mappings go either way. A
    /// sweep wired to the record being deleted would pass the expiry half of this case and leave
    /// every revoked pairing's mappings on the disk.
    /// </remarks>
    [Fact]
    public void EndingAPairingSweepsItsMappingsOnDiskByBothRoutes()
    {
        var directory = TemporaryDirectory();
        var file = Path.Join(directory, MappingStorePath.FileName);
        var records = Path.Join(directory, RecordStorePath.FileName);

        var mappings = new FileUserMappingStore(file);
        var machine = new PairingStateMachine(new FilePairingRecordStore(records), mappings);
        var surface = new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance);

        Open(machine, PairingId);
        Open(machine, AnotherPairing);

        Assert.Equal(MappingOutcome.Mapped, surface.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, _at));
        Assert.Equal(MappingOutcome.Mapped, surface.Map(AnotherPairing, LocalUser, PeerUser, "Anna", Administrator, _at));

        Assert.Single(new FileUserMappingStore(file).For(PairingId));
        Assert.Single(new FileUserMappingStore(file).For(AnotherPairing));

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, _at);

        Assert.Empty(new FileUserMappingStore(file).For(PairingId));
        Assert.Single(new FileUserMappingStore(file).For(AnotherPairing));

        machine.Apply(AnotherPairing, LocalEvent.WindowExpired, Administrator, _at);

        Assert.Empty(new FileUserMappingStore(file).For(AnotherPairing));
    }

    /// <summary>
    /// A mapping is held under a provisional identifier exactly as readily as under a derived
    /// one. An administrator may map users while a pairing is still being enrolled, so a store
    /// that normalised, truncated or lower-cased its key would answer for one shape and silently
    /// merge two pairings for the other.
    /// </summary>
    [Fact]
    public void AProvisionalIdentifierHoldsMappingsAndIsNotAWireIdentifier()
    {
        var file = FileInATemporaryDirectory();
        var provisional = ProvisionalPairingId.Mint();

        Assert.True(ProvisionalPairingId.Is(provisional));
        Assert.False(FieldShape.IsHexField(provisional));

        var store = new FileUserMappingStore(file);

        store.Put(new UserMapping(provisional, LocalUser, PeerUser, "Anna", Administrator, _at));
        store.Put(MappingFor(LocalUser, PeerUser));

        var onDisk = new FileUserMappingStore(file);

        Assert.Single(onDisk.For(provisional));
        Assert.Single(onDisk.For(PairingId));
    }

    /// <summary>
    /// A pairing whose last mapping went holds no table rather than an empty one, so the file
    /// does not grow an entry for every pairing that ever had a mapping removed and does not say
    /// this plugin holds something for a pairing when it holds nothing.
    /// </summary>
    [Fact]
    public void RemovingTheLastMappingTakesThePairingOutOfTheFile()
    {
        var file = FileInATemporaryDirectory();
        var store = new FileUserMappingStore(file);

        store.Put(MappingFor(LocalUser, PeerUser));
        store.Remove(PairingId, LocalUser);

        Assert.DoesNotContain(PairingId, File.ReadAllText(file), StringComparison.Ordinal);
    }

    /// <summary>
    /// A removal of a mapping that is not there, and a sweep of a pairing that holds none, write
    /// nothing at all. Every transition into <see cref="PairingState.Absent"/> reaches the sweep,
    /// and a store that rewrote its file for each of them would let anything able to drive a
    /// transition make this server write to disk as fast as it can answer.
    /// </summary>
    [Fact]
    public void RemovingWhatIsNotThereWritesNothing()
    {
        var file = FileInATemporaryDirectory();
        var writes = 0;

        var store = new FileUserMappingStore(
            file,
            (temporary, destination) =>
            {
                writes++;
                File.Move(temporary, destination, overwrite: true);
            });

        store.Remove(PairingId, LocalUser);
        store.RemoveEvery(PairingId);

        Assert.Equal(0, writes);
        Assert.False(File.Exists(file));

        store.Put(MappingFor(LocalUser, PeerUser));

        Assert.Equal(1, writes);

        store.Remove(PairingId, AnotherLocalUser);
        store.RemoveEvery(AnotherPairing);

        Assert.Equal(1, writes);
    }

    /// <summary>
    /// A file that parses and is not a mapping store is refused rather than answered as an empty
    /// table. An empty table is what a fresh installation has, so an administrator meeting one
    /// makes the mappings again, on top of rows that are still on the disk in front of them.
    /// </summary>
    /// <param name="shape">What is wrong with the file, for the case name.</param>
    /// <param name="bytes">The file's whole content.</param>
    [Theory]
    [MemberData(nameof(ParsesAndIsNotAMappingStore))]
    public void AFileThatParsesAndIsNotAMappingStoreIsRefused(string shape, string bytes)
    {
        Assert.NotEmpty(shape);

        var file = WithContent(bytes);

        var refusal = Assert.Throws<StoreDamagedException>(() => new FileUserMappingStore(file).For(PairingId));

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

        Assert.Throws<StoreDamagedException>(() => new FileUserMappingStore(file).For(PairingId));
    }

    /// <summary>
    /// Every operation refuses, not only the one the cases above happen to call. Each reads the
    /// file, so a store that refused on one path and answered on another would be a plugin that
    /// shows an administrator no mappings and writes into a damaged file anyway.
    /// </summary>
    [Fact]
    public void EveryOperationRefusesADamagedFile()
    {
        var store = new FileUserMappingStore(WithContent("not a mapping store"));

        Assert.Throws<StoreDamagedException>(() => store.For(PairingId));
        Assert.Throws<StoreDamagedException>(() => store.Put(MappingFor(LocalUser, PeerUser)));
        Assert.Throws<StoreDamagedException>(() => store.Remove(PairingId, LocalUser));
        Assert.Throws<StoreDamagedException>(() => store.RemoveEvery(PairingId));
    }

    /// <summary>
    /// The refusal names the file and tells an operator not to write over it, and it names THIS
    /// store rather than either of the two beside it. A sentence naming the wrong file sends
    /// somebody to look at a file that is fine.
    /// </summary>
    [Fact]
    public void TheRefusalNamesThisStoreAndItsFile()
    {
        var file = WithContent("not a mapping store");

        var refusal = Assert.Throws<StoreDamagedException>(() => new FileUserMappingStore(file).For(PairingId));

        Assert.Contains(file, refusal.Message, StringComparison.Ordinal);
        Assert.Contains(StoreDamagedException.MappingStoreName, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StoreDamagedException.KeyStoreName, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(StoreDamagedException.RecordStoreName, refusal.Message, StringComparison.Ordinal);
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
        var file = WithContent(
            "{\"format\":" + (MappingStoreFormat.Current + 1).ToString(CultureInfo.InvariantCulture) + ",\"mappings\":{}}");

        var refusal = Assert.Throws<StoreFormatRefusedException>(() => new FileUserMappingStore(file).For(PairingId));

        Assert.Equal(MappingStoreFormat.Current + 1, refusal.Found);
        Assert.Equal(MappingStoreFormat.Current, refusal.Understood);
        Assert.Contains(StoreDamagedException.MappingStoreName, refusal.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A format added without the rung that carries a file up to it is refused rather than
    /// leaving the document where it was, so forgetting a migration is a refusal somebody reads
    /// instead of a file quietly half-read.
    /// </summary>
    [Fact]
    public void ThereIsNoMigrationAwayFromAFormatBelowTheCurrentOne()
    {
        var document = new JsonObject
        {
            [MappingStoreFormat.FormatMember] = MappingStoreFormat.Current - 1,
            [MappingStoreFormat.MappingsMember] = new JsonObject(),
        };

        Assert.Throws<InvalidOperationException>(() => MappingStoreFormat.Migrate(document));
    }

    /// <summary>
    /// A write that fails between the temporary file and the move leaves the previous table
    /// readable. The mapping store carries the same seam the other two do, and for the same
    /// reason: nothing outside the class can arrange that failure.
    /// </summary>
    [Fact]
    public void AWriteThatFailsLeavesThePreviousMappingReadable()
    {
        var file = FileInATemporaryDirectory();

        new FileUserMappingStore(file).Put(MappingFor(LocalUser, PeerUser));

        var breaks = new FileUserMappingStore(file, (_, _) => throw new IOException("the move failed"));

        Assert.Throws<IOException>(() => breaks.Put(MappingFor(LocalUser, "peer-user-9")));

        Assert.Equal(PeerUser, Assert.Single(new FileUserMappingStore(file).For(PairingId)).PeerUserId);
    }

    /// <summary>
    /// The file the store runs on is under the directory the key store owns and is neither of the
    /// two files already there. Two stores writing one file is the collision this name exists
    /// against.
    /// </summary>
    [Fact]
    public void TheStoreFileIsBesideTheOtherTwoAndIsNeitherOfThem()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.DataPath.Returns(Path.Join(Path.GetTempPath(), "server-pairing-path"));

        var mappings = MappingStorePath.FileFor(paths);
        var records = RecordStorePath.FileFor(paths);
        var keys = KeyStorePath.FileFor(paths);

        Assert.Equal(Path.GetDirectoryName(keys), Path.GetDirectoryName(mappings));
        Assert.NotEqual(keys, mappings);
        Assert.NotEqual(records, mappings);
        Assert.Equal(MappingStorePath.FileName, Path.GetFileName(mappings));
    }

    private static UserMapping MappingFor(string localUserId, string peerUserId, string displayName = "Anna Example")
        => new UserMapping(PairingId, localUserId, peerUserId, displayName, Administrator, _at);

    private static void Open(PairingStateMachine machine, string pairingId)
    {
        machine.Apply(pairingId, LocalEvent.WindowOpened, Administrator, _at);
        machine.Receive(pairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, _at);
    }

    private string WithContent(string bytes)
    {
        var file = FileInATemporaryDirectory();

        File.WriteAllText(file, bytes);

        return file;
    }

    private string FileInATemporaryDirectory() => Path.Join(TemporaryDirectory(), MappingStorePath.FileName);

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
            "server-pairing-mappings-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        _directories.Add(directory);

        StorePermissions.PrepareDirectory(directory);

        return directory;
    }
}
