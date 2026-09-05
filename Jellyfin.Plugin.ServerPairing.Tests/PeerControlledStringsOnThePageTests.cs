using System;
using System.IO;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Issue #52's subject on the page that now carries it: a string the peer chose, rendered in an
/// administrator's session, on the same origin and with full privilege.
/// </summary>
/// <remarks>
/// The page was free of peer-controlled values until the mapping table landed. It renders the
/// cached peer display name now, so the rule that every value out of an endpoint is text has a
/// hostile string to be about rather than only a habit learned on this server's own identifiers.
/// <para>
/// WHAT THIS ADDS TO THE GUARD BESIDE IT. <c>EnrolmentWindowOnThePageTests</c> refuses the four
/// sinks that parse a string as markup, and that list is not repeated here: two lists holding one
/// subject drift, and the case below is a different subject. A string becomes dangerous on this
/// page by two routes, and only one of them is markup. The other is a string the browser executes
/// or fetches without any element being parsed - an <c>href</c>, a <c>src</c>, an attribute set by
/// name, or a call that compiles text. A page rendering the peer address as a link would take a
/// peer-controlled string into an <c>href</c>, <c>javascript:</c> would run in the administrator's
/// session, and every markup sink would still be absent. That is the one-character-shaped mistake
/// this refuses, and it is the value issue #52's body names first.
/// </para>
/// <para>
/// WHAT THIS READS IS THE FILE AND NOT A BROWSER. No page is rendered, no script is executed and
/// no request is made, here or anywhere in this suite: <c>docs/testing.md</c> refuses the
/// apparatus that would. So this does NOT discharge issue #52's second done condition, which asks
/// for a peer display name and a peer username carrying markup to be fed through the rendering
/// path and the output asserted escaped. Nothing here feeds anything through anything. What is
/// asserted is that the page carries no route by which such a string could become code, and that
/// the two helpers every rendered value passes through assign it as text.
/// </para>
/// </remarks>
public class PeerControlledStringsOnThePageTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The field of the mapping listing that the peer chooses. It is the cached display name the
    /// peer sent, so it is the one value on this page whose bytes an attacker picks.
    /// </summary>
    private const string PeerControlledField = "mapping.peerUserShownAs";

    /// <summary>
    /// The helpers every rendered value goes through. Each has to assign its argument as text,
    /// because a value that reaches the document by any other means on this page reaches it
    /// through one of these two functions.
    /// </summary>
    private static readonly string[] RenderingHelpers = { "cell", "paragraph" };

    /// <summary>
    /// The ways a string on this page becomes something the browser executes or fetches, without
    /// any of it being parsed as markup. Each is absent today, so each is a refusal of a change
    /// somebody would otherwise make rather than a description of the page.
    /// </summary>
    /// <remarks>
    /// <c>Function(</c> is capitalised and so does not match a function expression, which this
    /// page has many of and which is spelled in lower case. The two URL properties are refused
    /// whatever the string in them, rather than only where it came from an endpoint: which values
    /// are peer-controlled changes with every field the plane learns to answer, and a guard that
    /// had to know would be re-argued each time one does.
    /// </remarks>
    private static readonly string[] ExecutionSinks =
    {
        "eval(",
        "Function(",
        "setAttribute(",
        "javascript:",
        ".href",
        ".src",
        "srcdoc",
    };

    /// <summary>
    /// Nothing on this page turns a string into code the browser runs or a URL it acts on. This
    /// is the refusal the markup guard does not make: every sink below leaves the document's
    /// markup untouched and is a cross-site scripting path into an administrator's session all
    /// the same.
    /// </summary>
    [Fact]
    public void NoStringOnThisPageBecomesCodeOrAUrlTheBrowserActsOn()
    {
        var page = Page();

        foreach (var sink in ExecutionSinks)
        {
            Assert.DoesNotContain(sink, page, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The peer's string is on this page and reaches the document as text. Without the first
    /// half the case above asserts a property of a page that renders nothing a peer chose, which
    /// is what every guard here did until the mapping table landed and is a state this must not
    /// silently return to.
    /// </summary>
    [Fact]
    public void ThePeerControlledStringIsRenderedThroughAHelperThatAssignsText()
    {
        var page = Page();

        Assert.Contains(PeerControlledField, page, StringComparison.Ordinal);
        Assert.Contains("cell(row, " + PeerControlledField + ")", page, StringComparison.Ordinal);

        foreach (var helper in RenderingHelpers)
        {
            var body = FunctionBody(page, helper);

            Assert.Contains(".textContent = ", body, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The floor under the two above. Each reads one file, and a moved page or a renamed helper
    /// would make them read an empty string, which the negative assertions pass on.
    /// </summary>
    [Fact]
    public void TheRealPageAndItsHelpersAreRead()
    {
        var page = Page();

        Assert.NotEmpty(page);
        Assert.Contains("ServerPairingConfigPage", page, StringComparison.Ordinal);
        Assert.NotEmpty(ExecutionSinks);
        Assert.NotEmpty(RenderingHelpers);

        foreach (var helper in RenderingHelpers)
        {
            Assert.NotEmpty(FunctionBody(page, helper));
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

        Assert.NotNull(directory);

        return directory.FullName;
    }
}
