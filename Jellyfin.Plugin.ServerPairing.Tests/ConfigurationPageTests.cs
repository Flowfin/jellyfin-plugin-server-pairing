using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The dashboard page runs inside the Jellyfin web client, on the same origin, holding an
/// administrator's session. A resource it pulls from somewhere else runs there too, with
/// that session, and it changes whenever the server serving it changes. So the page loads
/// nothing that this plugin does not ship, and this is the assertion that refuses the
/// first one to arrive.
///
/// It also keeps the page working on a server with no route to the internet, which is a
/// setup an operator pairing two of their own machines is entitled to have.
/// </summary>
public class ConfigurationPageTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// A string the configuration page carries. The scan below passes trivially if it
    /// reads nothing, so one page has to be found and it has to be the real one rather
    /// than any file that happens to end in .html.
    /// </summary>
    private const string PageMarker = "ServerPairingConfigPage";

    /// <summary>
    /// What a reference to somewhere else looks like in markup, style or script.
    ///
    /// The two schemes are the obvious half. The protocol-relative forms are the half
    /// worth writing out: they carry no scheme, they read like a local path, and they
    /// resolve to whatever host follows the slashes.
    /// </summary>
    private static readonly string[] ExternalResourceMarkers =
    {
        "http://",
        "https://",
        "src=\"//",
        "src='//",
        "href=\"//",
        "href='//",
        "url(//",
    };

    /// <summary>
    /// No page this plugin ships references a host. Every script, style, font and image it
    /// needs is served by the plugin or is already in the web client, so a third party
    /// going down, or being taken over, cannot change what an administrator's browser
    /// executes.
    /// </summary>
    [Fact]
    public void NoConfigurationPageReferencesAnExternalHost()
    {
        var offenders = Occurrences(PageFiles(), ExternalResourceMarkers);

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The assertion above passes on an empty set, which is what a moved or renamed page
    /// would produce. This fixes the floor: a real page is found, and it is the plugin's
    /// own rather than whatever else the walk turned up.
    /// </summary>
    [Fact]
    public void TheScanReadsTheRealPage()
    {
        var pages = PageFiles();

        Assert.NotEmpty(pages);
        Assert.Contains(pages, p => Path.GetFileName(p) == "configPage.html");
        Assert.Contains(
            pages,
            p => File.ReadAllText(p).Contains(PageMarker, StringComparison.Ordinal));
    }

    /// <summary>
    /// The vocabulary is not empty, so an edit that empties it turns the guard off in a
    /// way that stays green.
    /// </summary>
    [Fact]
    public void TheVocabularyHasEntries()
    {
        Assert.NotEmpty(ExternalResourceMarkers);
    }

    /// <summary>
    /// Finds every line of every given file that contains one of the given markers.
    /// </summary>
    /// <param name="files">The files to read.</param>
    /// <param name="markers">The markers to look for.</param>
    /// <returns>One entry per occurrence, as file, line number and the marker found.</returns>
    private static string[] Occurrences(IReadOnlyCollection<string> files, IReadOnlyCollection<string> markers)
    {
        var found = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var marker in markers.Where(m => line.Contains(m, StringComparison.Ordinal)))
                {
                    found.Add(Path.GetFileName(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + marker);
                }
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Every page the plugin project holds, skipping the build output, which is a copy
    /// rather than a thing anybody edits.
    /// </summary>
    /// <returns>The paths of the pages found.</returns>
    private static string[] PageFiles()
        => Directory.EnumerateFiles(PluginSourceDirectory(), "*.html", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Segment("obj"), StringComparison.Ordinal))
            .Where(f => !f.Contains(Segment("bin"), StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// A path segment with a separator on both sides, so that a directory named obj is
    /// skipped and a file named something-obj-something is not.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns>The name wrapped in directory separators.</returns>
    private static string Segment(string name)
        => $"{Path.DirectorySeparatorChar}{name}{Path.DirectorySeparatorChar}";

    /// <summary>
    /// The plugin project directory, derived from where the test assembly sits rather than
    /// from the working directory of the test run, which differs between a local run and a
    /// run on a build machine.
    /// </summary>
    /// <returns>The absolute path of the plugin project directory.</returns>
    private static string PluginSourceDirectory()
        => Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears.
    ///
    /// It is not derived from the path the compiler recorded for this file. Deterministic
    /// builds rewrite that path to a placeholder root, so a compiler-supplied path is a
    /// real directory on a developer machine and is not one anywhere the build sets
    /// ContinuousIntegrationBuild, which Directory.Build.props does on every build machine.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the page scan has no root to read.")
            : directory.FullName;
    }
}
