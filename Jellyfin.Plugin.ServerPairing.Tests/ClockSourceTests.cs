using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
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
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

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
    /// The one call that produces a real clock, and the one file allowed to name it.
    /// </summary>
    /// <remarks>
    /// A clock has to enter the plugin somewhere: a request from a peer carries no instant
    /// this server may believe, so the edge that serves it reads the time and hands it in.
    /// What this refuses is a second place doing the same, because a clock resolved wherever
    /// it is wanted is a clock no test can move, which is the state the two assertions above
    /// exist to keep this plugin out of.
    /// <para>
    /// The composition root is the exception because that is where a dependency is chosen
    /// rather than used. Everything it hands the clock to takes it as an argument, so a test
    /// replaces it by constructing the type it is testing, and nothing has to reach past a
    /// static to do it.
    /// </para>
    /// </remarks>
    private const string RealClockCall = "TimeProvider.System";

    /// <summary>
    /// The one file that may name the call above.
    /// </summary>
    private const string CompositionRoot = "PluginServiceRegistrator.cs";

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
    /// is judged at an instant its caller hands in, so a test can move time rather than
    /// wait for it. There is no clock to inject and nothing injects one: a type that wanted
    /// the time would have nowhere to get it except its argument, which is what this
    /// assertion leaves it with.
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
    /// One file names the real clock and no other does. The composition root is where a
    /// dependency is chosen; a type that resolved one for itself would be a type whose expiry
    /// nobody can move, and that is the habit the whole of this file is about.
    /// </summary>
    [Fact]
    public void OnlyTheCompositionRootReachesForARealClock()
    {
        var reaching = Occurrences(SourceFiles(PluginSourceDirectory()), new[] { RealClockCall })
            .Select(o => o.Split(':')[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { CompositionRoot }, reaching);
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
        Assert.NotEmpty(RealClockCall);
        Assert.NotEmpty(CompositionRoot);
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
    /// The plugin project directory, derived from where the test assembly sits rather than
    /// from the working directory of the test run, which differs between a local run and a
    /// run on a build machine.
    /// </summary>
    /// <returns>The absolute path of the plugin project directory.</returns>
    private static string PluginSourceDirectory()
        => Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    /// <summary>
    /// The test project directory, derived the same way.
    /// </summary>
    /// <returns>The absolute path of the test project directory.</returns>
    private static string TestSourceDirectory()
        => Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing.Tests");

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
