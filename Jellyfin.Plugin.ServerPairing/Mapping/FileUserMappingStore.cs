using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using Jellyfin.Plugin.ServerPairing.KeyStore;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// The mapping store a server runs on: one file, in the same directory as the key store and the
/// pairing records, outside the plugin configuration.
/// </summary>
/// <remarks>
/// This is the implementation <see cref="IUserMappingStore"/> spent its whole life without.
/// Until it landed, every implementation was a fixture inside the test project, so
/// <see cref="Protocol.PairingStateMachine"/> could not be registered on a server at all: it
/// requires a mapping store, and a registration the container cannot satisfy is a plugin that
/// fails to load rather than one missing a feature. The model was proved and unreachable.
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
/// PROCESS IS OUT OF REACH ENTIRELY, exactly as it is for the two files beside this one.
/// </para>
/// <para>
/// A FILE THAT IS THERE AND IS NOT A MAPPING STORE IS REFUSED, with the same answer the other
/// two stores give and for the same reason: an empty table is what a fresh installation has, so
/// answering an unreadable file as an empty one shows an administrator a pairing with no
/// mappings and invites them to make the mappings again, on top of rows that are still on the
/// disk. <see cref="StoreDamagedException"/> is the refusal and carries the whole of that
/// argument.
/// </para>
/// <para>
/// A ROW THIS BUILD COULD NOT MAKE A MAPPING FROM IS DAMAGE RATHER THAN A ROW WITH A SURPRISING
/// VALUE. <see cref="UserMapping"/> refuses a blank identifier and a blank actor at
/// construction, because a mapping naming nobody on one side is not a decision anybody made. A
/// store that let such a row through would either throw whatever that constructor throws out of
/// a read, or skip the row and answer a table quietly shorter than its file - and a mapping
/// table missing a row is one person's data going to the default of nowhere or to somebody else.
/// So the whole document is refused instead.
/// </para>
/// <para>
/// NO KEY MATERIAL PASSES THROUGH HERE, which <see cref="UserMapping"/> states about itself and
/// this class does not soften. What it does hold is personal data:
/// <see cref="UserMapping.PeerDisplayName"/> names a person, which is why this file is written
/// with the key store's permissions rather than the platform's default, and <c>docs/data.md</c>
/// is where every field of it is argued.
/// </para>
/// <para>
/// THE PAIRING IDENTIFIER IS NOT ASSUMED TO BE A WIRE IDENTIFIER, for the reason the record
/// store gives: a pairing held while it is <see cref="Protocol.PairingState.Offered"/> is held
/// under a <see cref="Protocol.ProvisionalPairingId"/>, so the key of this map is an opaque
/// string here and the shape is judged where it is minted.
/// </para>
/// </remarks>
public sealed class FileUserMappingStore : IUserMappingStore
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
    /// Initializes a new instance of the <see cref="FileUserMappingStore"/> class.
    /// </summary>
    /// <param name="file">The file the mappings are held in.</param>
    /// <exception cref="ArgumentNullException">The file is null.</exception>
    public FileUserMappingStore(string file)
        : this(file, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FileUserMappingStore"/> class, with the step
    /// that puts a written file in place replaced.
    /// </summary>
    /// <param name="file">The file the mappings are held in.</param>
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
    public FileUserMappingStore(string file, Action<string, string>? moveIntoPlace)
    {
        _file = file ?? throw new ArgumentNullException(nameof(file));
        _moveIntoPlace = moveIntoPlace;
    }

    /// <summary>
    /// Gets the file this store reads and writes.
    /// </summary>
    public string File => _file;

    /// <inheritdoc />
    public IReadOnlyList<UserMapping> For(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Held();

            return held.TryGetValue(pairingId, out var under)
                ? under.Select(row => row.Value.AsMapping(pairingId, row.Key)).ToArray()
                : Array.Empty<UserMapping>();
        }
    }

    /// <inheritdoc />
    public void Put(UserMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        lock (_gate)
        {
            var held = Held();

            if (!held.TryGetValue(mapping.PairingId, out var under))
            {
                under = new Dictionary<string, StoredMapping>(StringComparer.Ordinal);
                held[mapping.PairingId] = under;
            }

            under[mapping.LocalUserId] = StoredMapping.From(mapping);

            Write(held);
        }
    }

    /// <inheritdoc />
    public void Remove(string pairingId, string localUserId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);
        ArgumentNullException.ThrowIfNull(localUserId);

        lock (_gate)
        {
            var held = Held();

            // A write only where something was removed, which is the record store's argument read
            // one file over: removing a mapping that is not there asks for the state the table is
            // already in, and rewriting the file for it would let anything that can reach this
            // make the server write to disk as fast as it can answer.
            if (!held.TryGetValue(pairingId, out var under) || !under.Remove(localUserId))
            {
                return;
            }

            Prune(held, pairingId, under);

            Write(held);
        }
    }

    /// <inheritdoc />
    public void RemoveEvery(string pairingId)
    {
        ArgumentNullException.ThrowIfNull(pairingId);

        lock (_gate)
        {
            var held = Held();

            if (held.Remove(pairingId))
            {
                Write(held);
            }
        }
    }

    /// <summary>
    /// Drops a pairing that holds no mappings any more.
    /// </summary>
    /// <param name="held">What the file holds.</param>
    /// <param name="pairingId">The pairing the last mapping was removed from.</param>
    /// <param name="under">What is left under it.</param>
    /// <remarks>
    /// A pairing whose last mapping went holds no table rather than an empty one. Leaving the
    /// member behind would put an entry in the file for every pairing that ever had a mapping
    /// removed, which is a file that only grows, and it would say this plugin holds something for
    /// a pairing when it holds nothing.
    /// </remarks>
    private static void Prune(
        Dictionary<string, Dictionary<string, StoredMapping>> held,
        string pairingId,
        Dictionary<string, StoredMapping> under)
    {
        if (under.Count == 0)
        {
            held.Remove(pairingId);
        }
    }

    private Dictionary<string, Dictionary<string, StoredMapping>> Held()
    {
        if (!System.IO.File.Exists(_file))
        {
            return new Dictionary<string, Dictionary<string, StoredMapping>>(StringComparer.Ordinal);
        }

        var json = System.IO.File.ReadAllText(_file);

        JsonNode? parsed;

        try
        {
            parsed = JsonNode.Parse(json);
        }
        catch (JsonException damaged)
        {
            throw StoreDamagedException.For(_file, damaged, StoreDamagedException.MappingStoreName);
        }

        if (parsed is not JsonObject document)
        {
            throw Damaged();
        }

        var format = MappingStoreFormat.Read(document);

        if (format > MappingStoreFormat.Current)
        {
            throw new StoreFormatRefusedException(
                format,
                MappingStoreFormat.Current,
                _file,
                StoreDamagedException.MappingStoreName);
        }

        // Below the current format there is no rung, and there is no file either: this store has
        // never written one. So a document declaring anything under the current number was not
        // written by this plugin, which is damage rather than age.
        if (format < MappingStoreFormat.Current)
        {
            throw Damaged();
        }

        return Deserialise(Mappings(document));
    }

    /// <summary>
    /// The mappings member of a document, or a refusal where it holds anything but an object.
    /// </summary>
    /// <param name="document">The parsed store.</param>
    /// <returns>The mappings.</returns>
    /// <exception cref="StoreDamagedException">The member is absent or is not an object.</exception>
    /// <remarks>
    /// <see cref="MappingStoreFormat.Mappings"/> answers an absent member with an empty object,
    /// which is the right answer for a caller asking what a document holds and the wrong one for
    /// a store deciding whether it may answer at all: every write this store makes puts an object
    /// there, so a document without one was not written by this plugin.
    /// </remarks>
    private JsonObject Mappings(JsonObject document) =>
        document.TryGetPropertyValue(MappingStoreFormat.MappingsMember, out var member) && member is JsonObject mappings
            ? mappings
            : throw Damaged();

    private Dictionary<string, Dictionary<string, StoredMapping>> Deserialise(JsonObject mappings)
    {
        Dictionary<string, Dictionary<string, StoredMapping>>? read;

        try
        {
            read = mappings.Deserialize<Dictionary<string, Dictionary<string, StoredMapping>>>(_format);
        }
        catch (JsonException damaged)
        {
            throw StoreDamagedException.For(_file, damaged, StoreDamagedException.MappingStoreName);
        }

        if (read is null)
        {
            throw Damaged();
        }

        // One question over the whole document rather than a throw inside a loop, which is the
        // shape the record store took for the same reason: the damage is a property of the file
        // rather than of the row that happens to carry it.
        //
        // What is asked is whether every row could become a mapping. A blank pairing, a blank
        // local user, a blank peer user and a blank actor are each refused by UserMapping at
        // construction, and a display name is allowed to be empty and not to be absent.
        if (read.Any(pairing => string.IsNullOrWhiteSpace(pairing.Key)
            || pairing.Value is null
            || pairing.Value.Any(row => string.IsNullOrWhiteSpace(row.Key) || row.Value is null || !row.Value.IsWhole)))
        {
            throw Damaged();
        }

        return read.ToDictionary(
            pairing => pairing.Key,
            pairing => new Dictionary<string, StoredMapping>(pairing.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private StoreDamagedException Damaged() =>
        StoreDamagedException.For(_file, StoreDamagedException.MappingStoreName);

    private void Write(Dictionary<string, Dictionary<string, StoredMapping>> held)
    {
        var mappings = JsonSerializer.SerializeToNode(held, _format) ?? new JsonObject();

        AtomicWrite.Replace(_file, MappingStoreFormat.Wrap(mappings).ToJsonString(_format), _moveIntoPlace);
    }

    /// <summary>
    /// One mapping as the file holds it.
    /// </summary>
    /// <remarks>
    /// The pairing and the local user are the two keys this row is filed under rather than
    /// members of this type, so a file cannot hold a mapping whose pairing or whose local user
    /// disagrees with where it is kept. That is also what makes <see cref="Put"/> a replacement
    /// of whatever was held for the same pairing and local user without anything having to
    /// search for it.
    /// </remarks>
    private sealed class StoredMapping
    {
        /// <summary>
        /// Gets or sets the user on the peer.
        /// </summary>
        [JsonPropertyName("peerUserId")]
        public string PeerUserId { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the peer's display name for that user.
        /// </summary>
        /// <remarks>
        /// A cache and never an identifier, which <see cref="UserMapping.PeerDisplayName"/>
        /// argues. It is allowed to be empty and is not allowed to be absent: a peer that sends
        /// no display name is not a reason to refuse a mapping an administrator asked for, and a
        /// member missing from the file is a document this plugin did not write.
        /// </remarks>
        [JsonPropertyName("peerDisplayName")]
        public string? PeerDisplayName { get; set; }

        /// <summary>
        /// Gets or sets the administrator who decided this mapping.
        /// </summary>
        [JsonPropertyName("actor")]
        public string Actor { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets when the administrator decided this, as seconds since the epoch.
        /// </summary>
        /// <remarks>
        /// Seconds rather than the round-trip text form, for the reason the pairing record store
        /// gives: the wire already measures in whole seconds, and a file carrying more precision
        /// than the protocol does would invite somebody to compare the two.
        /// </remarks>
        [JsonPropertyName("at")]
        public long At { get; set; }

        /// <summary>
        /// Gets a value indicating whether this row could become a mapping.
        /// </summary>
        public bool IsWhole =>
            !string.IsNullOrWhiteSpace(PeerUserId)
            && PeerDisplayName is not null
            && !string.IsNullOrWhiteSpace(Actor);

        public static StoredMapping From(UserMapping mapping) => new StoredMapping
        {
            PeerUserId = mapping.PeerUserId,
            PeerDisplayName = mapping.PeerDisplayName,
            Actor = mapping.Actor,
            At = mapping.At.ToUnixTimeSeconds(),
        };

        public UserMapping AsMapping(string pairingId, string localUserId) => new UserMapping(
            pairingId,
            localUserId,
            PeerUserId,
            PeerDisplayName ?? string.Empty,
            Actor,
            DateTimeOffset.FromUnixTimeSeconds(At));
    }
}
