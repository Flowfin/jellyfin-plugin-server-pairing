using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.KeyStore;

/// <summary>
/// Holds in place the one sentence in <c>docs/keystore.md</c> that says what this plugin
/// cannot see.
///
/// Issue #33's second done condition asked that a restored older generation be refused by the
/// peer. The decision recorded on that issue removed the generation from the signed field set,
/// on the ground that a peer which was itself restored presents valid signatures over a state
/// it legitimately holds, so there is no refusal for a case to assert. What replaced the clause
/// is that the document states the case as undetectable, and that a check holds the statement
/// there.
///
/// The failure this exists against is the statement leaving quietly. An admission that
/// something is not covered reads as a gap somebody could tidy away, and a document that has
/// stopped admitting it looks exactly like one that never had to. Every other sentence in the
/// tree can be edited out without a suite noticing; this one cannot.
/// </summary>
public class KeyStoreDocumentTests
{
    /// <summary>
    /// The file that marks the repository root, found the same way
    /// <see cref="Jellyfin.Plugin.ServerPairing.Tests.Mapping.MappingRecordDocumentTests"/>
    /// finds it and for the same reason: a deterministic build rewrites the path the compiler
    /// recorded for this file, so a compiler-supplied path is a real directory on one machine
    /// and on no build machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The document this guard judges.
    /// </summary>
    private const string DocumentPath = "docs/keystore.md";

    /// <summary>
    /// The heading of the section the restored and copied store cases are argued under. The
    /// section is found by its heading rather than by its position, so a section written above
    /// it moves nothing, and the claim is required to stand here rather than anywhere in the
    /// file: a sentence moved out of the argument it belongs to is read by nobody who needs it.
    /// </summary>
    private const string SectionHeading = "## A file that is there and is not a key store";

    /// <summary>
    /// The claim itself, with the emphasis it is written in. Unbolding it is a softening rather
    /// than a formatting change, which is why the markers are part of what is held.
    /// </summary>
    private const string Claim =
        "**a peer that was restored behind this server's back is not detectable from here**";

    /// <summary>
    /// The clause that says which kind of statement the claim is. Without it the sentence can
    /// stand while a later paragraph implies a mechanism covers the case, which is the reading
    /// the decision on #33 refuses.
    /// </summary>
    private const string StatedAsUndetectable =
        "stated as undetectable rather than covered by a mechanism that implies otherwise";

    /// <summary>
    /// The document says a peer restored behind this server's back is not detectable from here,
    /// in the section where a reader meets the restored and copied store cases. Delete the
    /// sentence and this reddens, which is the whole of what the rewritten condition asks.
    /// </summary>
    [Fact]
    public void TheDocumentStatesThatARestoredPeerIsNotDetectableFromHere()
        => Assert.Contains(Claim, Section(), StringComparison.Ordinal);

    /// <summary>
    /// The claim is stated as undetectable rather than as covered. This is the half that would
    /// survive a well-meant edit: the sentence can be kept word for word while the qualifier
    /// beside it goes, and what is left then reads as a limitation somebody is working on.
    /// </summary>
    [Fact]
    public void TheClaimIsStatedAsUndetectableRatherThanCoveredByAMechanism()
        => Assert.Contains(StatedAsUndetectable, Section(), StringComparison.Ordinal);

    /// <summary>
    /// The floor under both cases above. Each of them searches a string, so a document that
    /// could not be read, a heading that was renamed, or a section that came back empty would
    /// turn the guard off rather than failing it, and the two would go on passing over nothing.
    /// </summary>
    [Fact]
    public void TheDocumentAndItsSectionAreBothActuallyRead()
    {
        Assert.True(
            File.Exists(Path.Join(RepositoryRoot(), DocumentPath)),
            DocumentPath + " does not exist, so nothing was searched.");

        Assert.NotEqual(string.Empty, Section());
        Assert.Contains("Each of those parses, carries the envelope", Section(), StringComparison.Ordinal);
    }

    /// <summary>
    /// The section the claim has to stand in, as one line of text with every run of whitespace
    /// collapsed to a single space. The claim is written across a line break in the file, so a
    /// search over the raw bytes would fail on a re-wrap that changed nothing, and this guard is
    /// about the words rather than about where the paragraph happens to break.
    /// </summary>
    /// <returns>The section's text, or the empty string where the heading is not there.</returns>
    private static string Section()
    {
        var lines = File.ReadAllLines(Path.Join(RepositoryRoot(), DocumentPath));
        var heading = Array.FindIndex(
            lines,
            line => string.Equals(line.TrimEnd(), SectionHeading, StringComparison.Ordinal));

        if (heading < 0)
        {
            return string.Empty;
        }

        var body = new List<string>();

        for (var i = heading + 1; i < lines.Length && !lines[i].StartsWith("## ", StringComparison.Ordinal); i++)
        {
            body.Add(lines[i]);
        }

        return string.Join(' ', body.SelectMany(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)));
    }

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was loaded
    /// from until the solution file appears.
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
                "No directory at or above '" + AppContext.BaseDirectory + "' holds '" + SolutionFileName
                + "', so '" + DocumentPath + "' has no root to be found under.")
            : directory.FullName;
    }
}
