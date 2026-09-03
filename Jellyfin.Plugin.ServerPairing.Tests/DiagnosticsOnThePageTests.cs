using System;
using System.IO;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Api;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The fourth condition of issue #51: the page renders the diagnostics without an operator
/// needing to call the endpoint by hand.
/// </summary>
/// <remarks>
/// The action beside it is proved in <c>Api/DiagnosticsSecretsTests</c> and
/// <c>Api/RefusalCountersTests</c>. What those cannot see is whether anything reads it, and a
/// payload nobody renders is a number an operator has to fetch with a token by hand, which is
/// the state this condition exists against.
/// <para>
/// WHAT THIS READS IS THE FILE AND NOT A BROWSER. No page is rendered, no script is executed
/// and no request is made, here or anywhere in this suite: <c>docs/testing.md</c> refuses the
/// apparatus that would. So what is asserted is the structure of the script as text: that the
/// call is carried, that the members are walked rather than named, and that the payload is
/// offered verbatim. Whether the web client runs it is not measured.
/// </para>
/// </remarks>
public class DiagnosticsOnThePageTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The path the diagnostics action is routed at, written as the string the page has to
    /// carry, so a route that moved without the page moving fails rather than leaving the page
    /// calling nothing.
    /// </summary>
    private const string DiagnosticsPath = "ServerPairing/Administration/diagnostics";

    /// <summary>
    /// The page asks this plugin for the diagnostics and renders the answer into an element that
    /// is on the page, so an operator reads it where they already are.
    /// </summary>
    [Fact]
    public void ThePageAsksForTheDiagnosticsAndRendersThem()
    {
        var page = Page();

        Assert.Contains("'" + DiagnosticsPath + "'", page, StringComparison.Ordinal);
        Assert.Contains("id=\"Diagnostics\"", page, StringComparison.Ordinal);
        Assert.Contains("loadDiagnostics();", page, StringComparison.Ordinal);
        Assert.Contains(".then(renderDiagnostics", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The members are walked, not named. <see cref="DiagnosticsAnswer"/> gains a member whenever
    /// something starts producing one, and a page naming the members it knew would render the
    /// old ones and silently omit the next. So the rendering walks the keys of the answer, and
    /// names none of the members the answer has today; this is asserted over every member the
    /// type declares rather than over a list written here.
    /// </summary>
    [Fact]
    public void TheMembersAreWalkedRatherThanNamed()
    {
        var rendering = FunctionBody(Page(), "renderDiagnostics");

        Assert.Contains("Object.keys(payload)", rendering, StringComparison.Ordinal);

        foreach (var member in typeof(DiagnosticsAnswer).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            Assert.DoesNotContain(member.Name, rendering, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// The payload is offered verbatim, because the thing an operator pastes into a support
    /// thread is the answer and not a rendering of it, and the two must not drift.
    /// </summary>
    [Fact]
    public void ThePayloadIsOfferedVerbatimForPasting()
    {
        var rendering = FunctionBody(Page(), "renderDiagnostics");

        Assert.Contains("JSON.stringify(payload", rendering, StringComparison.Ordinal);
        Assert.Contains("textContent = JSON.stringify(payload", rendering, StringComparison.Ordinal);
    }

    /// <summary>
    /// The floor under the cases above. Each reads one file and one function body out of it,
    /// so a moved page or a renamed function would make them read an empty string, which the
    /// negative assertion above passes on; and the walk over the answer's members has to find
    /// members, or that assertion is over nothing.
    /// </summary>
    [Fact]
    public void TheRealPageAndItsFunctionAreRead()
    {
        var page = Page();

        Assert.NotEmpty(page);
        Assert.Contains("ServerPairingConfigPage", page, StringComparison.Ordinal);
        Assert.NotEmpty(FunctionBody(page, "renderDiagnostics"));
        Assert.NotEmpty(typeof(DiagnosticsAnswer).GetProperties(BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    /// The text of one script function, from its declaration to the next declaration at the
    /// same indentation, which is how the page's script is laid out.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="name">The function's name.</param>
    /// <returns>The declaration and body, or an empty string where the page has no such function.</returns>
    private static string FunctionBody(string page, string name)
    {
        var declaration = "\n            function " + name + "(";
        var start = page.IndexOf(declaration, StringComparison.Ordinal);

        if (start < 0)
        {
            return string.Empty;
        }

        var next = page.IndexOf("\n            function ", start + declaration.Length, StringComparison.Ordinal);

        return next < 0 ? page[start..] : page[start..next];
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
