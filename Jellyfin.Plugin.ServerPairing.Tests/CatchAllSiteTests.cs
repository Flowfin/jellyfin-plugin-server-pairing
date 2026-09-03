using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// A catch that takes every fault exists in this plugin only where this file names one, and
/// each site is named with the reason it is there.
/// </summary>
/// <remarks>
/// A catch-all at a boundary is the property rather than a lapse: a fault that escapes the
/// pairing plane would be answered as a server error, and a server error tells a caller apart
/// from a refusal, which is exactly the bit the plane exists not to hand over. A catch-all
/// anywhere else is the opposite - it swallows a failure the caller was owed - and nothing in
/// this repository could tell the two apart until this file did.
/// <para>
/// `jellyfin.ruleset` sets CA1031 to Info, so the analyzer that would say this reports
/// nothing and the pragmas at the sites below suppress a warning that is not raised. What
/// judged it instead was CodeQL's `cs/catch-of-all-exceptions`, which is a note in a report
/// that has to be opened, reports the declared boundaries every time it runs, and cannot read
/// a declaration. This is that rule with the declaration added and the verdict moved into the
/// suite, and the CodeQL rule is excluded in `.github/workflows/scan-codeql.yaml` on the
/// strength of this file.
/// </para>
/// <para>
/// The subject is the plugin project alone. A catch-all in the suite or in the fuzz target
/// absorbs nothing a server would have answered, and a guard that reddens on test code is one
/// somebody turns off.
/// </para>
/// <para>
/// What it does not see, stated so the count is not read as more than it is: a catch-all that
/// MOVES inside a file this table already names is invisible to it, because the declaration
/// is per file and per count rather than per line. A line number in a table drifts on every
/// edit above it and would be repaired by hand until somebody repaired it wrongly.
/// </para>
/// </remarks>
public class CatchAllSiteTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The directory the subject is read from, relative to the repository root.
    /// </summary>
    private const string PluginProjectDirectory = "Jellyfin.Plugin.ServerPairing";

    /// <summary>
    /// The sites that may hold a catch that takes every fault, as the path relative to the
    /// plugin project and the number of them in that file.
    ///
    /// Api/PeerPlaneController.cs: every escaping fault is answered with the refusal every
    /// caller gets, so a fault on the pairing plane tells a peer no more than a refusal does.
    ///
    /// Api/AdministrativePlaneController.cs: every escaping fault is answered as the
    /// administrative problem that names what failed and carries nothing of the fault. Four of
    /// them, one per store each action reads: a key store that will not parse and a record store
    /// that will not parse are two files and two answers, and a single catch over both would
    /// name whichever store the code happened to reach first. The report of what is held about
    /// one user reads the record store and then the mapping store, and carries one per store for
    /// the same reason.
    ///
    /// KeyStore/StoreAtStartup.cs: a store that cannot be read at startup must not take the
    /// server down with it, so the read is reported and the host is left running.
    ///
    /// KeyStore/AtomicWrite.cs: the bare catch there discards the half-written temporary and
    /// rethrows. It absorbs nothing, which is why it is a site rather than an exception to
    /// this rule, and it is declared because the scan cannot read a rethrow.
    /// </summary>
    private static readonly Dictionary<string, int> DeclaredSites = new(StringComparer.Ordinal)
    {
        ["Api/PeerPlaneController.cs"] = 1,
        ["Api/AdministrativePlaneController.cs"] = 4,
        ["KeyStore/StoreAtStartup.cs"] = 1,
        ["KeyStore/AtomicWrite.cs"] = 1,
    };

    /// <summary>
    /// The typed form, with and without the namespace the compiler does not require. A line is
    /// judged by its start, because what follows is a variable name or a closing bracket.
    /// </summary>
    private static readonly string[] TypedCatchAlls =
    {
        "catch (Exception",
        "catch (System.Exception",
    };

    /// <summary>
    /// The bare form, with the brace on its own line and on the same one. These are compared
    /// whole rather than by prefix, because every typed catch in this tree starts with the
    /// same five letters and a prefix here would match all of them.
    /// </summary>
    private static readonly string[] BareCatchAlls =
    {
        "catch",
        "catch {",
    };

    /// <summary>
    /// Nothing in the plugin catches every fault at a site this file does not name. A site
    /// added without a line in the table above fails here, which is the whole point: the
    /// declaration is the review, and it happens before the merge rather than in a report
    /// somebody opens later.
    /// </summary>
    [Fact]
    public void NoCatchTakesEveryFaultAtASiteThisRepositoryHasNotDeclared()
    {
        var undeclared = Found()
            .Where(site => !DeclaredSites.ContainsKey(site.Key))
            .Select(site => site.Key + ": " + site.Value.ToString(CultureInfo.InvariantCulture))
            .OrderBy(site => site, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), undeclared);
    }

    /// <summary>
    /// Every declared site still holds the number of catch-alls it declares. This is the
    /// direction that fails closed the other way: a declaration for a site that has been
    /// repaired, moved or deleted is a permission nobody is using, and a file that has quietly
    /// gained a second catch-all under a declaration for one is the case the count exists for.
    /// </summary>
    [Fact]
    public void EveryDeclaredSiteStillHoldsWhatItDeclares()
    {
        var found = Found();

        var wrong = DeclaredSites
            .Where(declared => !found.TryGetValue(declared.Key, out var count) || count != declared.Value)
            .Select(declared => declared.Key
                + ": declared " + declared.Value.ToString(CultureInfo.InvariantCulture)
                + ", found " + (found.TryGetValue(declared.Key, out var count) ? count : 0).ToString(CultureInfo.InvariantCulture))
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), wrong);
    }

    /// <summary>
    /// The two assertions above pass on an empty scan, which is what a moved project or a
    /// renamed folder produces. This fixes the floor: the walk has to be reading the plugin's
    /// own sources and reaching more than the directory it starts in.
    /// </summary>
    [Fact]
    public void TheScanReadsThePluginProject()
    {
        var files = SourceFiles().Select(Path.GetFileName).ToArray();

        Assert.NotEmpty(files);
        Assert.Contains("Plugin.cs", files, StringComparer.Ordinal);
        Assert.Contains("PeerPlaneController.cs", files, StringComparer.Ordinal);
        Assert.Contains("AtomicWrite.cs", files, StringComparer.Ordinal);
    }

    /// <summary>
    /// The vocabulary and the table are not empty, so an edit that empties either one turns
    /// the guard off in a way that stays green.
    /// </summary>
    [Fact]
    public void TheVocabularyAndTheTableHaveEntries()
    {
        Assert.NotEmpty(TypedCatchAlls);
        Assert.NotEmpty(BareCatchAlls);
        Assert.NotEmpty(DeclaredSites);
    }

    /// <summary>
    /// Every catch-all in the plugin project, counted per file.
    /// </summary>
    /// <returns>The path relative to the plugin project, and how many the file holds.</returns>
    private static Dictionary<string, int> Found()
    {
        var root = Path.Join(RepositoryRoot(), PluginProjectDirectory);
        var found = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in SourceFiles())
        {
            var count = File.ReadAllLines(file).Count(IsCatchAll);

            if (count > 0)
            {
                found[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = count;
            }
        }

        return found;
    }

    /// <summary>
    /// Whether a line opens a catch that takes every fault, judged against the two
    /// vocabularies above rather than against literals written here, so that emptying one of
    /// them is the failure the assertion over them is about rather than a change nothing sees.
    /// The line is trimmed first, so indentation does not decide it.
    /// </summary>
    /// <param name="line">The source line to judge.</param>
    /// <returns>True where the line opens a catch-all.</returns>
    private static bool IsCatchAll(string line)
    {
        var trimmed = line.Trim();

        return TypedCatchAlls.Any(spelling => trimmed.StartsWith(spelling, StringComparison.Ordinal))
            || BareCatchAlls.Any(spelling => string.Equals(trimmed, spelling, StringComparison.Ordinal));
    }

    /// <summary>
    /// Every C# file in the plugin project, skipping the build output, which holds generated
    /// files nobody wrote.
    /// </summary>
    /// <returns>The paths of the files found.</returns>
    private static string[] SourceFiles()
        => Directory.EnumerateFiles(Path.Join(RepositoryRoot(), PluginProjectDirectory), "*.cs", SearchOption.AllDirectories)
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
    /// It is not derived from the path the compiler recorded for this file, for the reason
    /// PathCompositionTests gives at the same walk: a deterministic build rewrites that path
    /// to a placeholder root on every build machine.
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
