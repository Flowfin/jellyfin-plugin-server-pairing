using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The one body shape this protocol has, read from the bytes that arrived.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> fixes it: every body that is not empty is a single JSON object with
/// no nesting, whose members are exact byte sequences matched case-sensitively. This type is
/// that sentence expressed in code and a difference between the two is a defect in this file.
/// <para>
/// IT IS A READER RATHER THAN A DESERIALISER, AND THAT IS THE POINT. A deserialiser is
/// configured, and every one of its defaults is a decision about the wire taken by whoever
/// configured it: an unknown member ignored, a member matched case-insensitively, a comment
/// skipped, a trailing comma allowed, a string coerced to a number. The document refuses all of
/// those by name, so the shape is read here with <see cref="Utf8JsonReader"/> at its strictest
/// settings and every refusal is a line somebody can point at.
/// </para>
/// <para>
/// What it refuses, each because the document says so: a body that is not one object, a member
/// carrying an object or an array, a member carrying <c>null</c>, a member carrying a boolean -
/// which no member of this protocol is - the same member twice, a member name written with an
/// escape rather than as the bytes the table names, and anything at all after the object ends.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO IS KNOW WHICH MEMBERS A MESSAGE HAS. That is the message's own shape and
/// lives with the message, because the member table is per body rather than per protocol. This
/// type answers what was written; <see cref="HelloRequestBody"/> and
/// <see cref="ConfirmRequestBody"/> answer whether that is the body they are.
/// </para>
/// </remarks>
public sealed class BodyObject
{
    private readonly Dictionary<string, BodyValue> _members;

    private BodyObject(Dictionary<string, BodyValue> members)
    {
        _members = members;
    }

    /// <summary>
    /// Gets how many members the object carried.
    /// </summary>
    /// <remarks>
    /// A shape reads this against the count its table fixes, which is what refuses an unknown
    /// member without either the shape or this type holding a list of the names it does not
    /// expect. Every member the table names is required, so a body carrying the right count and
    /// every required name carries nothing else.
    /// </remarks>
    public int Count => _members.Count;

    /// <summary>
    /// Reads the members of a body.
    /// </summary>
    /// <param name="body">The body bytes, exactly as they arrived.</param>
    /// <param name="read">The members, where this returns true.</param>
    /// <returns>True where the bytes are one object of the shape the document fixes.</returns>
    /// <remarks>
    /// An empty body is not an object and is refused here. That is not this type deciding what
    /// an empty body means: <c>docs/protocol.md</c> says empty is zero bytes rather than
    /// <c>{}</c>, so a message whose table says empty never reaches this at all and one whose
    /// table names members and arrives with nothing has not sent them.
    /// </remarks>
    public static bool TryRead(ReadOnlySpan<byte> body, out BodyObject read)
    {
        read = new BodyObject(new Dictionary<string, BodyValue>(StringComparer.Ordinal));

        if (body.IsEmpty)
        {
            return false;
        }

        var members = new Dictionary<string, BodyValue>(StringComparer.Ordinal);

        try
        {
            if (!ReadInto(body, members))
            {
                return false;
            }
        }
        catch (JsonException)
        {
            // The bytes are not JSON at all. A caller that verified is told the body did not
            // parse, which is what the taxonomy calls malformed, and the exception goes no
            // further: a parse failure on a request path is an answer rather than a fault.
            return false;
        }

        read = new BodyObject(members);
        return true;
    }

    /// <summary>
    /// Whether a member is present and is text.
    /// </summary>
    /// <param name="name">The member name, as the table spells it.</param>
    /// <param name="text">The value, where this returns true.</param>
    /// <returns>True where the member is there and arrived as a JSON string.</returns>
    public bool TryText(string name, out string text)
    {
        text = string.Empty;

        if (!_members.TryGetValue(name, out var value) || value.Kind != BodyValueKind.Text)
        {
            return false;
        }

        text = value.Text;
        return true;
    }

    /// <summary>
    /// Whether a member is present, is a number, and is written as this protocol writes one.
    /// </summary>
    /// <param name="name">The member name, as the table spells it.</param>
    /// <param name="digitLimit">The most digits the field's limit allows.</param>
    /// <param name="digits">The value as it was written, where this returns true.</param>
    /// <returns>True where the member is there, arrived as a JSON number, and is inside its limit.</returns>
    /// <remarks>
    /// The digits are judged by <see cref="FieldShape.IsUnsignedInteger"/>, which is the same
    /// predicate the header fields are judged by, so a number with a sign, an exponent, a
    /// fractional part or more digits than its limit allows is refused here rather than reaching
    /// a conversion that would take some of them.
    /// </remarks>
    public bool TryDigits(string name, int digitLimit, out string digits)
    {
        digits = string.Empty;

        if (!_members.TryGetValue(name, out var value)
            || value.Kind != BodyValueKind.Number
            || !FieldShape.IsUnsignedInteger(value.Text, digitLimit))
        {
            return false;
        }

        digits = value.Text;
        return true;
    }

    /// <summary>
    /// Walks the bytes once, filling the members, and refuses everything the document refuses.
    /// </summary>
    /// <param name="body">The body bytes.</param>
    /// <param name="members">Where the members are put.</param>
    /// <returns>True where the walk finished on one whole object and nothing else.</returns>
    private static bool ReadInto(ReadOnlySpan<byte> body, Dictionary<string, BodyValue> members)
    {
        var reader = new Utf8JsonReader(body, isFinalBlock: true, state: default);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
        {
            return false;
        }

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            // A name written with an escape decodes to the same characters and is not the same
            // bytes, and the table says the members are exact byte sequences. Refusing the
            // escape is what keeps one spelling of a member name on the wire.
            if (reader.ValueIsEscaped)
            {
                return false;
            }

            var name = Encoding.UTF8.GetString(reader.ValueSpan);

            if (!reader.Read() || !TryValue(ref reader, out var value))
            {
                return false;
            }

            // The same member twice is refused rather than resolved by a rule about which copy
            // wins, because two implementations would need that rule to agree and the document
            // gives them none.
            if (!members.TryAdd(name, value))
            {
                return false;
            }
        }

        // The loop above stops on the first token that is not a member name. Exactly one token
        // may be there, which is the end of the one object, and the reader must then be at the
        // end of the bytes: anything after the object is a second value in a body the document
        // says carries one.
        return reader.TokenType == JsonTokenType.EndObject && !reader.Read();
    }

    /// <summary>
    /// Reads the value the reader is positioned on, where it is a kind this protocol has.
    /// </summary>
    /// <param name="reader">The reader, positioned on a value token.</param>
    /// <param name="value">The value, where this returns true.</param>
    /// <returns>True where the token is a string or a number.</returns>
    /// <remarks>
    /// Every other token is a refusal rather than a case to handle. An object or an array is the
    /// nesting the document forbids, <c>null</c> is refused by name, and a boolean is a type no
    /// member of this protocol carries, so admitting one would be admitting a member the table
    /// does not describe.
    /// </remarks>
    private static bool TryValue(ref Utf8JsonReader reader, out BodyValue value)
    {
        value = default;

        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                value = new BodyValue(BodyValueKind.Text, reader.GetString() ?? string.Empty);
                return true;

            case JsonTokenType.Number:
                value = new BodyValue(BodyValueKind.Number, Encoding.UTF8.GetString(reader.ValueSpan));
                return true;

            default:
                return false;
        }
    }
}
