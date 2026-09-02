using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using Jellyfin.Plugin.ServerPairing.KeyStore;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The pairing record store a server runs on: one file, in the same directory as the key store,
/// outside the plugin configuration.
/// </summary>
/// <remarks>
/// This is the implementation <see cref="IPairingRecordStore"/> spent its whole life without.
/// Until it landed, <see cref="PairingStateMachine"/> could be constructed only against a
/// fixture inside the test project, so the state machine was proved and unreachable: nothing a
/// server can build could hold a pairing at all.
/// <para>
/// Every operation reads the file, changes what it holds and writes it back, so what is on disk
/// is what the store answers with and nothing is cached to go stale against a file somebody
/// replaced. That is the key store's trade, taken again here for the same reason and at the same
/// price of a read per call.
/// </para>
/// <para>
/// Every operation on one instance is serialised by one lock, and every write goes through
/// <see cref="AtomicWrite"/>, so a reader sees the file as it was before a write or as it is
/// after one and never as it is during one. The lock is per instance, so what makes it cover the
/// server is the singleton registration in <see cref="PluginServiceRegistrator"/>. A SECOND
/// PROCESS IS OUT OF REACH ENTIRELY, exactly as it is for the key store.
/// </para>
/// <para>
/// A FILE THAT IS THERE AND IS NOT A RECORD STORE IS REFUSED, with the same answer the key store
/// gives and for the same reason: an empty store is what a fresh installation has, so answering
/// an unreadable file as an empty one invites an operator to pair afresh over a state they have
/// not actually lost. <see cref="StoreDamagedException"/> is the refusal and carries the whole of
/// that argument. What this class cannot see is a file that is an intact record store and is
/// nevertheless the wrong one - restored from a backup, or copied from another machine - which is
/// issue #33 and is the key store's limit read one file over.
/// </para>
/// <para>
/// A FILE CARRYING NO FORMAT NUMBER IS DAMAGED HERE AND IS AN OLD FILE THERE, and the difference
/// is not an inconsistency. The key store wrote files before its number existed and they are on
/// operators' disks; this store has never shipped without one, so nothing that wrote a file
/// without an envelope was this plugin. <see cref="RecordStoreFormat"/> is where that is argued.
/// </para>
/// <para>
/// NO KEY MATERIAL PASSES THROUGH HERE, which <see cref="PairingRecord"/> states about itself and
/// this class does not soften: the record says what state a pairing is in and how it got there,
/// and what verifies a request is the key store. The two are separate files on purpose, so a key
/// store that refuses does not also take away the state an operator is trying to read.
/// </para>
/// <para>
/// THE PAIRING IDENTIFIER IS NOT ASSUMED TO BE A WIRE IDENTIFIER. A record held while a pairing
/// is <see cref="PairingState.Offered"/> is held under a <see cref="ProvisionalPairingId"/>,
/// which is longer than a wire identifier and carries a character no hex field may hold, so the
/// key of this map is an opaque string here and the shape is judged where it is minted.
/// </para>
/// </remarks>
public sealed class FilePairingRecordStore : IPairingRecordStore
{
    private readonly JsonSerializerOptions _format = new JsonSerializerOptions
    {
        // Nothing about this file is read by a person, so the compact form is the right one, and
        // writing it the same way every time keeps a diff of it about what changed.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new Lock();
    private readonly string _file;
    private readonly Action<string, string>? _moveIntoPlace;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingRecordStore"/> class.
    /// </summary>
    /// <param name="file">The file the records are held in.</param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    public FilePairingRecordStore(string file)
        : this(file, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingRecordStore"/> class, with the
    /// step that puts a written file in place replaced.
    /// </summary>
    /// <param name="file">The file the records are held in.</param>
    /// <param name="moveIntoPlace">
    /// How a temporary file becomes the store's file, or null for the platform's own move.
    /// </param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    /// <remarks>
    /// The seam exists for one reason: the interesting failure of an atomic write is a failure
    /// BETWEEN writing the temporary file and putting it in place, and nothing outside this class
    /// can arrange one. A caller that passes its own is driving that failure; a server uses the
    /// constructor above.
    /// </remarks>
    public FilePairingRecordStore(string file, Action<string, string>? moveIntoPlace)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _moveIntoPlace = moveIntoPlace;
    }

    /// <summary>
    /// Gets the file this store reads and writes.
    /// </summary>
    public string File => _file;

    /// <inheritdoc />
    public PairingRecord? Read(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Held();

            return held.TryGetValue(pairingId, out var stored) ? stored.AsRecord(pairingId) : null;
        }
    }

    /// <inheritdoc />
    public void Write(PairingRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            var held = Held();

            held[record.PairingId] = StoredRecord.From(record);

            Put(held);
        }
    }

    /// <inheritdoc />
    public void Delete(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Held();

            // A write only where something was removed. A delete of a pairing that is not there
            // is the caller reaching Absent from Absent, and rewriting the file for it would let
            // anything that can drive a transition make this server write to disk as fast as it
            // can answer.
            if (held.Remove(pairingId))
            {
                Put(held);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// THIS REMARK SAID THE WALK IS NOT ON <see cref="IPairingRecordStore"/> AND IS DELIBERATELY
    /// NOT PUT THERE. It is on the interface now. What that sentence rested on was that the only
    /// caller was the state machine, which needs one record at a time, and the two callers it
    /// named as needing a walk - issues #40 and #60 - were both unbuilt. The administrative read
    /// of the open enrolment windows is a third, it resolves the interface rather than this
    /// class, and a plane built against a concrete file store to get a walk is the coupling the
    /// interface exists against.
    /// <para>
    /// The reason it was here first is unchanged and is why the cases below read it: a store that
    /// cannot say what it holds cannot be proved to have swept itself.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> Pairings()
    {
        lock (_gate)
        {
            return new List<string>(Held().Keys);
        }
    }

    private Dictionary<string, StoredRecord> Held()
    {
        if (!System.IO.File.Exists(_file))
        {
            return new Dictionary<string, StoredRecord>(StringComparer.Ordinal);
        }

        var json = System.IO.File.ReadAllText(_file);

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException damaged)
        {
            throw StoreDamagedException.For(_file, damaged, StoreDamagedException.RecordStoreName);
        }

        if (parsed is not JsonObject document)
        {
            throw StoreDamagedException.For(_file, StoreDamagedException.RecordStoreName);
        }

        var format = RecordStoreFormat.Read(document);

        if (format > RecordStoreFormat.Current)
        {
            throw new StoreFormatRefusedException(
                format,
                RecordStoreFormat.Current,
                _file,
                StoreDamagedException.RecordStoreName);
        }

        // Below the current format there is no rung, and there is no file either: this store has
        // never written one. So a document declaring anything under the current number was not
        // written by this plugin, which is damage rather than age.
        if (format < RecordStoreFormat.Current)
        {
            throw StoreDamagedException.For(_file, StoreDamagedException.RecordStoreName);
        }

        return Deserialise(Records(document));
    }

    /// <summary>
    /// The records member of a document, or a refusal where it holds anything but an object.
    /// </summary>
    /// <param name="document">The parsed store.</param>
    /// <returns>The records.</returns>
    /// <exception cref="StoreDamagedException">The member is absent or is not an object.</exception>
    /// <remarks>
    /// <see cref="RecordStoreFormat.Records"/> answers an absent member with an empty object,
    /// which is the right answer for a caller asking what a document holds and the wrong one for
    /// a store deciding whether it may answer at all: every write this store makes puts an object
    /// there, so a document without one was not written by this plugin.
    /// </remarks>
    private JsonObject Records(JsonObject document) =>
        document.TryGetPropertyValue(RecordStoreFormat.RecordsMember, out var member) && member is JsonObject records
            ? records
            : throw StoreDamagedException.For(_file, StoreDamagedException.RecordStoreName);

    private Dictionary<string, StoredRecord> Deserialise(JsonObject records)
    {
        Dictionary<string, StoredRecord>? read;

        try
        {
            read = records.Deserialize<Dictionary<string, StoredRecord>>(_format);
        }
        catch (JsonException damaged)
        {
            throw StoreDamagedException.For(_file, damaged, StoreDamagedException.RecordStoreName);
        }

        if (read is null)
        {
            throw StoreDamagedException.For(_file, StoreDamagedException.RecordStoreName);
        }

        // A member that parsed as an object and holds a state this build has no name for is
        // damage rather than a record with a surprising value. Reading it would put a pairing
        // into whatever the default of the enumeration is, which is Absent, and a revoked pairing
        // silently reading as absent is the one outcome the kept record exists to prevent.
        //
        // One question over the sequence rather than a loop that throws inside itself. The loop
        // this replaces walked the values and refused the first bad one, which is a filter
        // followed by a single action, and it read as a missed Where to static analysis for
        // exactly that reason. The damage is a property of the document rather than of the
        // record that happens to carry it, so asking once and throwing once says what is meant.
        if (read.Values.Any(stored => stored is null || !Enum.IsDefined(stored.State) || !Enum.IsDefined(stored.CameFrom)))
        {
            throw StoreDamagedException.For(_file, StoreDamagedException.RecordStoreName);
        }

        return new Dictionary<string, StoredRecord>(read, StringComparer.Ordinal);
    }

    private void Put(Dictionary<string, StoredRecord> held)
    {
        var records = JsonSerializer.SerializeToNode(held, _format) ?? new JsonObject();

        AtomicWrite.Replace(_file, RecordStoreFormat.Wrap(records).ToJsonString(_format), _moveIntoPlace);
    }

    /// <summary>
    /// One pairing's record as the file holds it.
    /// </summary>
    /// <remarks>
    /// The identifier is the key of the map rather than a member of this type, so a file cannot
    /// hold a record whose identifier disagrees with the one it is filed under.
    /// </remarks>
    private sealed class StoredRecord
    {
        [JsonPropertyName("state")]
        public PairingState State { get; set; }

        [JsonPropertyName("cameFrom")]
        public PairingState CameFrom { get; set; }

        [JsonPropertyName("cause")]
        public string Cause { get; set; } = string.Empty;

        [JsonPropertyName("actor")]
        public string Actor { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets when the transition happened, as seconds since the epoch.
        /// </summary>
        /// <remarks>
        /// Seconds rather than the round-trip text form, because the wire already measures
        /// freshness in whole seconds and a record that carried more precision than the protocol
        /// does would invite somebody to compare the two.
        /// </remarks>
        [JsonPropertyName("at")]
        public long At { get; set; }

        public static StoredRecord From(PairingRecord record) => new StoredRecord
        {
            State = record.State,
            CameFrom = record.CameFrom,
            Cause = record.Cause,
            Actor = record.Actor,
            At = record.At.ToUnixTimeSeconds(),
        };

        public PairingRecord AsRecord(string pairingId) => new PairingRecord(
            pairingId,
            State,
            CameFrom,
            Cause,
            Actor,
            DateTimeOffset.FromUnixTimeSeconds(At));
    }
}
