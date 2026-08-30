using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Wording;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Wording;

/// <summary>
/// The confirmation ceremony is the one mechanism in this design that is a person, so its
/// wording is judged rather than left to whoever writes the markup.
/// </summary>
public class CeremonyWordingTests
{
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// Words that claim more than this design proves. Every one of them is reached for by
    /// somebody trying to reassure an operator, and each turns a statement about what was
    /// compared into a promise about what is true.
    /// </summary>
    private static readonly string[] Overstating =
    {
        "secure",
        "safe",
        "guarantee",
        "impossible",
        "unbreakable",
        "certified",
        "military",
        "bank-level",
        "100%",
        "totally",
        "completely",
    };

    /// <summary>
    /// Words that mean something exact to whoever wrote the protocol and nothing at all to
    /// somebody running a server for a household.
    /// </summary>
    private static readonly string[] Jargon =
    {
        "SHA-256",
        "ECDiffieHellman",
        "SubjectPublicKeyInfo",
        "P-256",
        "asymmetric",
        "digest",
        "entropy",
        "nonce",
        "canonical form",
        "HMAC",
        "cipher",
        "preimage",
    };

    /// <summary>
    /// The wording for a mismatch says stop, and says nothing that reads as trying again. A
    /// second attempt against the same peer is the one response that turns a detected attack
    /// into an accepted one.
    /// </summary>
    [Fact]
    public void TheMismatchWordingSaysStopRatherThanRetry()
    {
        Assert.StartsWith("Stop.", CeremonyWording.WhenTheyDiffer, StringComparison.Ordinal);

        foreach (var retry in new[] { "try again", "retry", "once more", "start over" })
        {
            Assert.DoesNotContain(retry, CeremonyWording.WhenTheyDiffer, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Every destructive action names whether it can be undone. The answer is no in each case,
    /// and an operator is entitled to it before pressing rather than afterwards.
    /// </summary>
    [Fact]
    public void EveryDestructiveActionSaysWhetherItCanBeUndone()
    {
        foreach (var (name, sentence) in Sentences(typeof(DestructiveWording)))
        {
            Assert.Contains("undo", sentence, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Every destructive action names what goes, rather than asking for a confirmation of
    /// something the operator has to work out for themselves.
    /// </summary>
    [Fact]
    public void EveryDestructiveActionIsMoreThanOneSentence()
    {
        foreach (var (name, sentence) in Sentences(typeof(DestructiveWording)))
        {
            Assert.True(
                sentence.Count(c => c == '.') >= 2,
                $"{name} has to say what goes as well as whether it can be undone.");
        }
    }

    /// <summary>
    /// Nothing overstates what has been proved, in either register.
    /// </summary>
    [Fact]
    public void NothingOverstatesWhatHasBeenProved()
    {
        Assert.Equal(Array.Empty<string>(), Occurrences(Overstating));
    }

    /// <summary>
    /// Nothing is written in the protocol's vocabulary. An operator reading this has not read
    /// the specification and is not going to.
    /// </summary>
    [Fact]
    public void NothingIsWrittenInProtocolVocabulary()
    {
        Assert.Equal(Array.Empty<string>(), Occurrences(Jargon));
    }

    /// <summary>
    /// The comparison instruction names the grouping the value is rendered in, which
    /// <c>docs/crypto.md</c> pins as part of the construction rather than as presentation. A
    /// fingerprint read as one run of characters is one people compare badly.
    /// </summary>
    [Fact]
    public void TheComparisonNamesTheEightGroups()
    {
        Assert.Contains("eight groups", CeremonyWording.HowToCompare, StringComparison.Ordinal);
        Assert.Contains("all eight", CeremonyWording.ConfirmOnlyIfTheyMatch, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sentence saying that clicking through establishes nothing is present and says it in
    /// those terms. It is the sentence the ceremony exists to deliver and the easiest one to
    /// soften in an edit that means well.
    /// </summary>
    [Fact]
    public void TheCostOfConfirmingWithoutComparingIsStated()
    {
        Assert.Contains("establishes nothing", CeremonyWording.TheComparisonIsTheTrust, StringComparison.Ordinal);
    }

    /// <summary>
    /// No sentence is empty, none is a duplicate of another, and none is assembled from a
    /// placeholder, because a sentence with a hole in it is a sentence nobody reviewed.
    /// </summary>
    [Fact]
    public void EverySentenceIsWholeAndDistinct()
    {
        var all = AllSentences().ToArray();

        Assert.NotEmpty(all);
        Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(s.Sentence)));
        Assert.All(all, s => Assert.DoesNotContain("{", s.Sentence, StringComparison.Ordinal));
        Assert.Equal(all.Length, all.Select(s => s.Sentence).Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The walk finds both registers. Every assertion above is over what it returns, so a walk
    /// that found nothing would pass all of them while judging no sentence at all.
    /// </summary>
    [Fact]
    public void TheWalkFindsBothRegisters()
    {
        Assert.NotEmpty(Sentences(typeof(CeremonyWording)));
        Assert.NotEmpty(Sentences(typeof(DestructiveWording)));
        Assert.Contains(AllSentences(), s => s.Name == nameof(CeremonyWording.WhenTheyDiffer));
        Assert.Contains(AllSentences(), s => s.Name == nameof(DestructiveWording.Revoke));
    }

    /// <summary>
    /// Both vocabularies have entries, so an edit that empties one turns a guard off in a way
    /// that stays green.
    /// </summary>
    [Fact]
    public void BothVocabulariesHaveEntries()
    {
        Assert.NotEmpty(Overstating);
        Assert.NotEmpty(Jargon);
    }

    /// <summary>
    /// No markup in this plugin carries one of these sentences. This is what keeps the single
    /// copy single: a page that pastes a sentence into its own markup has made a second copy
    /// that the review above does not read and that drifts from the first.
    /// </summary>
    [Fact]
    public void NoMarkupInThePluginCarriesASentence()
    {
        var pages = Directory
            .EnumerateFiles(PluginDirectory(), "*.html", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(pages);

        var copies = new List<string>();

        foreach (var page in pages)
        {
            var markup = File.ReadAllText(page);

            copies.AddRange(
                AllSentences()
                    .Where(s => markup.Contains(s.Sentence, StringComparison.Ordinal))
                    .Select(s => Path.GetFileName(page) + ": " + s.Name));
        }

        Assert.Equal(Array.Empty<string>(), copies.OrderBy(c => c, StringComparer.Ordinal).ToArray());
    }

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

    /// <summary>
    /// Every sentence holding one of the given words, as the sentence's name and the word.
    /// </summary>
    /// <param name="vocabulary">The words to look for.</param>
    /// <returns>One entry per occurrence.</returns>
    private static string[] Occurrences(IReadOnlyCollection<string> vocabulary)
        => AllSentences()
            .SelectMany(
                s => vocabulary
                    .Where(word => s.Sentence.Contains(word, StringComparison.OrdinalIgnoreCase))
                    .Select(word => s.Name + ": " + word))
            .OrderBy(o => o, StringComparer.Ordinal)
            .ToArray();

    private static string PluginDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Join(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the markup scan has no root to read.")
            : Path.Join(directory.FullName, "Jellyfin.Plugin.ServerPairing");
    }
}
