using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The ladder the pairing record store's file climbs, and the read that climbs it.
/// </summary>
/// <remarks>
/// There was one format and no ladder, so every number below the current one was damage and the
/// walk was written for a caller that did not exist. Format 2 added the peer address, and this
/// file is what says a store written by a build that shipped before it is read by this one rather
/// than refused as a file this plugin never wrote.
/// <para>
/// WHAT IS NOT CLAIMED IS THAT A FORMAT 1 FILE EXISTS ON ANY DISK. Nothing on a server wrote a
/// record before the enrolment join landed, so a shipped build could reach the store and never
/// make a file. The fixtures below are written by hand, and no run on a server was made to look
/// for one.
/// </para>
/// </remarks>
public sealed class RecordStoreFormatTests : IDisposable
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    private const string FirstFormatFile =
        "{\"format\":1,\"records\":{\"" + PairingId
        + "\":{\"state\":5,\"cameFrom\":4,\"cause\":\"Confirm\",\"actor\":\"peer\",\"at\":1786000000}}}";

    private readonly List<string> _directories = new List<string>();

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var directory in _directories.Where(Directory.Exists))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    /// <summary>
    /// A store written by the previous rung is read by this build. The record comes back with
    /// every field the older format carried, and with no peer address, because the build that
    /// wrote it had none to write and inventing one would put an address on a record nobody ever
    /// entered one for.
    /// </summary>
    [Fact]
    public void AStoreWrittenByThePreviousRungIsRead()
    {
        var file = WithContent(FirstFormatFile);

        var read = new FilePairingRecordStore(file).Read(PairingId);

        Assert.NotNull(read);
        Assert.Equal(PairingState.Active, read.State);
        Assert.Equal(PairingState.ConfirmedByPeer, read.CameFrom);
        Assert.Equal("Confirm", read.Cause);
        Assert.Equal("peer", read.Actor);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1786000000), read.At);
        Assert.Null(read.PeerAddress);
    }

    /// <summary>
    /// The walk is watched happening rather than inferred from the read above succeeding. The
    /// document handed in declares the older format and the one handed back declares the current
    /// one, with the record carried across, so what moved is the number and not the records.
    /// </summary>
    [Fact]
    public void TheWalkMovesTheNumberAndCarriesTheRecords()
    {
        var document = Assert.IsType<JsonObject>(JsonNode.Parse(FirstFormatFile));

        Assert.Equal(RecordStoreFormat.Earliest, RecordStoreFormat.Read(document));

        var walked = RecordStoreFormat.Migrate(document);

        Assert.Equal(RecordStoreFormat.Current, RecordStoreFormat.Read(walked));
        Assert.Equal(
            RecordStoreFormat.Records(document).ToJsonString(),
            RecordStoreFormat.Records(walked).ToJsonString());
    }

    /// <summary>
    /// The document handed in is left as it was. A rung that re-parented the records member would
    /// empty the document its caller is holding, so a second read of the same parse would find a
    /// store with no records in it and answer that the server is paired with nobody.
    /// </summary>
    [Fact]
    public void TheWalkLeavesTheDocumentItWasGiven()
    {
        var document = Assert.IsType<JsonObject>(JsonNode.Parse(FirstFormatFile));

        RecordStoreFormat.Migrate(document);

        Assert.Equal(RecordStoreFormat.Earliest, RecordStoreFormat.Read(document));
        Assert.Equal(FirstFormatFile, document.ToJsonString());
    }

    /// <summary>
    /// A read does not rewrite the file. An operator's store moves to the current format at the
    /// next write, so a server that only ever reads leaves the file exactly as it found it, and a
    /// plugin rolled back after one read finds the store it left.
    /// </summary>
    [Fact]
    public void AReadDoesNotCarryTheFileItself()
    {
        var file = WithContent(FirstFormatFile);

        new FilePairingRecordStore(file).Read(PairingId);
        new FilePairingRecordStore(file).Pairings();

        Assert.Equal(FirstFormatFile, File.ReadAllText(file));
    }

    /// <summary>
    /// A write moves the file to the current format, and the record it wrote carries its address
    /// while the record carried up from the older format still carries none.
    /// </summary>
    [Fact]
    public void AWriteMovesTheFileToTheCurrentFormat()
    {
        var file = WithContent(FirstFormatFile);
        var store = new FilePairingRecordStore(file);
        var provisional = ProvisionalPairingId.Mint();

        store.Write(new PairingRecord(
            provisional,
            PairingState.Offered,
            PairingState.Absent,
            "WindowOpened",
            "an-administrator",
            DateTimeOffset.FromUnixTimeSeconds(1786000001),
            "https://peer.example"));

        var onDisk = Assert.IsType<JsonObject>(JsonNode.Parse(File.ReadAllText(file)));

        Assert.Equal(RecordStoreFormat.Current, RecordStoreFormat.Read(onDisk));

        var reread = new FilePairingRecordStore(file);

        Assert.Equal("https://peer.example", reread.Read(provisional)?.PeerAddress);
        Assert.Null(reread.Read(PairingId)?.PeerAddress);
    }

    /// <summary>
    /// A format above the current one is still refused as a rolled-back plugin rather than walked.
    /// The ladder climbs and does not descend, and a file a newer build wrote may hold members
    /// this one would drop on the next write.
    /// </summary>
    [Fact]
    public void AFormatAboveTheCurrentOneIsStillRefused()
    {
        var file = WithContent(
            "{\"format\":" + (RecordStoreFormat.Current + 1).ToString(CultureInfo.InvariantCulture) + ",\"records\":{}}");

        Assert.Throws<StoreFormatRefusedException>(() => new FilePairingRecordStore(file).Pairings());
    }

    /// <summary>
    /// A format with no rung away from it is refused rather than left where it is. This is the
    /// failure the walk exists to make loud: a number added to the ladder without its migration
    /// throws instead of handing back a document nothing carried.
    /// </summary>
    [Fact]
    public void AFormatWithNoRungIsRefused()
    {
        var document = Assert.IsType<JsonObject>(JsonNode.Parse("{\"format\":0,\"records\":{}}"));

        Assert.Throws<InvalidOperationException>(() => RecordStoreFormat.Migrate(document));
    }

    private string WithContent(string bytes)
    {
        var directory = Path.Join(Path.GetTempPath(), "pairing-format-" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(directory);
        _directories.Add(directory);

        var file = Path.Join(directory, RecordStorePath.FileName);

        File.WriteAllText(file, bytes);

        return file;
    }
}
