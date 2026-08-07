using System;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Matching;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Matching;

/// <summary>
/// Runs the corpus, and holds the assertions that are about the matcher as a whole rather
/// than about one situation.
/// </summary>
public class ItemMatcherTests
{
    /// <summary>
    /// Every row of the corpus, executed. The theory is parameterised by the situation
    /// name so a failure names the case rather than a position in a list.
    /// </summary>
    /// <param name="situation">The situation name of the row to run.</param>
    [Theory]
    [MemberData(nameof(MatchingCorpus.Situations), MemberType = typeof(MatchingCorpus))]
    public void TheCorpusMatchesWhatTheDocumentSays(string situation)
    {
        var row = MatchingCorpus.Row(situation);

        var match = ItemMatcher.Match(row.PeerItem, row.LocalCandidates);

        Assert.Equal(row.Expected, match.Outcome);
        Assert.Equal(row.ExpectedMatches, match.Matches);

        if (row.ExpectedMatchedOn is not null)
        {
            Assert.Equal(row.ExpectedMatchedOn, match.MatchedOn);
        }
    }

    /// <summary>
    /// The matched list is built from the host's own enumeration. Asserting membership
    /// here means a provider name that stops existing on the host is a failure with a
    /// name in it, rather than a matcher that silently stops matching on that provider.
    /// </summary>
    [Fact]
    public void MatchedProvidersAreAllNamesTheHostKnows()
    {
        var known = Enum.GetNames<MetadataProvider>();

        Assert.All(MatchedProviders.InPrecedenceOrder, provider => Assert.Contains(provider, known));
    }

    /// <summary>
    /// Precedence is an order over identifiers, so the list has to have one. A duplicate
    /// entry would make the order undecidable for the provider that appears twice.
    /// </summary>
    [Fact]
    public void MatchedProvidersAreDistinct()
    {
        Assert.Equal(
            MatchedProviders.InPrecedenceOrder.Count,
            MatchedProviders.InPrecedenceOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    /// <summary>
    /// The matcher is a pure function over its two arguments. This asserts the shape of
    /// that claim which is cheapest to break by accident: that the type carries no state
    /// between calls and takes no service to construct.
    /// </summary>
    [Fact]
    public void TheMatcherIsStaticAndHoldsNoState()
    {
        var type = typeof(ItemMatcher);

        Assert.True(type.IsAbstract && type.IsSealed);
        Assert.Empty(type.GetFields(
            System.Reflection.BindingFlags.Static
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic
            | System.Reflection.BindingFlags.DeclaredOnly));
    }
}
