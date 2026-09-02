using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The half of issue #18's seventh property that lives in markup: the page an operator opens
/// asks this plugin whether a window is open, and renders the answer as text.
/// </summary>
/// <remarks>
/// The action beside it is proved in <c>Api/OpenWindowTests</c>. What that cannot see is
/// whether anything reads it, and an endpoint nobody calls says nothing to an operator, which
/// is the whole of the property.
/// <para>
/// WHAT THIS READS IS THE FILE AND NOT A BROWSER. No page is rendered, no script is executed
/// and no request is made, here or anywhere in this suite: <c>docs/testing.md</c> refuses the
/// apparatus that would, so what is asserted is that the markup carries the call and carries
/// no assignment that would put an endpoint's answer into the document as markup. Whether the
/// web client runs it is not measured.
/// </para>
/// </remarks>
public class EnrolmentWindowOnThePageTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The path the administrative action is routed at. It is written here as the string the
    /// page has to carry, so a route that moved without the page moving fails rather than
    /// leaving the page calling nothing.
    /// </summary>
    private const string WindowsPath = "ServerPairing/Administration/windows";

    /// <summary>
    /// The ways a value reaches the document as markup rather than as text. A string put
    /// through any of these is parsed, so what an endpoint answered would become elements.
    /// </summary>
    private static readonly string[] MarkupSinks =
    {
        "innerHTML",
        "outerHTML",
        "insertAdjacentHTML",
        "document.write",
    };

    /// <summary>
    /// The page asks this plugin whether a window is open. Without this the action is a read
    /// nobody performs and an operator is told nothing, which is the failure the seventh
    /// property of issue #18 is about.
    /// </summary>
    [Fact]
    public void ThePageAsksWhetherAnEnrolmentWindowIsOpen()
    {
        Assert.Contains(WindowsPath, Page(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The answer is rendered rather than fetched and dropped. The element the sentence goes
    /// into is on the page and the function that fills it is called, so a page that asks and
    /// renders nothing is not this case passing.
    /// </summary>
    [Fact]
    public void TheAnswerReachesSomethingAnOperatorCanSee()
    {
        var page = Page();

        Assert.Contains("id=\"EnrolmentWindowState\"", page, StringComparison.Ordinal);
        Assert.Contains("renderEnrolmentWindows", page, StringComparison.Ordinal);
        Assert.Contains("No enrolment window is open.", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// Nothing on this page puts a value into the document as markup. The rule is written at
    /// the first value out of an endpoint rather than at the first peer-controlled one, because
    /// a page that has learned the habit on one value keeps it when a peer's display name
    /// arrives, and that string is issue #52's subject.
    /// </summary>
    [Fact]
    public void NoValueOnThisPageReachesTheDocumentAsMarkup()
    {
        var page = Page();

        foreach (var sink in MarkupSinks)
        {
            Assert.DoesNotContain(sink, page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The floor under the three above. Each of them reads one file, and a moved or renamed
    /// page would make the assertions read an empty string, which the last one passes on.
    /// </summary>
    [Fact]
    public void TheRealPageIsRead()
    {
        var page = Page();

        Assert.NotEmpty(page);
        Assert.Contains("ServerPairingConfigPage", page, StringComparison.Ordinal);
        Assert.NotEmpty(MarkupSinks);
    }

    /// <summary>
    /// The configuration page as it is committed, read from the source tree rather than from
    /// the build output, which is a copy.
    /// </summary>
    /// <returns>The whole file.</returns>
    private static string Page()
        => File.ReadAllText(Path.Join(
            RepositoryRoot(),
            "Jellyfin.Plugin.ServerPairing",
            "Configuration",
            "configPage.html"));

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears. It is not derived from the path the
    /// compiler recorded for this file, which a deterministic build rewrites.
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
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the page has no root to be read from.")
            : directory.FullName;
    }
}
