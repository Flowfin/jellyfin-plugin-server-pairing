using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The number the pairing record store's file carries saying what shape it is in.
/// </summary>
/// <remarks>
/// This is the key store's <c>StoreFormat</c> read one file over, and it is a second type rather
/// than a second caller of that one ON PURPOSE. The two files are written by the same plugin and
/// are not the same document: a rung added to the key store's ladder would otherwise make this
/// store write a number it has no rung for, and a file carrying a format nothing can migrate is
/// the failure the number exists to prevent.
/// <para>
/// THERE IS NO FORMAT 0 HERE AND THAT IS NOT AN OMISSION. The key store carries one because it
/// wrote files before the number existed, and those files are on operators' disks. This store
/// has never shipped, so every file it will ever meet was written with an envelope, and a file
/// carrying no format number was written by something that is not this plugin. That is a damaged
/// store rather than an old one, which is the difference between the two refusals below.
/// </para>
/// <para>
/// A rung is owed the moment this number moves. There is no ladder here because there is nothing
/// to climb yet, and the switch below fails rather than leaving a document where it was, so a
/// format added without its migration is refused rather than silently half-read.
/// </para>
/// </remarks>
public static class RecordStoreFormat
{
    /// <summary>
    /// The format this build writes and reads.
    /// </summary>
    public const int Current = 1;

    /// <summary>
    /// The member the format number is held in.
    /// </summary>
    public const string FormatMember = "format";

    /// <summary>
    /// The member the records are held in.
    /// </summary>
    public const string RecordsMember = "records";

    /// <summary>
    /// What format a parsed document declares.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The format, or zero where it declares none.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <remarks>
    /// Zero is the answer for a document with no number and for one whose number is not a number,
    /// and both are refused by the store as damaged rather than migrated. The value is read as a
    /// number rather than as whatever is there, so a file whose <see cref="FormatMember"/> holds
    /// an object cannot be read as declaring a format.
    /// </remarks>
    public static int Read(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!document.TryGetPropertyValue(FormatMember, out var declared) || declared is null)
        {
            return 0;
        }

        return declared.GetValueKind() == JsonValueKind.Number ? declared.GetValue<int>() : 0;
    }

    /// <summary>
    /// Walks a document up to <see cref="Current"/>.
    /// </summary>
    /// <param name="document">The document, in the format it declares.</param>
    /// <returns>The document in <see cref="Current"/>.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    /// <exception cref="InvalidOperationException">There is no rung away from the format it declares.</exception>
    /// <remarks>
    /// Nothing calls this today, because the only format a store may be in is the current one and
    /// every other value is refused before a migration could be reached. It is here so that the
    /// rung a second format needs has a place to be written, and so that the failure of adding a
    /// format without one is a refusal rather than a document quietly left where it was.
    /// </remarks>
    public static JsonObject Migrate(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var format = Read(document);
        var carried = document;

        for (var rung = format; rung < Current; rung++)
        {
            carried = Rung(rung, carried);
        }

        return carried;
    }

    /// <summary>
    /// The records a document holds.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <returns>The records, which is an empty object where the member is absent.</returns>
    /// <exception cref="ArgumentNullException">The document is null.</exception>
    public static JsonObject Records(JsonObject document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.TryGetPropertyValue(RecordsMember, out var held) && held is JsonObject records
            ? records
            : new JsonObject();
    }

    /// <summary>
    /// Puts <see cref="Current"/>'s envelope around the records given.
    /// </summary>
    /// <param name="records">The records.</param>
    /// <returns>The document.</returns>
    /// <exception cref="ArgumentNullException">The records are null.</exception>
    public static JsonObject Wrap(JsonNode records)
    {
        ArgumentNullException.ThrowIfNull(records);

        return new JsonObject
        {
            [FormatMember] = JsonValue.Create(Current),
            [RecordsMember] = records,
        };
    }

    private static JsonObject Rung(int from, JsonObject document)
    {
        _ = document;

        throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "There is no migration away from pairing record store format {0}, so a file in it cannot be carried up to format {1}.",
            from,
            Current));
    }
}
