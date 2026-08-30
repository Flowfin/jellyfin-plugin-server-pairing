using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Logging;

/// <summary>
/// Ties every logger call site in the plugin to a row of the table in
/// <c>docs/logging.md</c>, in both directions.
///
/// A Jellyfin log file is something an operator pastes into a public thread, so what this
/// plugin writes there is part of the threat model. Issue #13's first done condition is
/// that the document holds the list of what is logged, and a list is only that while it
/// matches the tree. It stopped matching without anything noticing: two entries
/// <c>ConfigurationAtStartup</c> writes had no row at all from the day they landed, and
/// both were found by a person reading two files side by side.
///
/// The document is the subject rather than the source. A test that read the call sites and
/// printed a table would agree with itself forever; this reads a file a person wrote and
/// fails when the two disagree.
/// </summary>
public class LoggedEventTableTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The document this guard judges.
    /// </summary>
    private const string DocumentPath = "docs/logging.md";

    /// <summary>
    /// The header of the one table in that document this guard reads. The table is found by
    /// its header rather than by its position, so a section added above it moves nothing.
    /// </summary>
    private const string TableHeader = "| Event | Level | Fields |";

    /// <summary>
    /// The logger methods a call site is written with. The list is closed on purpose: a
    /// method outside it is a call site this guard would not see, so it is added here in
    /// the same change that first uses it.
    /// </summary>
    private static readonly string[] LoggerMethods =
    {
        ".LogTrace(",
        ".LogDebug(",
        ".LogInformation(",
        ".LogWarning(",
        ".LogError(",
        ".LogCritical(",
    };

    /// <summary>
    /// Every call site in the plugin, keyed by the opening sentence of the message it
    /// writes, against the row of the table that entry is.
    ///
    /// The key is the message rather than a file and a line, because a line number moves
    /// whenever anything above it does, and a message moving is a thing somebody should
    /// re-read against its row. Two call sites may name one row: an unreadable key store is
    /// one event whether the startup read or an administrator's request met it.
    /// </summary>
    private static readonly Dictionary<string, string> RowPerCallSite = new(StringComparer.Ordinal)
    {
        ["A request on the pairing plane faulted and was answered with the refusal every caller gets."] =
            "A request on the pairing plane faulted",
        ["A setting was refused, so this plugin is loaded and will not pair."] =
            "A setting was refused at startup",
        ["This server is configured to accept a cleartext peer address."] =
            "A cleartext peer address was acknowledged",
        ["The key store could not be read for an administrator, so what this server holds is unknown."] =
            "The key store could not be read or written",
        ["The key store could not be read at startup, so what it holds is unknown and no pairing will work."] =
            "The key store could not be read or written",
        ["The key store was written by an older build and has been carried up to the format this one reads."] =
            "The key store was carried up from an older format",
        ["This plugin started against a key store that already holds this pairing."] =
            "The plugin started against a store that already holds a pairing",
    };

    /// <summary>
    /// The direction this guard exists for. A call site the table names no row for is an
    /// entry this plugin writes into an operator's log and this document does not admit to
    /// writing, which is the defect that stood in the tree until it was read by hand.
    /// </summary>
    [Fact]
    public void EveryCallSiteHasARow()
    {
        var unmapped = CallSites()
            .Where(site => !RowPerCallSite.ContainsKey(site.Message))
            .Select(site => site.Where + ": " + site.Message)
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), unmapped);
    }

    /// <summary>
    /// The row a call site is held against is one the table actually carries. Without this
    /// the case above passes on a row name that exists here and nowhere else, which is the
    /// document going stale with the guard reporting green.
    /// </summary>
    [Fact]
    public void EveryRowNamedForACallSiteIsInTheTable()
    {
        var rows = Rows().ToHashSet(StringComparer.Ordinal);

        var missing = RowPerCallSite.Values
            .Distinct(StringComparer.Ordinal)
            .Where(row => !rows.Contains(row))
            .OrderBy(row => row, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), missing);
    }

    /// <summary>
    /// The other direction over the call sites, which is the half that goes stale silently.
    /// An entry claimed here for a call site the plugin no longer has says this plugin
    /// writes something it does not, and it keeps the case above green while doing it.
    /// </summary>
    [Fact]
    public void EveryMappedCallSiteIsStillInTheSource()
    {
        var written = CallSites().Select(site => site.Message).ToHashSet(StringComparer.Ordinal);

        var gone = RowPerCallSite.Keys
            .Where(message => !written.Contains(message))
            .OrderBy(message => message, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), gone);
    }

    /// <summary>
    /// The floor under the three above. Each of them compares two sets and passes when both
    /// are empty, so a source walk that reached no file, a table header that was renamed, or
    /// an emptied vocabulary would turn the whole guard off while leaving it green.
    /// </summary>
    [Fact]
    public void TheSourceAndTheTableAreBothActuallyRead()
    {
        var sites = CallSites();

        Assert.NotEmpty(LoggerMethods);
        Assert.NotEmpty(sites);
        Assert.NotEmpty(Rows());
        Assert.NotEmpty(RowPerCallSite);
        Assert.Contains(sites, site => site.Where.Contains("ConfigurationAtStartup.cs", StringComparison.Ordinal));
        Assert.Contains(Rows(), row => string.Equals(row, "A setting was refused at startup", StringComparison.Ordinal));
    }

    /// <summary>
    /// Every logger call site in the plugin's own source, as where it is and the opening
    /// sentence of the message it writes.
    /// </summary>
    /// <returns>One entry per call site, ordered by where it is.</returns>
    private static CallSite[] CallSites()
    {
        var found = new List<CallSite>();

        foreach (var file in SourceFiles())
        {
            var text = File.ReadAllText(file);

            foreach (var method in LoggerMethods)
            {
                var at = text.IndexOf(method, StringComparison.Ordinal);

                while (at >= 0)
                {
                    var message = FirstLiteralIn(text, at + method.Length);

                    if (message.Length > 0)
                    {
                        found.Add(new CallSite(Where(file, text, at), OpeningSentence(message)));
                    }

                    at = text.IndexOf(method, at + method.Length, StringComparison.Ordinal);
                }
            }
        }

        return found.OrderBy(site => site.Where, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// The first string literal of a call, which for a logger call is the message. A call
    /// whose first argument is the fault reads the same way, because an exception argument
    /// carries no literal of its own. The search stops at the end of the statement, so a
    /// call with no message at all answers nothing rather than reaching into the next one.
    /// </summary>
    /// <param name="text">The whole file.</param>
    /// <param name="from">Where to start looking.</param>
    /// <returns>The literal's contents, empty where the call carries none.</returns>
    private static string FirstLiteralIn(string text, int from)
    {
        var open = text.IndexOf('"', from);
        var statement = text.IndexOf(';', from);

        if (open < 0 || (statement >= 0 && open > statement))
        {
            return string.Empty;
        }

        var close = open + 1;

        while (close < text.Length && text[close] != '"')
        {
            close += text[close] == '\\' ? 2 : 1;
        }

        return close < text.Length ? text[(open + 1)..close] : string.Empty;
    }

    /// <summary>
    /// The opening sentence of a message, which is what a row is held against. What follows
    /// it is the operator's instructions and the placeholders, and both move for reasons
    /// that are not about which event this is.
    /// </summary>
    /// <param name="message">The whole message.</param>
    /// <returns>Up to and including the first full stop, or all of it where there is none.</returns>
    private static string OpeningSentence(string message)
    {
        var stop = message.IndexOf('.', StringComparison.Ordinal);

        return stop < 0 ? message : message[..(stop + 1)];
    }

    /// <summary>
    /// Where a position in a file is, as the file's name and the line number, so a failure
    /// names something a reader can open.
    /// </summary>
    /// <param name="file">The file's path.</param>
    /// <param name="text">The whole file.</param>
    /// <param name="at">The position.</param>
    /// <returns>The file name and the line number.</returns>
    private static string Where(string file, string text, int at)
        => Path.GetFileName(file)
            + ":"
            + (text[..at].Count(c => c == '\n') + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// The event column of every row of the table in the document, in the order they are
    /// written.
    /// </summary>
    /// <returns>One entry per row, with the header and the alignment row skipped.</returns>
    private static string[] Rows()
    {
        var lines = File.ReadAllLines(Path.Join(RepositoryRoot(), DocumentPath));
        var header = Array.FindIndex(lines, line => string.Equals(line.Trim(), TableHeader, StringComparison.Ordinal));

        if (header < 0)
        {
            throw new InvalidOperationException(
                $"'{DocumentPath}' carries no line reading '{TableHeader}', so the table cannot be found and this guard would otherwise pass on an empty set.");
        }

        var rows = new List<string>();

        // The header, then the alignment row, then the rows. The table ends at the first
        // line that is not one, which is how a paragraph written under it stays out.
        for (var i = header + 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
        {
            var cells = lines[i].Trim().Trim('|').Split('|');

            if (cells.Length == 3)
            {
                rows.Add(cells[0].Trim());
            }
        }

        return rows.ToArray();
    }

    /// <summary>
    /// Every C# file the plugin project holds, skipping the build output, which is a copy
    /// rather than a thing anybody edits.
    /// </summary>
    /// <returns>The paths of the files found.</returns>
    private static string[] SourceFiles()
        => Directory
            .EnumerateFiles(Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing"), "*.cs", SearchOption.AllDirectories)
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

        while (directory is not null && !File.Exists(Path.Join(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so '{DocumentPath}' has no root to be found under.")
            : directory.FullName;
    }

    /// <summary>
    /// One logger call site.
    /// </summary>
    /// <param name="Where">The file name and the line number.</param>
    /// <param name="Message">The opening sentence of the message it writes.</param>
    private sealed record CallSite(string Where, string Message);
}
