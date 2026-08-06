using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Two habits make an expiry impossible to test: reading the wall clock from the plugin
/// source, and waiting for real time in the suite. Both are cheap to introduce and
/// expensive to remove once several expiries depend on them, so they are refused here
/// before the first expiry exists rather than after.
/// </summary>
public class ClockSourceTests
{
    /// <summary>
    /// The calls that read the wall clock. Each one returns a different answer on every
    /// invocation and takes no argument a test could substitute, which is what makes a
    /// value derived from it impossible to move on command.
    /// </summary>
    private static readonly string[] WallClockCalls =
    {
        "DateTime.Now",
        "DateTime.UtcNow",
        "DateTime.Today",
        "DateTimeOffset.Now",
        "DateTimeOffset.UtcNow",
        "Environment.TickCount",
        "Stopwatch.GetTimestamp",
    };

    /// <summary>
    /// The calls that wait for real time. They are assembled from pieces rather than
    /// written out, because this file is inside the set the assertion below reads and a
    /// literal here would make the guard refuse itself.
    /// </summary>
    private static readonly string[] SleepingCalls =
    {
        "Thread" + ".Sleep",
        "Task" + ".Delay",
        "SpinWait" + ".SpinUntil",
    };

    /// <summary>
    /// Nothing in the plugin reads the wall clock. Every expiry this protocol has, the
    /// enrolment window, the timestamp window, the rotation overlap and the nonce store,
    /// takes its now from an injected source instead, so a test can move time rather than
    /// wait for it.
    /// </summary>
    [Fact]
    public void NoPluginSourceFileReadsTheWallClock()
    {
        var offenders = Occurrences(SourceFiles(PluginSourceDirectory()), WallClockCalls);

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// Nothing in the suite waits for real time. A test that sleeps is slow on the day it
    /// is written and flaky on some later day, and the first response to a flaky test is
    /// to delete it, which is how an expiry ends up with no test at all.
    /// </summary>
    [Fact]
    public void NoTestSourceFileWaitsForRealTime()
    {
        var offenders = Occurrences(SourceFiles(TestSourceDirectory()), SleepingCalls);

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// The two assertions above pass trivially if the directory walk finds nothing, which
    /// is what happens the day somebody moves a project or renames a folder. This fixes
    /// the floor: both scans have to be reading a real set of files, and this file is one
    /// of the ones the second scan reads.
    /// </summary>
    [Fact]
    public void BothScansActuallyReadFiles()
    {
        var pluginFiles = SourceFiles(PluginSourceDirectory());
        var testFiles = SourceFiles(TestSourceDirectory());

        Assert.NotEmpty(pluginFiles);
        Assert.NotEmpty(testFiles);
        Assert.Contains(pluginFiles, f => Path.GetFileName(f) == "Plugin.cs");
        Assert.Contains(testFiles, f => Path.GetFileName(f) == "ClockSourceTests.cs");
    }

    /// <summary>
    /// The vocabularies are not empty, so a later edit that empties one turns the guard
    /// off in a way that stays green. This is the check the ledger of a removed rule
    /// would otherwise have to catch by hand.
    /// </summary>
    [Fact]
    public void BothVocabulariesHaveEntries()
    {
        Assert.NotEmpty(WallClockCalls);
        Assert.NotEmpty(SleepingCalls);
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
                foreach (var call in calls)
                {
                    if (lines[i].Contains(call, StringComparison.Ordinal))
                    {
                        found.Add(Path.GetFileName(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + call);
                    }
                }
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Every C# file under a directory, skipping the build output, which holds generated
    /// files nobody wrote and a copy of nothing worth judging.
    /// </summary>
    /// <param name="directory">The directory to walk.</param>
    /// <returns>The paths of the files found.</returns>
    private static string[] SourceFiles(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
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
    /// The plugin project directory, derived from where this file was compiled from rather
    /// than from the working directory of the test run, which differs between a local run
    /// and a run on a build machine.
    /// </summary>
    /// <returns>The absolute path of the plugin project directory.</returns>
    private static string PluginSourceDirectory()
        => Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    /// <summary>
    /// The test project directory, derived the same way.
    /// </summary>
    /// <returns>The absolute path of the test project directory.</returns>
    private static string TestSourceDirectory()
        => Path.Combine(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing.Tests");

    /// <summary>
    /// The repository root, two directories above this file.
    /// </summary>
    /// <param name="thisFile">Filled in by the compiler with the path of this source file.</param>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot([CallerFilePath] string thisFile = "")
        => Path.GetDirectoryName(Path.GetDirectoryName(thisFile)!)!;
}
