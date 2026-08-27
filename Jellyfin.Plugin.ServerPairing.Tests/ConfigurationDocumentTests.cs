using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests;

/// <summary>
/// Ties every setting on the configuration object to a row in
/// <c>docs/configuration.md</c> naming its type, its default and its range.
///
/// The second done condition of issue #50 asks for a walk over the configuration type
/// that fails on a setting with no documented range. It is worth having on the day the
/// first real setting arrives rather than after it: an undocumented setting is only ever
/// noticed by an operator who set it and got a behaviour nobody wrote down, and by then
/// the value is in a configuration file on a running server.
///
/// The document is the subject rather than the source. A test that read the type and
/// printed a table would agree with itself forever; this one reads a file a person wrote
/// and fails when the two disagree, in either direction.
/// </summary>
public class ConfigurationDocumentTests
{
    /// <summary>
    /// The file that marks the repository root, found the same way
    /// <see cref="ClockSourceTests"/> finds it and for the same reason: a deterministic
    /// build rewrites the path the compiler recorded for this file, so a
    /// compiler-supplied path is a real directory on one machine and on no build machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The document this guard judges.
    /// </summary>
    private const string DocumentPath = "docs/configuration.md";

    /// <summary>
    /// The header of the one table in that document this guard reads. The table is found
    /// by its header rather than by its position, so a section added above it moves
    /// nothing.
    /// </summary>
    private const string TableHeader = "| Setting | Type | Default | Range |";

    /// <summary>
    /// The first half of issue #50's second done condition. A setting the document does
    /// not carry a row for is refused.
    /// </summary>
    [Fact]
    public void EverySettingOnTheConfigurationHasARow()
    {
        var documented = Rows().Select(row => row.Setting).ToHashSet(StringComparer.Ordinal);

        var undocumented = Settings()
            .Select(setting => setting.Name)
            .Where(name => !documented.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), undocumented);
    }

    /// <summary>
    /// The other direction, which is the half that goes stale silently. A setting removed
    /// from the type leaves a row describing something an operator cannot set, and a
    /// document naming a setting that does not exist is worse than one naming none,
    /// because it is read as current.
    /// </summary>
    [Fact]
    public void EveryRowNamesASettingOnTheConfiguration()
    {
        var settings = Settings().Select(setting => setting.Name).ToHashSet(StringComparer.Ordinal);

        var orphans = Rows()
            .Select(row => row.Setting)
            .Where(name => !settings.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), orphans);
    }

    /// <summary>
    /// A row with an empty cell documents nothing while looking like documentation, which
    /// is the state the condition's own words "documented range" are about. All three
    /// cells are required, because a setting whose default is written and whose range is
    /// not is exactly as unusable to an operator as one with neither.
    /// </summary>
    [Fact]
    public void EveryRowDocumentsATypeADefaultAndARange()
    {
        var incomplete = Rows()
            .Where(row => row.Type.Length == 0 || row.Default.Length == 0 || row.Range.Length == 0)
            .Select(row => row.Setting)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), incomplete);
    }

    /// <summary>
    /// The documented type is the declared one. This is the cheapest of the five to keep
    /// true and the easiest to get wrong in a way nobody sees: a setting widened from a
    /// count to a duration keeps its name, and a row still saying <c>int</c> tells an
    /// operator to write a number where the plugin now reads something else.
    /// </summary>
    [Fact]
    public void EveryDocumentedTypeIsTheTypeTheSettingHas()
    {
        var declared = Settings().ToDictionary(
            setting => setting.Name,
            setting => Spelt(setting.PropertyType),
            StringComparer.Ordinal);

        var wrong = Rows()
            .Where(row => declared.ContainsKey(row.Setting))
            .Where(row => !string.Equals(declared[row.Setting], row.Type, StringComparison.Ordinal))
            .Select(row => row.Setting + ": document says " + row.Type + ", type says " + declared[row.Setting])
            .OrderBy(entry => entry, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), wrong);
    }

    /// <summary>
    /// The documented default is the one the type produces.
    ///
    /// This is the assertion that makes the table evidence rather than prose. A default is
    /// the value a server runs on when an operator has set nothing, so a document
    /// disagreeing with the constructor describes a server that does not exist. The
    /// configuration is constructed here the way the host constructs it, with no
    /// arguments, so what is compared is what a fresh installation gets.
    /// </summary>
    [Fact]
    public void EveryDocumentedDefaultIsTheDefaultTheTypeProduces()
    {
        var fresh = new PluginConfiguration();
        var settings = Settings();
        var wrong = new List<string>();

        foreach (var row in Rows())
        {
            var property = settings.FirstOrDefault(p => string.Equals(p.Name, row.Setting, StringComparison.Ordinal));
            if (property is null)
            {
                continue;
            }

            var produced = Rendered(property.GetValue(fresh));
            if (!string.Equals(produced, row.Default, StringComparison.Ordinal))
            {
                wrong.Add(row.Setting + ": document says " + row.Default + ", type produces " + produced);
            }
        }

        Assert.Equal(Array.Empty<string>(), wrong.OrderBy(entry => entry, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The floor under all five. Every assertion above compares two sets and passes when
    /// both are empty, so a document that could not be found, a table header that was
    /// renamed, or a reflection walk that reached nothing would turn the whole guard off
    /// while leaving it green.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheTypeAreBothActuallyRead()
    {
        Assert.NotEmpty(Settings());
        Assert.NotEmpty(Rows());
        Assert.Contains(Settings(), setting => string.Equals(setting.Name, nameof(PluginConfiguration.AnInteger), StringComparison.Ordinal));
        Assert.Contains(Rows(), row => string.Equals(row.Setting, nameof(PluginConfiguration.AnInteger), StringComparison.Ordinal));
    }

    /// <summary>
    /// Every setting on the configuration object: a public instance property the host's
    /// serialiser both reads and writes. Inherited members count, because a setting on a
    /// base class is written to the same file and served to the same page as one declared
    /// here.
    /// </summary>
    /// <returns>The properties, ordered by name.</returns>
    private static PropertyInfo[] Settings()
        => typeof(PluginConfiguration)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.CanWrite)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// The rows of the settings table in the document, in the order they are written.
    /// </summary>
    /// <returns>One entry per row, with the header and the alignment row skipped.</returns>
    private static Row[] Rows()
    {
        var lines = File.ReadAllLines(Path.Combine(RepositoryRoot(), DocumentPath));
        var header = Array.FindIndex(lines, line => string.Equals(line.Trim(), TableHeader, StringComparison.Ordinal));

        if (header < 0)
        {
            throw new InvalidOperationException(
                "'" + DocumentPath + "' carries no line reading '" + TableHeader
                + "', so the settings table cannot be found and this guard would otherwise pass on an empty set.");
        }

        var rows = new List<Row>();

        // The header, then the alignment row, then the rows. The table ends at the first
        // line that is not one, which is how a section written under it stays out.
        for (var i = header + 2; i < lines.Length && lines[i].TrimStart().StartsWith('|'); i++)
        {
            var cells = Cells(lines[i]);
            if (cells.Length == 4)
            {
                rows.Add(new Row(cells[0], cells[1], cells[2], cells[3]));
            }
        }

        return rows.ToArray();
    }

    /// <summary>
    /// The cells of one table row, with the code spans a document writes a name and a
    /// value in removed, so the comparison is against the value rather than against how it
    /// was marked up.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <returns>The cell contents.</returns>
    private static string[] Cells(string line)
        => line.Trim().Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Trim('`').Trim())
            .ToArray();

    /// <summary>
    /// How a value is written in the document. Culture is fixed, because a default read
    /// back on a machine with a comma for a decimal point is the same value and a
    /// different string, and a guard that reds on the operator's locale is a guard that
    /// gets deleted.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The spelling the document is expected to carry.</returns>
    private static string Rendered(object? value) => value switch
    {
        null => "(unset)",
        bool flag => flag ? "true" : "false",

        // An empty default is written as a word rather than as an empty cell. A row whose
        // default cell is blank documents nothing while looking like documentation, which is
        // the state EveryRowDocumentsATypeADefaultAndARange refuses, so a setting whose safe
        // value is the empty string would otherwise be undocumentable rather than documented.
        string { Length: 0 } => "(empty)",
        string text => text,
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => "(unwritable)",
    };

    /// <summary>
    /// How a type is written in the document, which is how it is written in the source.
    /// </summary>
    /// <param name="type">The type.</param>
    /// <returns>The keyword where the language has one, otherwise the type's own name.</returns>
    private static string Spelt(Type type)
    {
        if (type == typeof(bool))
        {
            return "bool";
        }

        if (type == typeof(int))
        {
            return "int";
        }

        if (type == typeof(string))
        {
            return "string";
        }

        return type.Name;
    }

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException(
                "No directory at or above '" + AppContext.BaseDirectory + "' holds '" + SolutionFileName
                + "', so '" + DocumentPath + "' has no root to be found under.")
            : directory.FullName;
    }

    /// <summary>
    /// What a row says about one setting.
    /// </summary>
    /// <param name="Setting">The setting's name.</param>
    /// <param name="Type">The type the row claims the setting has.</param>
    /// <param name="Default">The value the row claims the setting defaults to.</param>
    /// <param name="Range">The values the row says are accepted.</param>
    private sealed record Row(string Setting, string Type, string Default, string Range);
}
