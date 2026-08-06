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
