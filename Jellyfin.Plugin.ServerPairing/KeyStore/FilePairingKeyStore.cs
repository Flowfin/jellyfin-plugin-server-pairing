using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// The key store a server runs on: one file, in the store's own directory, outside the plugin
/// configuration.
/// </summary>
/// <remarks>
/// Every operation reads the file, changes what it holds and writes it back, so what is on
/// disk is what the store answers with and nothing is cached to go stale against a file
/// somebody replaced. That costs a read per call and buys a store with no second copy of the
/// truth, which is the trade this size of file is worth making.
/// <para>
/// Key material never meets a serialiser. <see cref="KeyMaterial"/> is converted to base64 at
/// this one place and back at this one place, so there is no path by which a serialiser
/// somewhere else discovers a key on an object it was walking.
/// </para>
/// <para>
/// Every operation on one store instance is serialised by one lock, and every write goes
/// through <see cref="AtomicWrite"/>, so a reader sees the file as it was before a write or as
/// it is after one and never as it is during one. That is the whole of the mechanism, stated
/// rather than left to be inferred from a type name.
/// </para>
/// <para>
/// The lock is per instance, so what makes it cover the server is the singleton registration
/// in <see cref="PluginServiceRegistrator"/>: two instances over one file would each serialise
/// their own callers and neither would see the other. A SECOND PROCESS IS OUT OF REACH
/// ENTIRELY - an operator editing the file by hand while the server runs is not serialised by
/// anything, and the last write wins.
/// </para>
/// <para>
/// TWO NEIGHBOURING RULES ARE NOT HERE AND EACH HAS AN ISSUE. The file is created with
/// whatever permissions the platform gives it, which is issue #35. What a restored, copied or
/// corrupt store does is issue #33, and a file that does not parse currently throws rather
/// than being answered for. Reading this class as covering either is the mistake this
/// paragraph exists to stop.
/// </para>
/// <para>
/// The file carries the format number <see cref="StoreFormat"/> declares, and a read is where
/// an older one is carried up to it. So a READ CAN WRITE, which the rest of this class does
/// not otherwise do: meeting a file in an older format writes a copy of it beside the store
/// and then the migrated file, both through <see cref="AtomicWrite"/>. A file that is absent
/// is still not created by looking at it, and a file already in the current format is not
/// rewritten.
/// </para>
/// <para>
/// A MIGRATION PRESERVES A MEMBER IT DOES NOT KNOW AND THE NEXT WRITE DOES NOT. The ladder
/// works on the parsed document, so a member some other build wrote inside a pairing survives
/// the way up; the next call that writes serialises this build's own type and holds only what
/// that type holds. That is the same bound the store had before the envelope existed, and it
/// is the reason <see cref="StoreFormatRefusedException"/> refuses a NEWER file outright rather
/// than reading what it recognises.
/// </para>
/// </remarks>
public sealed class FilePairingKeyStore : IPairingKeyStore
{
    private readonly JsonSerializerOptions _format = new JsonSerializerOptions
    {
        // Nothing about this file is read by a person, so the compact form is the right one,
        // and writing it the same way every time keeps a diff of it about what changed.
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly Lock _gate = new Lock();
    private readonly string _file;
    private readonly Action<string, string>? _moveIntoPlace;
    private readonly ILogger<FilePairingKeyStore>? _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingKeyStore"/> class.
    /// </summary>
    /// <param name="file">The file the keys are held in.</param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    public FilePairingKeyStore(string file)
        : this(file, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingKeyStore"/> class, with the step
    /// that puts a written file in place replaced.
    /// </summary>
    /// <param name="file">The file the keys are held in.</param>
    /// <param name="moveIntoPlace">
    /// How a temporary file becomes the store's file, or null for the platform's own move.
    /// </param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    /// <remarks>
    /// The seam exists for one reason: the interesting failure of an atomic write is a failure
    /// BETWEEN writing the temporary file and putting it in place, and nothing outside this
    /// class can arrange one. A caller that passes its own is driving that failure; a server
    /// uses the constructor above.
    /// </remarks>
    public FilePairingKeyStore(string file, Action<string, string>? moveIntoPlace)
        : this(file, moveIntoPlace, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingKeyStore"/> class, with somewhere
    /// to say that a migration happened.
    /// </summary>
    /// <param name="file">The file the keys are held in.</param>
    /// <param name="moveIntoPlace">
    /// How a temporary file becomes the store's file, or null for the platform's own move.
    /// </param>
    /// <param name="logger">Where a migration is reported, or null to report it nowhere.</param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    /// <remarks>
    /// The logger is optional because most of what this class does is answering a caller, which
    /// reports itself through its return value. The one thing it does that nobody asked for is
    /// rewriting the file it was only asked to read, and an operator who is not told about that
    /// finds a second file holding key material beside their store with nothing saying where it
    /// came from.
    /// <para>
    /// Nothing else here writes a line. A read that throws is reported by throwing, and the row
    /// in <c>docs/logging.md</c> for a store that could not be read or written belongs to
    /// whoever catches it.
    /// </para>
    /// </remarks>
    public FilePairingKeyStore(string file, Action<string, string>? moveIntoPlace, ILogger<FilePairingKeyStore>? logger)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _moveIntoPlace = moveIntoPlace;
        _logger = logger;
    }

    /// <summary>
    /// Gets the file this store reads and writes.
    /// </summary>
    public string File => _file;

    /// <inheritdoc />
    public KeyMaterial? Live(string pairingId, DateTimeOffset at) => Both(pairingId, at)?.Current;

    /// <inheritdoc />
    public PairingKeys? Both(string pairingId, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Read();

            return held.TryGetValue(pairingId, out var stored) ? PairingKeyOverlap.AsOf(stored.AsKeys(pairingId), at) : null;
        }
    }

    /// <inheritdoc />
    public void Add(string pairingId, KeyMaterial current)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(current);

        lock (_gate)
        {
            var held = Read();

            if (held.ContainsKey(pairingId))
            {
                throw new InvalidOperationException(
                    "A key is already held for this pairing, and replacing one is a rotation rather than an add.");
            }

            held[pairingId] = StoredPairing.From(new PairingKeys(pairingId, current, null, default));

            Write(held);
        }
    }

    /// <inheritdoc />
    public void Replace(string pairingId, KeyMaterial replacement, DateTimeOffset supersededStopsAt)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(replacement);

        lock (_gate)
        {
            var held = Read();

            if (!held.TryGetValue(pairingId, out var stored))
            {
                throw new InvalidOperationException(
                    "There is no key held for this pairing, so there is nothing for a replacement to supersede.");
            }

            var was = stored.AsKeys(pairingId);

            held[pairingId] = StoredPairing.From(
                new PairingKeys(pairingId, replacement, was.Current, supersededStopsAt));

            Write(held);
        }
    }

    /// <inheritdoc />
    public void Destroy(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Read();

            if (held.Remove(pairingId))
            {
                Write(held);
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Pairings()
    {
        lock (_gate)
        {
            return new List<string>(Read().Keys);
        }
    }

    private Dictionary<string, StoredPairing> Read()
    {
        if (!System.IO.File.Exists(_file))
        {
            return new Dictionary<string, StoredPairing>(StringComparer.Ordinal);
        }

        var json = System.IO.File.ReadAllText(_file);

        // A file holding the literal null answered with an empty store before the envelope
        // existed and answers the same way now. Anything else that is not an object threw
        // then and throws now: what a damaged store should do instead is issue #33, and it is
        // deliberately not decided here.
        if (JsonNode.Parse(json) is not JsonObject document)
        {
            return new Dictionary<string, StoredPairing>(StringComparer.Ordinal);
        }

        var format = StoreFormat.Read(document);

        if (format > StoreFormat.Current)
        {
            throw new StoreFormatRefusedException(format, StoreFormat.Current, _file);
        }

        if (format < StoreFormat.Current)
        {
            document = MigrateOnDisk(json, document, format);
        }

        var read = StoreFormat.Pairings(document).Deserialize<Dictionary<string, StoredPairing>>(_format);

        return read is null
            ? new Dictionary<string, StoredPairing>(StringComparer.Ordinal)
            : new Dictionary<string, StoredPairing>(read, StringComparer.Ordinal);
    }

    /// <summary>
    /// Carries an older file up to the current format and puts the result on disk, keeping a
    /// copy of what was there beside it.
    /// </summary>
    /// <param name="json">The bytes read from the file.</param>
    /// <param name="document">Those bytes parsed.</param>
    /// <param name="from">The format they are in.</param>
    /// <returns>The document in the current format.</returns>
    /// <remarks>
    /// The copy is written first and the store second, so a migration that fails at the write
    /// leaves the original file exactly as it was and a copy of that same original beside it.
    /// The alternative ordering leaves a copy of a file the store no longer holds.
    /// <para>
    /// A failed write throws out of here and therefore out of whichever store operation asked,
    /// so the plugin refuses rather than answering from a half-migrated file. The next call
    /// reads the original again and tries the same migration, which is the behaviour an
    /// operator who fixes a full disk wants.
    /// </para>
    /// </remarks>
    private JsonObject MigrateOnDisk(string json, JsonObject document, int from)
    {
        var migrated = StoreFormat.Migrate(document, _file);

        var copy = _file + StoreFormat.BackupSuffix(from);

        AtomicWrite.Replace(copy, json);

        AtomicWrite.Replace(_file, migrated.ToJsonString(_format), _moveIntoPlace);

        // After both writes rather than before either. A line saying the store was migrated,
        // written by a run that then failed to migrate it, is worse than no line: the file it
        // names is the one still to be migrated and the operator reads it as done.
        // The guard is around the writing only, and it is here because the analyzers refuse a
        // call at a level that can be switched off without one. The migration above happens
        // whatever the level is.
        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "The key store was written by an older build and has been carried up to the format this one reads. What was there is kept beside it and nothing removes it, so delete it once the pairings work. Was format: {WasFormat}. Is format: {IsFormat}. Copy: {Copy}",
                from,
                StoreFormat.Current,
                copy);
        }

        return migrated;
    }

    private void Write(Dictionary<string, StoredPairing> held)
    {
        var pairings = JsonSerializer.SerializeToNode(held, _format) ?? new JsonObject();

        AtomicWrite.Replace(_file, StoreFormat.Wrap(pairings).ToJsonString(_format), _moveIntoPlace);
    }

    /// <summary>
    /// One pairing as the file holds it. This is the only shape key material takes outside
    /// <see cref="KeyMaterial"/>, and it exists so that the conversion happens at one place a
    /// reader can find rather than wherever a serialiser meets an object.
    /// </summary>
    private sealed class StoredPairing
    {
        [JsonPropertyName("current")]
        public string Current { get; set; } = string.Empty;

        [JsonPropertyName("superseded")]
        public string? Superseded { get; set; }

        [JsonPropertyName("supersededStopsAt")]
        public long SupersededStopsAt { get; set; }

        /// <summary>
        /// Lowercase hex rather than base64. Both encode the same bytes, and the serialiser
        /// escapes a base64 alphabet's plus sign into an escape sequence, so what lands in the
        /// file is then neither the encoding this code wrote nor one a reader can search for.
        /// Hex is alphanumeric, so it survives every escaping rule untouched, and it is the
        /// encoding docs/crypto.md and docs/protocol.md already use everywhere else.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <returns>The bytes as lowercase hex.</returns>
        public static string Hex(KeyMaterial key) => Convert.ToHexString(key.Span).ToLowerInvariant();

        public static StoredPairing From(PairingKeys keys) => new StoredPairing
        {
            Current = Hex(keys.Current),
            Superseded = keys.Superseded is null ? null : Hex(keys.Superseded),
            SupersededStopsAt = keys.SupersededStopsAt.ToUnixTimeSeconds(),
        };

        public PairingKeys AsKeys(string pairingId) => new PairingKeys(
            pairingId,
            KeyMaterial.From(Convert.FromHexString(Current)),
            Superseded is null ? null : KeyMaterial.From(Convert.FromHexString(Superseded)),
            DateTimeOffset.FromUnixTimeSeconds(SupersededStopsAt));
    }
}
