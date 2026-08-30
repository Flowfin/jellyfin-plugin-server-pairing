using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// One call composes paths in this repository and the other does not. The refused call drops
/// every earlier argument when a later one turns out to be rooted, so a name that arrives
/// from somewhere else decides the whole path instead of the part it was given.
/// </summary>
/// <remarks>
/// Nothing that reaches a file name in this plugin comes from a peer today, so this refuses a
/// habit rather than a live defect. It is written now because the day one does is the day the
/// habit is expensive: a pairing identifier arrives on the wire, reaches a file name, and a
/// caller that sent an absolute one is answered with a store of its own choosing. The bound
/// is the same at every site, which is why the substitution is total rather than judged case
/// by case.
/// <para>
/// The replacement never drops a segment: it concatenates what it is given, separator
/// included, and a rooted later argument becomes part of the path rather than the whole of
/// it. Everything this repository composes is a directory joined with a constant or with a
/// name the caller built, so the two answer identically at every site that exists, and they
/// stop answering identically at exactly the site this guard is about.
/// </para>
/// <para>
/// The one behavioural difference worth naming rather than discovering: the refused call
/// throws on a null argument and the replacement treats one as empty. No site here can pass
/// null - every argument is a constant, a host path or a string the caller just built - so
/// nothing in this tree turned a throw into a silent join, and a site that could pass null
/// would owe its own refusal rather than relying on this one.
/// </para>
/// </remarks>
public class PathCompositionTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The call that composes a path and may drop what it was given. It is assembled from
    /// pieces rather than written out, because this file is inside the set the assertion
    /// below reads and a literal here would make the guard refuse itself.
    /// </summary>
    private static readonly string[] DroppingCalls =
    {
        "Path" + ".Combine(",
    };

    /// <summary>
    /// The call that is used instead, spelt the same way for the same reason.
    /// </summary>
    private static readonly string[] KeepingCalls =
    {
        "Path" + ".Join(",
    };

    /// <summary>
    /// Nothing in this repository composes a path with the call that drops its earlier
    /// arguments. The walk covers every project rather than the two the solution names, so a
    /// project added later is judged without anybody remembering this file.
    /// </summary>
    [Fact]
    public void NoSourceFileComposesAPathWithACallThatDropsWhatItWasGiven()
    {
        var offenders = Occurrences(SourceFiles(), DroppingCalls);

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The assertion above passes trivially if the walk finds nothing, which is what happens
    /// the day somebody moves a project or renames a folder. This fixes the floor: the scan
    /// has to be reading a real set of files, and it has to be reaching the plugin, the suite
    /// and the fuzz target rather than one of the three.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsEveryProject()
    {
        var files = SourceFiles().Select(Path.GetFileName).ToArray();

        Assert.NotEmpty(files);
        Assert.Contains("Plugin.cs", files, StringComparer.Ordinal);
        Assert.Contains("PathCompositionTests.cs", files, StringComparer.Ordinal);
        Assert.Contains("Program.cs", files, StringComparer.Ordinal);
    }

    /// <summary>
    /// The replacement is actually in use, in files other than this one. Without this the
    /// guard stays green on a tree that composes no paths at all, which is the state it would
    /// reach if somebody satisfied it by deleting the call sites rather than by moving them.
    /// This file is excluded from the count because it composes a path of its own, so a
    /// tree that had lost every other site would still answer this question with one.
    /// </summary>
    [Fact]
    public void TheReplacementIsWhatComposesPathsElsewhere()
    {
        var uses = Occurrences(SourceFiles(), KeepingCalls)
            .Where(u => !u.StartsWith("PathCompositionTests.cs:", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(uses);
    }

    /// <summary>
    /// The vocabulary is not empty, so a later edit that empties it turns the guard off in a
    /// way that stays green.
    /// </summary>
    [Fact]
    public void TheVocabularyHasEntries()
    {
        Assert.NotEmpty(DroppingCalls);
        Assert.NotEmpty(KeepingCalls);
    }

    /// <summary>
    /// Finds every line of every given file that contains one of the given calls.
    /// </summary>
    /// <param name="files">The files to read.</param>
    /// <param name="calls">The calls to look for.</param>
    /// <returns>One entry per occurrence, as file, line number and the call found.</returns>
    private static string[] Occurrences(IReadOnlyCollection<string> files, IReadOnlyCollection<string> calls)
    {
        var found = new List<string>();

        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                foreach (var call in calls.Where(c => line.Contains(c, StringComparison.Ordinal)))
                {
                    found.Add(Path.GetFileName(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + call);
                }
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Every C# file in the repository, skipping the build output, which holds generated
    /// files nobody wrote.
    /// </summary>
    /// <returns>The paths of the files found.</returns>
    private static string[] SourceFiles()
        => Directory.EnumerateFiles(RepositoryRoot(), "*.cs", SearchOption.AllDirectories)
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
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the source scan has no root to read.")
            : directory.FullName;
    }
}
