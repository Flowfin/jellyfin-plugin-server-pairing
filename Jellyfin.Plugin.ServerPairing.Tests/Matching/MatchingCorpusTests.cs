using System;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Matching;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Matching;

/// <summary>
/// Assertions about the corpus rather than about the matcher.
/// <para>
/// A corpus that accumulates from bugs covers the mistakes somebody already made. The
/// guards here are what stop it going back to that: a provider cannot join the matched
/// list without rows that exercise it, and a row cannot stop being run by being given a
/// name another row already has.
/// </para>
/// </summary>
public class MatchingCorpusTests
{
    /// <summary>
    /// A new provider needs a row where it carries a match. Without this, adding one to
    /// the matched list is a change to what the plugin matches on with no case behind it,
    /// and the suite stays green while the new provider is never exercised.
    /// </summary>
    [Fact]
    public void EveryMatchedProviderCarriesAMatchInSomeRow()
    {
        var uncovered = MatchedProviders.InPrecedenceOrder
            .Where(provider => !MatchingCorpus.Rows.Any(
                row => string.Equals(row.ExpectedMatchedOn, provider, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        Assert.Equal(Array.Empty<string>(), uncovered);
    }

    /// <summary>
    /// A new provider also needs a row where its presence does not produce a match. A
    /// provider covered only by a row that matches is a provider whose refusals nobody
    /// has written down, and the refusals are the half that goes wrong quietly.
    /// </summary>
    [Fact]
    public void EveryMatchedProviderAppearsInSomeRowThatDoesNotMatch()
    {
        var uncovered = MatchedProviders.InPrecedenceOrder
            .Where(provider => !MatchingCorpus.Rows.Any(
                row => row.Expected != MatchOutcome.Matched && MatchingCorpus.ProviderNames(row).Contains(provider)))
            .ToArray();

        Assert.Equal(Array.Empty<string>(), uncovered);
    }

    /// <summary>
    /// Rows are found by their situation name, so two rows sharing one would make the
    /// lookup ambiguous and leave one of them unrun.
    /// </summary>
    [Fact]
    public void EverySituationNameIsDistinct()
    {
        var names = MatchingCorpus.Rows.Select(row => row.Situation).ToArray();

        Assert.Equal(names.Length, names.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>
    /// The theory is driven by the situation names, so a row that the name list does not
    /// mention is a row nothing executes.
    /// </summary>
    [Fact]
    public void EveryRowIsHandedToTheTheory()
    {
        Assert.Equal(MatchingCorpus.Rows.Count, MatchingCorpus.Situations().Count());
    }

    /// <summary>
    /// A situation is a sentence about a case, and a row that names itself after its
    /// values says nothing when it fails. This is the cheapest check that the naming
    /// convention is being kept.
    /// </summary>
    [Fact]
    public void EveryRowNamesItsSituation()
    {
        Assert.All(MatchingCorpus.Rows, row => Assert.False(string.IsNullOrWhiteSpace(row.Situation)));
    }
}
