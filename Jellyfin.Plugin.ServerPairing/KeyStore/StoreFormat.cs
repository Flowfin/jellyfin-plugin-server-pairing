using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.ServerPairing.KeyStore;

/// <summary>
/// The number the key store's file carries saying what shape it is in, and the ladder that
/// carries an older file up to the shape this build reads.
/// </summary>
/// <remarks>
/// This exists before there is much to migrate, which is the whole of issue #55. The first
/// version to ship is the only one that gets to define a format without migrating one, and a
/// file already written under no envelope is the case that costs the most to repair later.
/// <para>
/// A migration is a function from one format to the next, and the ladder is walked one rung at
/// a time in order rather than jumped. Two rungs written as one jump have to be rewritten
/// every time a rung is added below them, and a file three formats old then travels a path no
/// fixture ever took.
/// </para>
/// <para>
/// WHAT A MIGRATION MAY NOT DO is lose a member it does not recognise. Every step here works
/// on the parsed document rather than on this plugin's own types, so a member the step does
/// not name travels through it untouched. Deserialising into a type and reserialising would
/// drop exactly the members a newer build added, which is the failure
/// <see cref="StoreFormatRefusedException"/> exists to refuse from the other direction.
/// </para>
/// <para>
/// FORMAT 0 IS NOT A FORMAT THAT WAS DESIGNED. It is what this store wrote before this number
/// existed: a bare map of pairing identifier to the three fields held per pairing, with no
/// envelope around it. It is named rather than special-cased so that a file already on an
/// operator's disk has a rung to start from.
/// </para>
/// </remarks>
public static class StoreFormat
{
    /// <summary>
    /// The format this build writes and reads.
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// The format a file that carries no format number is in.
    /// </summary>
    /// <remarks>
    /// A file written before the number existed. Nothing writes this any more; it is read so
    /// that a store already on an operator's disk has somewhere to be migrated from.
    /// </remarks>
    public const int Unversioned = 0;

    /// <summary>
    /// The member the format number is held in.
    /// </summary>
    public const string FormatMember = "format";

    /// <summary>
    /// The member the pairings are held in, from format 1 onwards.
    /// </summary>
    public const string PairingsMember = "pairings";

    /// <summary>
    /// The suffix a copy of the pre-migration file carries, saying which format it is in.
    /// </summary>
    /// <param name="format">The format the copy is in.</param>
    /// <returns>The suffix.</returns>
    /// <remarks>
    /// The format is in the name rather than a timestamp, so an operator looking at the
    /// directory can see what the file beside the store is without opening it, and so a second
    /// migration away from the same format does not leave two files nobody can tell apart.
    /// </remarks>
    public static string BackupSuffix(int format) =>
        "." + FormatMember + "-" + format.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// What format a parsed store document is in.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The format it declares, or <see cref="Unversioned"/> where it declares none.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <remarks>
    /// A document carrying no <see cref="FormatMember"/>, or one whose <see cref="FormatMember"/>
    /// is not a number, is format 0. The second half matters more than it looks: in format 0
    /// every member of the document is a pairing identifier, so a store holding a pairing whose
    /// identifier happened to be the word this member is named would otherwise be read as
    /// carrying a format. Its value is an object rather than a number, which is what tells the
    /// two apart.
    /// </remarks>
    public static int Read(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.TryGetPropertyValue(FormatMember, out var declared) || declared is null)
        {
            return Unversioned;
        }

        return declared.GetValueKind() == JsonValueKind.Number ? declared.GetValue<int>() : Unversioned;
    }

    /// <summary>
    /// Walks a document up the ladder to <see cref="Current"/>, one rung at a time.
    /// </summary>
    /// <param name="document">The document, in whatever format it declares.</param>
    /// <param name="file">The file it was read from, for the refusal to name.</param>
    /// <returns>The document in <see cref="Current"/>.</returns>
    /// <exception cref="ArgumentNullException">The document or the file is null.</exception>
    /// <exception cref="StoreFormatRefusedException">
    /// The document declares a format newer than this build understands.
    /// </exception>
    public static JsonObject Migrate(JsonObject document, string file)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(file);

        var format = Read(document);

        if (format > Current)
        {
            throw new StoreFormatRefusedException(format, Current, file);
        }

        var carried = document;

        for (var rung = format; rung < Current; rung++)
        {
            carried = Rung(rung, carried);
        }

        return carried;
    }

    /// <summary>
    /// The pairings a document in <see cref="Current"/> holds.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The pairings, which is an empty object where the member is absent.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static JsonObject Pairings(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.TryGetPropertyValue(PairingsMember, out var held) && held is JsonObject pairings
            ? pairings
            : new JsonObject();
    }

    /// <summary>
    /// Puts <see cref="Current"/>'s envelope around the pairings given.
    /// </summary>
    /// <param name="pairings">The pairings.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">The pairings are null.</exception>
    public static JsonObject Wrap(JsonNode pairings)
    {
        ArgumentNullException.ThrowIfNull(pairings);

        return new JsonObject
        {
            [FormatMember] = JsonValue.Create(Current),
            [PairingsMember] = pairings,
        };
    }

    /// <summary>
    /// The one rung that carries a document away from the format given.
    /// </summary>
    /// <param name="from">The format the document is in.</param>
    /// <param name="document">The document.</param>
    /// <returns>The document one format higher.</returns>
    /// <exception cref="InvalidOperationException">There is no rung away from that format.</exception>
    /// <remarks>
    /// A switch rather than a table held in a static field, because the assembly carries no
    /// static state outside the plugin instance and <c>StaticStateTests</c> refuses one. The
    /// default arm is what a format added below <see cref="Current"/> without its migration
    /// meets, and it fails rather than silently leaving the document where it was.
    /// </remarks>
    private static JsonObject Rung(int from, JsonObject document) => from switch
    {
        Unversioned => FromUnversionedToOne(document),
        _ => throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "There is no migration away from key store format {0}, so a file in it cannot be carried up to format {1}.",
            from,
            Current)),
    };

    /// <summary>
    /// Format 0 to format 1: the bare map becomes the value of the pairings member, and the
    /// format number is written beside it.
    /// </summary>
    /// <param name="document">The document in format 0.</param>
    /// <returns>The document in format 1.</returns>
    /// <remarks>
    /// Nothing per pairing changes. Every member of the old document is carried across as it
    /// was, so a field a newer build wrote inside a pairing survives a rung that never named
    /// it.
    /// </remarks>
    private static JsonObject FromUnversionedToOne(JsonObject document)
    {
        var pairings = new JsonObject();

        foreach (var member in new List<KeyValuePair<string, JsonNode?>>(document))
        {
            // Detached first. A node belongs to one parent, and adding one that still has
            // another is refused by the runtime rather than reparented.
            document.Remove(member.Key);

            pairings[member.Key] = member.Value;
        }

        return Wrap(pairings);
    }
}
