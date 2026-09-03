using System;
using System.IO;
using Jellyfin.Plugin.ServerPairing.Wording;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The half of a pairing's mapping table that lives in markup: the page an operator opens
/// asks this plugin for every pairing's table, offers the one destructive action the plane has,
/// and puts what that action costs in the same view as the button.
/// </summary>
/// <remarks>
/// The actions beside it are proved in <c>Api/MappingTableTests</c> and <c>Api/WordingTests</c>.
/// What those cannot see is whether anything reads them, and a table nobody renders and a
/// sentence nobody shows say nothing to an operator, which is the whole of what issue #49's
/// third condition and issue #40's page half are about.
/// <para>
/// WHAT THIS READS IS THE FILE AND NOT A BROWSER. No page is rendered, no script is executed
/// and no request is made, here or anywhere in this suite: <c>docs/testing.md</c> refuses the
/// apparatus that would. So what is asserted is the structure of the script as text: that the
/// call is carried, that the consequence is read from the register by name rather than pasted,
/// and that the function rendering the button is the function rendering the sentence. Whether
/// the web client runs it, and what the rendered view looks like, is not measured.
/// </para>
/// </remarks>
public class MappingTableOnThePageTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The paths the page has to carry, written as the strings the page carries, so a route
    /// that moved without the page moving fails rather than leaving the page calling nothing.
    /// The per-pairing paths are built from the first of them and the page carries that prefix.
    /// </summary>
    private const string PairingsPath = "ServerPairing/Administration/pairings";

    private const string WordingPath = "ServerPairing/Administration/wording";

    private const string MappingsSuffix = "/mappings";

    /// <summary>
    /// The destructive actions the page offers, each as the function that renders its button,
    /// the name under which its consequence is served, and the marker of the request it sends.
    /// One today, because removing a mapping is the one state-changing action on the plane.
    /// </summary>
    private static readonly (string RenderingFunction, string SendingFunction, string WordingName, string RequestMarker)[] DestructiveActions =
    {
        ("renderMappingTable", "removeMapping", nameof(DestructiveWording.RemoveMapping), "type: 'DELETE'"),
    };

    /// <summary>
    /// The page asks this plugin which pairings it holds and, for each, its mapping table.
    /// Without this the listing is a read nobody performs.
    /// </summary>
    [Fact]
    public void ThePageAsksForEveryPairingsMappingTable()
    {
        var page = Page();

        Assert.Contains("'" + PairingsPath + "'", page, StringComparison.Ordinal);
        Assert.Contains("'" + PairingsPath + "/'", page, StringComparison.Ordinal);
        Assert.Contains("'" + MappingsSuffix + "'", page, StringComparison.Ordinal);
        Assert.Contains("renderMappingTables", page, StringComparison.Ordinal);
        Assert.Contains("id=\"MappingTables\"", page, StringComparison.Ordinal);
    }

    /// <summary>
    /// The consequence text is read from the register through the wording action, by the name
    /// of the constant, and the page carries no copy of it. The copy is refused one file over
    /// by <c>CeremonyWordingTests</c>; this is the assertion that the page can still show it.
    /// </summary>
    [Fact]
    public void TheConsequenceIsReadFromTheRegisterRatherThanCarried()
    {
        var page = Page();

        Assert.Contains("'" + WordingPath + "'", page, StringComparison.Ordinal);

        foreach (var action in DestructiveActions)
        {
            Assert.Contains("wording.destructive." + action.WordingName, page, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentence(action.WordingName), page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Each destructive action the page offers has its consequence in the same view: the
    /// function that renders the button is the function that renders the sentence, so the
    /// sentence cannot be moved to a tooltip, a dialog or another page without this reddening.
    /// </summary>
    [Fact]
    public void EachDestructiveActionHasItsConsequenceInTheSameView()
    {
        var page = Page();

        foreach (var action in DestructiveActions)
        {
            var rendering = FunctionBody(page, action.RenderingFunction);

            Assert.Contains(action.SendingFunction + "(", rendering, StringComparison.Ordinal);
            Assert.Contains("wording.destructive." + action.WordingName, rendering, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The removal is sent as the method the endpoint table declares, at the per-user path, so
    /// a page that offered the button and sent a read would pass the case above and remove
    /// nothing.
    /// </summary>
    [Fact]
    public void EachDestructiveActionSendsTheRequestTheTableDeclares()
    {
        var page = Page();

        foreach (var action in DestructiveActions)
        {
            var sending = FunctionBody(page, action.SendingFunction);

            Assert.Contains(action.RequestMarker, sending, StringComparison.Ordinal);
            Assert.Contains("mappingsPath(pairingId)", sending, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The floor under the cases above. Each reads one file and one function body out of it,
    /// so a moved page or a renamed function would make them read an empty string, which the
    /// negative assertion above passes on.
    /// </summary>
    [Fact]
    public void TheRealPageAndItsFunctionsAreRead()
    {
        var page = Page();

        Assert.NotEmpty(page);
        Assert.Contains("ServerPairingConfigPage", page, StringComparison.Ordinal);
        Assert.NotEmpty(DestructiveActions);

        foreach (var action in DestructiveActions)
        {
            Assert.NotEmpty(FunctionBody(page, action.RenderingFunction));
            Assert.NotEmpty(FunctionBody(page, action.SendingFunction));
            Assert.NotEmpty(Sentence(action.WordingName));
        }
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
    /// The sentence a destructive-action constant holds, read off the register by name so the
    /// table above cannot name a constant that does not exist.
    /// </summary>
    /// <param name="name">The constant's name.</param>
    /// <returns>The sentence.</returns>
    private static string Sentence(string name)
    {
        var field = typeof(DestructiveWording).GetField(name);

        Assert.NotNull(field);

        return (string)field.GetRawConstantValue()!;
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
