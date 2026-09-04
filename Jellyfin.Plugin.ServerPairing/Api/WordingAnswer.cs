using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Wording;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The sentences an operator reads on the page, served out of the registers that hold them.
/// </summary>
/// <remarks>
/// Every sentence an operator reads lives in exactly one place, which is the two registers in
/// the <c>Wording</c> namespace. The operator guide is held equal to them by a case, and a page
/// that pasted one into its own markup would be a second copy that drifts, which is why the
/// suite refuses markup carrying a sentence at all. So the page reads them here, at the moment
/// it is shown, and what it renders is the constant as it stands rather than a copy made when
/// the page was written.
/// <para>
/// The answer is built by reflection over the registers' public constants rather than by a
/// list written here, so a sentence added to a register is served without this type moving,
/// and a sentence this type forgot to name cannot exist. Each register is one member of the
/// answer and each sentence sits under the name of its constant, which is the name the page
/// asks for and the name the guide's cases already read.
/// </para>
/// <para>
/// Nothing here is a secret and nothing here is peer-controlled. The registers are this
/// plugin's own text, so serving them discloses nothing a reader of the source does not have.
/// </para>
/// </remarks>
public static class WordingAnswer
{
    /// <summary>
    /// The member the ceremony register is served under.
    /// </summary>
    public const string CeremonyMember = "ceremony";

    /// <summary>
    /// The member the destructive-action register is served under.
    /// </summary>
    public const string DestructiveMember = "destructive";

    /// <summary>
    /// Both registers, each sentence under the name of its constant.
    /// </summary>
    /// <returns>The register name to the sentences it holds, in ordinal order of name.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Registers()
        => new SortedDictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal)
        {
            [CeremonyMember] = Sentences(typeof(CeremonyWording)),
            [DestructiveMember] = Sentences(typeof(DestructiveWording)),
        };

    /// <summary>
    /// The body the action answers with.
    /// </summary>
    /// <returns>Both registers as one JSON object.</returns>
    public static string Body() => JsonSerializer.Serialize(Registers());

    /// <summary>
    /// Every public string constant a register declares, under its name.
    /// </summary>
    /// <param name="register">The register.</param>
    /// <returns>The constant's name to its value, in ordinal order of name.</returns>
    private static SortedDictionary<string, string> Sentences(Type register)
    {
        var sentences = new SortedDictionary<string, string>(StringComparer.Ordinal);

        var literals = register
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string));

        foreach (var field in literals)
        {
            if (field.GetRawConstantValue() is string sentence)
            {
                sentences[field.Name] = sentence;
            }
        }

        return sentences;
    }
}
