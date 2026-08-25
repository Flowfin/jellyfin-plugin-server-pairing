using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="FilePairingKeyStore"/> class.
    /// </summary>
    /// <param name="file">The file the keys are held in.</param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    public FilePairingKeyStore(string file)
        : this(file, null)
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
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _moveIntoPlace = moveIntoPlace;
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

        var read = JsonSerializer.Deserialize<Dictionary<string, StoredPairing>>(json, _format);

        return read is null
            ? new Dictionary<string, StoredPairing>(StringComparer.Ordinal)
            : new Dictionary<string, StoredPairing>(read, StringComparer.Ordinal);
    }

    private void Write(Dictionary<string, StoredPairing> held)
    {
        AtomicWrite.Replace(_file, JsonSerializer.Serialize(held, _format), _moveIntoPlace);
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
