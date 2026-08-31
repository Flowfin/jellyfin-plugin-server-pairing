using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Wording;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Wording;

/// <summary>
/// The operator guide walks a person through the ceremony, so the words it puts in front of
/// them have to be the words the surface will. These cases hold the two equal.
/// </summary>
/// <remarks>
/// The guide is asked for a comparison step whose wording matches what an operator reads on
/// the screen, checkable rather than trusted. A grep of the guide against the markup answers
/// nothing, because <c>CeremonyWordingTests.NoMarkupInThePluginCarriesASentence</c> refuses
/// any page in this plugin from carrying one of these sentences: the page reads the constant
/// rather than containing it. So the artefact holding the words is the wording register, and
/// the comparison that carries the obligation is between the register and
/// <c>docs/operator-guide.md</c>.
/// <para>
/// The direction is total. Every sentence in both registers has to be quoted in the guide,
/// rather than a hand-kept list of the ones somebody thought were about the walkthrough,
/// because a list like that is one more place a new sentence can be forgotten.
/// </para>
/// </remarks>
public class OperatorGuideWordingTests
{
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    private const string GuidePath = "docs/operator-guide.md";

    /// <summary>
    /// Every sentence an operator reads is quoted in the guide, as a block quote of its own and
    /// byte for byte. A sentence edited in one place and not the other is a red suite rather
    /// than two documents that disagree about what the screen says.
    /// </summary>
    [Fact]
    public void EverySentenceAnOperatorReadsIsQuotedHere()
    {
        var quoted = BlockQuotes();
        var missing = AllSentences()
            .Where(s => !quoted.Contains("> " + s.Sentence, StringComparer.Ordinal))
            .Select(s => s.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), missing);
    }

    /// <summary>
    /// The guide quotes no sentence that is not in a register. A block quote holding words an
    /// operator will never see is the same drift read from the other end, and it is the one a
    /// comparison in a single direction lets through.
    /// </summary>
    [Fact]
    public void TheGuideQuotesNoSentenceTheSurfaceDoesNotHold()
    {
        var known = AllSentences().Select(s => "> " + s.Sentence).ToHashSet(StringComparer.Ordinal);
        var strangers = BlockQuotes()
            .Where(q => !known.Contains(q))
            .OrderBy(q => q, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), strangers);
    }

    /// <summary>
    /// The guide is where it is named and it has block quotes in it. Both assertions above are
    /// over what the reader returns, so a guide that was not found, or one carrying no quote at
    /// all, would pass one of them while comparing nothing.
    /// </summary>
    [Fact]
    public void TheGuideIsReadAndCarriesQuotes()
    {
        Assert.True(File.Exists(GuideFile()), GuideFile() + " does not exist, so nothing was compared.");
        Assert.NotEmpty(BlockQuotes());
        Assert.NotEmpty(AllSentences());
    }

    private static string GuideFile()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Join(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the guide has no root to be read from.")
            : Path.Join(directory.FullName, GuidePath.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// Every block-quoted line of the guide, with the trailing carriage return a checkout may
    /// have written removed, so the comparison is over the words rather than over the line
    /// ending this machine happens to use.
    /// </summary>
    /// <returns>One entry per block-quoted line, in the order they appear.</returns>
    private static string[] BlockQuotes()
        => File.ReadAllLines(GuideFile())
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("> ", StringComparison.Ordinal))
            .ToArray();

    private static (string Name, string Sentence)[] Sentences(Type holder)
        => holder
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .OrderBy(f => f.Name, StringComparer.Ordinal)
            .ToArray();

    private static (string Name, string Sentence)[] AllSentences()
        => Sentences(typeof(CeremonyWording))
            .Concat(Sentences(typeof(DestructiveWording)))
            .ToArray();
}
