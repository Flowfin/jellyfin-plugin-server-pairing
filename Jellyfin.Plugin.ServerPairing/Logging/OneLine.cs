using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.ServerPairing.Logging;

/// <summary>
/// Puts a value on one line before it reaches a log entry.
/// </summary>
/// <remarks>
/// A log entry is one line and a reader treats it as one. A value that carries a line break
/// therefore writes as many entries as it holds breaks, and every line after the first is one
/// the reader attributes to this plugin while nothing here composed it. Where the value came in
/// on a request, the caller chose those lines: they can name a pairing that was never revoked,
/// an administrator who did nothing, or a level this plugin never writes at.
/// <para>
/// WHAT THIS IS FOR IS THE READER OF THE LOG RATHER THAN THE STRUCTURE OF IT. A structured sink
/// keeps the value in its own field, where a break is data and harms nothing. The Jellyfin
/// default is a text file an operator opens and pastes into a public thread, which is what
/// <c>docs/logging.md</c> takes as the threat model, and in that file there is no field - there
/// is a line.
/// </para>
/// <para>
/// THE SET IS WIDER THAN CARRIAGE RETURN AND LINE FEED, AND EACH ADDITION EARNS ITS PLACE.
/// <c>Cc</c> holds the two obvious breaks and also the escape character, which drives a
/// terminal rather than being printed by it. <c>Zl</c> and <c>Zp</c> are line and paragraph
/// separators, which a viewer may break on where a plain reader does not. <c>Cf</c> holds the
/// bidirectional overrides of CVE-2021-42574: they reorder what a reader sees without changing
/// what is stored, so a log line can be made to read as its own opposite. That last one is the
/// same attack <c>.github/workflows/unicode-guard.yml</c> refuses in tracked source, arriving
/// at runtime through a value instead of at review time through a file.
/// </para>
/// <para>
/// The replacement is a visible character rather than a deletion, because a value that was
/// tampered with should not read as a value that was clean. An identifier this plugin
/// recognises holds none of these characters, so nothing an operator meets in normal running
/// passes through here changed.
/// </para>
/// <para>
/// This is applied at the log call rather than at the edge on purpose. Refusing the value at
/// the edge is a separate question with a separate answer per endpoint, and it would leave
/// every future call site trusting that somebody upstream had asked it. What is guaranteed
/// here is narrower and holds whatever the edge does: what this plugin writes to a log is one
/// line per entry.
/// </para>
/// </remarks>
public static class OneLine
{
    /// <summary>
    /// What stands in the entry where the value carried a character that would break the line.
    /// </summary>
    public const string Replacement = "\uFFFD";

    /// <summary>
    /// The characters a log entry may not carry, named by Unicode general category.
    /// </summary>
    /// <remarks>
    /// A category rather than a list, because a list is a thing that goes stale against a
    /// standard that adds codepoints. <c>OneLineTests</c> asserts the members one by one, so a
    /// category widened here is still proved by characters somebody argued for.
    /// </remarks>
    private const string Breaking = @"[\p{Cc}\p{Cf}\p{Zl}\p{Zp}]";

    /// <summary>
    /// The value as a log entry may carry it.
    /// </summary>
    /// <param name="value">The value, as it arrived.</param>
    /// <returns>
    /// The value with every line-breaking, terminal-driving and text-reordering character
    /// replaced by <see cref="Replacement"/>, or the empty string where the value is null.
    /// </returns>
    /// <remarks>
    /// The static overload rather than a compiled instance held in a field. A field would be
    /// static state in the plugin assembly, which <c>StaticStateTests</c> refuses outright and
    /// which the source generator's own output is refused for as well: it emits a class holding
    /// the compiled expression. The framework's own expression cache is what makes the static
    /// call cheap enough for a path that runs once per audit entry, and it lives outside this
    /// assembly rather than in it.
    /// </remarks>
    public static string Of(string? value) =>
        value is null ? string.Empty : Regex.Replace(value, Breaking, Replacement);
}
