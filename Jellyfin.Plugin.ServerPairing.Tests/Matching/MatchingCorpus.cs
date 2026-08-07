using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Matching;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.ServerPairing.Tests.Matching;

/// <summary>
/// The situations the matcher has to get right, held as data.
/// <para>
/// The expected outcome of every row is read out of <c>docs/matching.md</c> rather than
/// out of the implementation. A row whose expectation can only be justified by reading
/// the code is a row testing the code against itself, and it does not belong here.
/// </para>
/// </summary>
internal static class MatchingCorpus
{
    private static readonly string Imdb = MetadataProvider.Imdb.ToString();
    private static readonly string Tmdb = MetadataProvider.Tmdb.ToString();
    private static readonly string Tvdb = MetadataProvider.Tvdb.ToString();

    /// <summary>
    /// A provider the host knows and this plugin does not match on. Rows use it to prove
    /// that a provider outside the matched list is ignored rather than quietly consulted.
    /// </summary>
    private static readonly string Tvcom = MetadataProvider.Tvcom.ToString();

    /// <summary>
    /// Gets every row.
    /// </summary>
    public static IReadOnlyList<MatchingCase> Rows { get; } =
    [
        new MatchingCase
        {
            Situation = "a film matched on one shared provider",
            PeerItem = Movie((Imdb, "tt0111161")),
            LocalCandidates = [Movie((Imdb, "tt0111161"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0],
            ExpectedMatchedOn = MetadataProvider.Imdb.ToString()
        },
        new MatchingCase
        {
            Situation = "a film where two providers agree and one side carries only one of them",
            PeerItem = Movie((Imdb, "tt0111161"), (Tmdb, "278")),
            LocalCandidates = [Movie((Tmdb, "278"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0],
            ExpectedMatchedOn = MetadataProvider.Tmdb.ToString()
        },
        new MatchingCase
        {
            Situation = "a film where two providers disagree, which precedence must not resolve",
            PeerItem = Movie((Imdb, "tt0111161"), (Tmdb, "278")),
            LocalCandidates = [Movie((Imdb, "tt0111161"), (Tmdb, "279"))],
            Expected = MatchOutcome.Disagreement
        },
        new MatchingCase
        {
            Situation = "an episode with no identifier of its own, matched through its series and numbering",
            PeerItem = Episode(1, 2, (Tvdb, "121361")),
            LocalCandidates = [Episode(1, 2, (Tvdb, "121361"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0],
            ExpectedMatchedOn = MetadataProvider.Tvdb.ToString()
        },
        new MatchingCase
        {
            Situation = "the same film held twice locally, which is an ordinary library and not a fault",
            PeerItem = Movie((Imdb, "tt0111161")),
            LocalCandidates = [Movie((Imdb, "tt0111161")), Movie((Imdb, "tt0111161"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0, 1],
            ExpectedMatchedOn = MetadataProvider.Imdb.ToString()
        },
        new MatchingCase
        {
            Situation = "an item with no provider identifiers at all",
            PeerItem = Movie(),
            LocalCandidates = [Movie(), Movie((Imdb, "tt0111161"))],
            Expected = MatchOutcome.NoIdentifiers
        },
        new MatchingCase
        {
            Situation = "an episode whose series carries a different identifier on each side",
            PeerItem = Episode(1, 2, (Tvdb, "121361")),
            LocalCandidates = [Episode(1, 2, (Tvdb, "999999"))],
            Expected = MatchOutcome.Disagreement
        },
        new MatchingCase
        {
            Situation = "identifiers differing only by the case of the provider name, the case of the value and surrounding whitespace",
            PeerItem = Movie(("imdb", "  TT0111161 ")),
            LocalCandidates = [Movie((Imdb, "tt0111161"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0],
            ExpectedMatchedOn = MetadataProvider.Imdb.ToString()
        },
        new MatchingCase
        {
            Situation = "a provider this plugin does not match on, identical on both sides and the only thing shared",
            PeerItem = Movie((Tvcom, "12345")),
            LocalCandidates = [Movie((Tvcom, "12345"))],
            Expected = MatchOutcome.NoIdentifiers
        },
        new MatchingCase
        {
            Situation = "a provider this plugin does not match on, agreeing while a matched provider contradicts",
            PeerItem = Movie((Imdb, "tt0111161"), (Tvcom, "12345")),
            LocalCandidates = [Movie((Imdb, "tt0000001"), (Tvcom, "12345"))],
            Expected = MatchOutcome.Disagreement
        },
        new MatchingCase
        {
            Situation = "two local films agreeing with the peer item and contradicting each other",
            PeerItem = Movie((Imdb, "tt0111161")),
            LocalCandidates = [Movie((Imdb, "tt0111161"), (Tmdb, "278")), Movie((Imdb, "tt0111161"), (Tmdb, "279"))],
            Expected = MatchOutcome.Ambiguous,
            ExpectedMatches = [0, 1]
        },
        new MatchingCase
        {
            Situation = "a film the local server does not have, which is not a contradiction",
            PeerItem = Movie((Imdb, "tt0111161")),
            LocalCandidates = [Movie((Tmdb, "238")), Movie((Tvdb, "9"))],
            Expected = MatchOutcome.NoCandidate
        },
        new MatchingCase
        {
            Situation = "another episode of the same series, which is a different item and not a contradiction",
            PeerItem = Episode(1, 2, (Tvdb, "121361")),
            LocalCandidates = [Episode(1, 3, (Tvdb, "121361"))],
            Expected = MatchOutcome.NoCandidate
        },
        new MatchingCase
        {
            Situation = "a provider present with a blank value, which is a gap in the metadata and not a contradiction",
            PeerItem = Movie((Imdb, "tt0111161"), (Tmdb, "   ")),
            LocalCandidates = [Movie((Imdb, "tt0111161"), (Tmdb, "278"))],
            Expected = MatchOutcome.Matched,
            ExpectedMatches = [0],
            ExpectedMatchedOn = MetadataProvider.Imdb.ToString()
        }
    ];

    /// <summary>
    /// Gets the situation names, which is what the theory is parameterised by so that a
    /// failure prints the situation rather than a row number.
    /// </summary>
    /// <returns>One situation name per row.</returns>
    public static IEnumerable<object[]> Situations()
        => Rows.Select(row => new object[] { row.Situation });

    /// <summary>
    /// Finds a row by its situation name.
    /// </summary>
    /// <param name="situation">The situation name.</param>
    /// <returns>The row.</returns>
    public static MatchingCase Row(string situation)
        => Rows.Single(row => row.Situation == situation);

    /// <summary>
    /// Every provider name a row mentions anywhere, on the peer item or on a candidate,
    /// on the item itself or on its series. This is what the coverage guards count, so it
    /// has to look everywhere a row can put one.
    /// </summary>
    /// <param name="row">The row.</param>
    /// <returns>The provider names, without duplicates and ignoring case.</returns>
    public static IReadOnlySet<string> ProviderNames(MatchingCase row)
    {
        ArgumentNullException.ThrowIfNull(row);

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in row.LocalCandidates.Prepend(row.PeerItem))
        {
            names.UnionWith(item.ProviderIds.Keys);
            names.UnionWith(item.SeriesProviderIds.Keys);
        }

        return names;
    }

    private static MatchableItem Movie(params (string Provider, string Value)[] providerIds)
        => new()
        {
            Kind = MatchableItemKind.Movie,
            ProviderIds = providerIds.ToDictionary(p => p.Provider, p => p.Value)
        };

    private static MatchableItem Episode(
        int seasonNumber,
        int episodeNumber,
        params (string Provider, string Value)[] seriesProviderIds)
        => new()
        {
            Kind = MatchableItemKind.Episode,
            SeasonNumber = seasonNumber,
            EpisodeNumber = episodeNumber,
            SeriesProviderIds = seriesProviderIds.ToDictionary(p => p.Provider, p => p.Value)
        };
}
