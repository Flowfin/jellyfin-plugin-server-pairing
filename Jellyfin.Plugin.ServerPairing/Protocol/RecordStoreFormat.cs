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
/// wrote files before the number existed, and those files are on operators' disks. This store has
/// carried an envelope since its first commit, so a file carrying no format number was written by
/// something that is not this plugin. That is a damaged store rather than an old one, which is the
/// difference between the two refusals in <see cref="FilePairingRecordStore"/>.
/// </para>
/// <para>
/// THIS REMARK SAID THE STORE HAD NEVER SHIPPED AND USED THAT TO ARGUE THAT EVERY FILE BELOW THE
/// CURRENT NUMBER WAS DAMAGE. It has shipped, in both releases the tree carries a tag for, so a
/// build that raises this number can meet a file an older build wrote and that argument does not
/// survive the raise.
/// </para>
/// <para>
/// WHAT IS NOT CLAIMED IS THAT SUCH A FILE EXISTS ON A DISK. Nothing on a server wrote a record
/// until the enrolment producer landed, so a shipped build could reach this store and never make a
/// file. The rung below is written because the number moved on a store that shipped, not because a
/// format 1 file was found anywhere, and no run on a server has been made to look for one.
/// </para>
/// <para>
/// A rung is owed the moment this number moves, and one is written for every step of the ladder.
/// <see cref="Rung"/> fails rather than leaving a document where it was, so a format added without
/// its migration is refused rather than silently half-read.
/// </para>
/// </remarks>
public static class RecordStoreFormat
{
    /// <summary>
    /// The format this build writes and reads.
    /// </summary>
    /// <remarks>
    /// Format 2 is format 1 with a peer address on each record. It moved because
    /// <see cref="PairingRecord.PeerAddress"/> arrived, and a store whose records gained a member
    /// is a document that has moved whether or not the member is optional to read.
    /// </remarks>
    public const int Current = 2;

    /// <summary>
    /// The first format this store ever wrote, which is where the ladder starts.
    /// </summary>
    /// <remarks>
    /// Named rather than written as a literal in two places, because the store refuses anything
    /// below it as damage and the ladder starts at it, and those two have to be the same number.
    /// </remarks>
    public const int Earliest = 1;

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
    /// THIS REMARK SAID NOTHING CALLS THIS. <see cref="FilePairingRecordStore"/> calls it for a
    /// document declaring a format between <see cref="Earliest"/> and <see cref="Current"/>, which
    /// is a population that came into existence when the number moved to 2.
    /// <para>
    /// The document handed back is a new one and the one handed in is not touched, so a caller
    /// that kept a reference to what it parsed is holding the file as it was read rather than a
    /// half-walked copy of it.
    /// </para>
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

    /// <summary>
    /// One step of the ladder.
    /// </summary>
    /// <param name="from">The format the document declares.</param>
    /// <param name="document">The document in that format.</param>
    /// <returns>The document one rung up.</returns>
    /// <exception cref="InvalidOperationException">There is no rung away from that format.</exception>
    /// <remarks>
    /// The rung from 1 to 2 moves the number and touches no record, and that is the whole of the
    /// migration rather than a step left unwritten. Format 2 added an address member a record may
    /// be without: a record written by format 1 was written by a build that had no address to
    /// write, so the honest value for it is the absent one, and inventing a value here would put
    /// an address on a record nobody ever entered one for.
    /// <para>
    /// The records are cloned out of the document rather than moved, because a node belongs to one
    /// document and re-parenting the member would empty the one the caller handed in.
    /// </para>
    /// </remarks>
    private static JsonObject Rung(int from, JsonObject document)
    {
        if (from == Earliest)
        {
            return new JsonObject
            {
                [FormatMember] = JsonValue.Create(from + 1),
                [RecordsMember] = Records(document).DeepClone(),
            };
        }

        throw new InvalidOperationException(string.Format(
            CultureInfo.InvariantCulture,
            "There is no migration away from pairing record store format {0}, so a file in it cannot be carried up to format {1}.",
            from,
            Current));
    }
}
