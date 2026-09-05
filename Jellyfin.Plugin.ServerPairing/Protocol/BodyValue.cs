namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// One member's value, as it was written in the body.
/// </summary>
/// <remarks>
/// The text is the spelling that arrived rather than a converted value, so the same
/// <see cref="FieldShape"/> predicates that judge a header field judge a body member: a version
/// written with a leading zero, a padded number and a value past its digit limit are refused by
/// the limit rather than by whichever parser would have been reached first.
/// <para>
/// <see cref="Kind"/> travels with it because a member's JSON type is part of what
/// <c>docs/protocol.md</c> fixes. A number where the document says string, and a string where it
/// says number, are two implementations disagreeing about the wire, so they are refused where
/// the shape is read rather than coerced into agreeing.
/// </para>
/// </remarks>
/// <param name="Kind">Which JSON type the value arrived as.</param>
/// <param name="Text">The value, as it was written.</param>
public readonly record struct BodyValue(BodyValueKind Kind, string Text);
