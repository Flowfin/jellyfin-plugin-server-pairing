using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// The plugin ships one manifest per supported server line, and each of the two files
/// repeats the whole plugin because the packaging tool reads one manifest per package
/// and has no way to include another. Every field except the two that name the line is
/// therefore written twice, and a change that reaches one file and not the other leaves
/// two packages describing two different plugins under one identifier.
///
/// That has already happened once. Naming Flowfin as the manifest owner changed
/// build.yaml and left build.net10.0.yaml saying something else for six days, while the
/// second file's own opening comment said the two agreed on everything but the line.
/// </summary>
public class ManifestAgreementTests
{
    /// <summary>
    /// The file that marks the repository root. It is tracked and it is at the top of the
    /// tree, so a walk upwards from the build output finds it on any machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The manifest for the 10.11 server line, and the default name the packaging tool
    /// reads when it is given none.
    /// </summary>
    private const string DefaultManifest = "build.yaml";

    /// <summary>
    /// The manifest for the 12.0 server line.
    /// </summary>
    private const string SecondManifest = "build.net10.0.yaml";

    /// <summary>
    /// The fields that are allowed to differ, because they are what a server line is.
    /// A field that has to differ and is not named here fails this suite until somebody
    /// decides it belongs in the list, which is the point at which a second value under
    /// one plugin identifier is a choice rather than an omission.
    /// </summary>
    private static readonly string[] FieldsThatNameTheLine = { "targetAbi", "framework" };

    /// <summary>
    /// Both manifests describe the same plugin, so they carry the same fields. A field
    /// present in one and absent from the other is a package that declares something its
    /// sibling does not, which the packaging run cannot see because it reads one file.
    /// </summary>
    [Fact]
    public void BothManifestsCarryTheSameFields()
    {
        var first = Fields(DefaultManifest);
        var second = Fields(SecondManifest);

        Assert.Equal(
            first.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray(),
            second.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Nothing differs between the two manifests except the two fields that name the
    /// server line. This is the second file's own opening comment, asserted rather than
    /// stated, so the next edit that reaches one file and not the other is refused at the
    /// moment it is made rather than found in a catalog.
    /// </summary>
    [Fact]
    public void NothingDiffersExceptTheFieldsThatNameTheLine()
    {
        var first = Fields(DefaultManifest);
        var second = Fields(SecondManifest);

        var differing = first.Keys
            .Where(second.ContainsKey)
            .Where(k => !string.Equals(first[k], second[k], StringComparison.Ordinal))
            .OrderBy(k => k, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(FieldsThatNameTheLine.OrderBy(f => f, StringComparer.Ordinal).ToArray(), differing);
    }

    /// <summary>
    /// The two assertions above pass on an empty reading, which is what a renamed
    /// manifest or a changed comment prefix would produce. This fixes the floor: both
    /// files parse to a real set of fields, and the fields the other two assertions turn
    /// on are among them.
    /// </summary>
    [Fact]
    public void BothManifestsActuallyParse()
    {
        var first = Fields(DefaultManifest);
        var second = Fields(SecondManifest);

        Assert.NotEmpty(first);
        Assert.NotEmpty(second);

        foreach (var required in new[] { "guid", "version", "owner", "targetAbi", "framework", "artifacts" })
        {
            Assert.Contains(required, first.Keys);
            Assert.Contains(required, second.Keys);
        }
    }

    /// <summary>
    /// The exemption list is not empty and holds only what a server line is. Emptying it
    /// would turn the comparison above into an assertion that the two files are identical,
    /// which they cannot be; widening it silently is how a field stops being compared
    /// without anybody reading a diff.
    /// </summary>
    [Fact]
    public void OnlyTheLineFieldsAreExempt()
    {
        Assert.Equal(2, FieldsThatNameTheLine.Length);
        Assert.Contains("framework", FieldsThatNameTheLine);
        Assert.Contains("targetAbi", FieldsThatNameTheLine);
    }

    /// <summary>
    /// Reads a manifest into its top-level fields. This relies on the manifest's flat
    /// shape rather than parsing YAML, which is the same reading the pull-request hygiene
    /// script takes over the same files; a nested manifest would need a parser and would
    /// break both at once rather than silently here.
    /// </summary>
    /// <param name="fileName">The manifest file name, relative to the repository root.</param>
    /// <returns>The field name against the text of its value, with the value's own lines joined.</returns>
    private static Dictionary<string, string> Fields(string fileName)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        var current = string.Empty;

        foreach (var line in File.ReadAllLines(Path.Join(RepositoryRoot(), fileName)))
        {
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("---", StringComparison.Ordinal))
            {
                continue;
            }

            var separator = line.IndexOf(':', StringComparison.Ordinal);
            var startsField = separator > 0 && !char.IsWhiteSpace(line[0]) && line[0] != '-';

            if (startsField)
            {
                current = line[..separator];
                fields[current] = line[(separator + 1)..].Trim();
            }
            else if (current.Length > 0)
            {
                fields[current] = (fields[current] + " " + line.Trim()).Trim();
            }
        }

        return fields;
    }

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
                $"No directory at or above '{AppContext.BaseDirectory}' holds '{SolutionFileName}', so the manifest reading has no root to read.")
            : directory.FullName;
    }
}
