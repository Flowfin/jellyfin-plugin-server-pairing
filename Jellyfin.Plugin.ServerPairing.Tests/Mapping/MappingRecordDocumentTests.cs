using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// Ties every field of the stored mapping record to a row in <c>docs/data.md</c>, and ties
/// every row to the end of the pairing it belongs to.
///
/// The mapping table is the personal data this plugin holds, so what is in it is a statement
/// somebody has to be able to read and rely on. Issue #41's first done condition asks that
/// the type and the field list agree, asserted by a walk that fails on an undocumented
/// member; its third asks that revoking a pairing removes every field the list names.
///
/// The document is the subject rather than the source. A test that read the type and printed
/// a table would agree with itself forever; these read a file a person wrote and fail when
/// the two disagree, in either direction.
/// </summary>
public class MappingRecordDocumentTests
{
    /// <summary>
    /// The file that marks the repository root, found the same way
    /// <see cref="ConfigurationDocumentTests"/> finds it and for the same reason: a
    /// deterministic build rewrites the path the compiler recorded for this file, so a
    /// compiler-supplied path is a real directory on one machine and on no build machine.
    /// </summary>
    private const string SolutionFileName = "Jellyfin.Plugin.ServerPairing.sln";

    /// <summary>
    /// The document this guard judges.
    /// </summary>
    private const string DocumentPath = "docs/data.md";

    /// <summary>
    /// The header of the one table in that document this guard reads. The table is found by
    /// its header rather than by its position, so a section added above it moves nothing.
    /// </summary>
    private const string TableHeader = "| Field | What it is | Why it is held | When it goes |";

    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string Administrator = "administrator";
    private const string Peer = "peer";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A field the document carries no row for is refused. That is the direction the
    /// condition is written for: a field added to the record is a field of personal data
    /// this plugin holds and nobody wrote down.
    /// </summary>
    [Fact]
    public void EveryStoredFieldHasARow()
    {
        var documented = Rows().Select(row => row.Field).ToHashSet(StringComparer.Ordinal);

        var undocumented = StoredFields()
            .Select(field => field.Name)
            .Where(name => !documented.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), undocumented);
    }

    /// <summary>
    /// The other direction, which is the half that goes stale silently. A row naming a field
    /// the record no longer has describes data that is not held, and a personal-data
    /// statement is read as current whether or not it is.
    /// </summary>
    [Fact]
    public void EveryRowNamesAStoredField()
    {
        var fields = StoredFields().Select(field => field.Name).ToHashSet(StringComparer.Ordinal);

        var orphans = Rows()
            .Select(row => row.Field)
            .Where(name => !fields.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), orphans);
    }

    /// <summary>
    /// A row with an empty cell documents nothing while looking like documentation. The
    /// deletion cell is the one this issue names specifically - every stored field owes an
    /// answer to when it goes - and the other two are required with it, because a field
    /// whose deletion is written and whose purpose is not is a field nobody can argue about
    /// holding.
    /// </summary>
    [Fact]
    public void EveryRowSaysWhatItIsWhyItIsHeldAndWhenItGoes()
    {
        var incomplete = Rows()
            .Where(row => row.What.Length == 0 || row.Why.Length == 0 || row.When.Length == 0)
            .Select(row => row.Field)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(Array.Empty<string>(), incomplete);
    }

    /// <summary>
    /// The floor under the three above. Each of them compares two sets and passes when both
    /// are empty, so a document that could not be found, a table header that was renamed, or
    /// a reflection walk that reached nothing would turn the whole guard off while leaving
    /// it green.
    /// </summary>
    [Fact]
    public void TheDocumentAndTheRecordAreBothActuallyRead()
    {
        Assert.NotEmpty(StoredFields());
        Assert.NotEmpty(Rows());
        Assert.Contains(
            StoredFields(),
            field => string.Equals(field.Name, nameof(UserMapping.PeerDisplayName), StringComparison.Ordinal));
        Assert.Contains(
            Rows(),
            row => string.Equals(row.Field, nameof(UserMapping.PeerDisplayName), StringComparison.Ordinal));
    }

    /// <summary>
    /// Issue #41's third condition. Every field the document lists is gone once the pairing
    /// ends, asserted by the value rather than by the row count.
    /// </summary>
    /// <remarks>
    /// Each field is given a value that occurs nowhere else, and after the pairing is revoked
    /// every mapping the surface still answers with is searched for every one of them. An
    /// empty table passes trivially, so the case asserts the mapping was there first and that
    /// the store was actually swept rather than never written.
    /// <para>
    /// Driven by the documented field list rather than by a hand-written set, so a seventh
    /// field with a row of its own is searched for without this case being touched. What it
    /// would catch is a store that keeps one field beside the record - a display cache in its
    /// own index is the shape that turns up in practice - which the row count going to zero
    /// cannot see.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoStoredFieldSurvivesTheEndOfAPairing()
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(new Records(), mappings);
        var subject = new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance);

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);

        Assert.Equal(
            MappingOutcome.Mapped,
            subject.Map(PairingId, "local-3a7f", "peer-c41d", "Anna-6b2e", "administrator-9d05", At));

        var written = Assert.Single(mappings.For(PairingId));
        var values = Rows().ToDictionary(row => row.Field, row => Value(written, row.Field), StringComparer.Ordinal);

        // The setup is asserted before the guard is. A value that came out empty would make
        // the search below find nothing for a reason that has nothing to do with the sweep.
        Assert.All(values, entry => Assert.NotEqual(string.Empty, entry.Value));

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(1, mappings.Sweeps);

        var survivors = new List<string>();

        foreach (var mapping in mappings.For(PairingId))
        {
            foreach (var entry in values.Where(e => Holds(mapping, e.Value)))
            {
                survivors.Add(entry.Key);
            }
        }

        Assert.Equal(Array.Empty<string>(), survivors.OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// Every field the record stores: a public instance property with a value a reader can
    /// take. The record is immutable, so the settable-property walk the configuration guard
    /// uses would find none of them.
    /// </summary>
    /// <returns>The properties, ordered by name.</returns>
    private static PropertyInfo[] StoredFields()
        => typeof(UserMapping)
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead)
            .Where(property => property.GetIndexParameters().Length == 0)
            .OrderBy(property => property.Name, StringComparer.Ordinal)
            .ToArray();

    /// <summary>
    /// One field's value on one mapping, written the way a store would have to write it.
    /// </summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="field">The field's name.</param>
    /// <returns>The value as text, empty where the field holds nothing.</returns>
    private static string Value(UserMapping mapping, string field)
    {
        var property = typeof(UserMapping).GetProperty(field, BindingFlags.Instance | BindingFlags.Public);

        return property?.GetValue(mapping) switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            var other => other.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    /// Whether any field of a mapping carries a value.
    /// </summary>
    /// <param name="mapping">The mapping.</param>
    /// <param name="value">The value looked for.</param>
    /// <returns>True where some field of it holds that value.</returns>
    private static bool Holds(UserMapping mapping, string value)
        => StoredFields().Any(field => string.Equals(Value(mapping, field.Name), value, StringComparison.Ordinal));

    /// <summary>
    /// The rows of the field table in the document, in the order they are written.
    /// </summary>
    /// <returns>One entry per row, with the header and the alignment row skipped.</returns>
    private static Row[] Rows()
    {
        var lines = File.ReadAllLines(Path.Join(RepositoryRoot(), DocumentPath));
        var header = Array.FindIndex(lines, line => string.Equals(line.Trim(), TableHeader, StringComparison.Ordinal));

        if (header < 0)
        {
            throw new InvalidOperationException(
                "'" + DocumentPath + "' carries no line reading '" + TableHeader
                + "', so the field table cannot be found and this guard would otherwise pass on an empty set.");
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
    /// The cells of one table row, with the code spans a document writes a field name in
    /// removed, so the comparison is against the name rather than against how it was marked
    /// up.
    /// </summary>
    /// <param name="line">The row.</param>
    /// <returns>The cell contents.</returns>
    private static string[] Cells(string line)
        => line.Trim().Trim('|')
            .Split('|')
            .Select(cell => cell.Trim().Trim('`').Trim())
            .ToArray();

    /// <summary>
    /// The repository root, found by walking up from the directory the test assembly was
    /// loaded from until the solution file appears.
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

    /// <summary>
    /// One row of the field table.
    /// </summary>
    /// <param name="Field">The field's name.</param>
    /// <param name="What">What it is.</param>
    /// <param name="Why">Why it is held.</param>
    /// <param name="When">When it goes.</param>
    private sealed record Row(string Field, string What, string Why, string When);

    /// <summary>
    /// A record store held in memory, so the state machine has somewhere to keep a pairing.
    /// </summary>
    private sealed class Records : IPairingRecordStore
    {
        private readonly Dictionary<string, PairingRecord> _held =
            new Dictionary<string, PairingRecord>(StringComparer.Ordinal);

        public PairingRecord? Read(string pairingId)
            => _held.TryGetValue(pairingId, out var record) ? record : null;

        public void Write(PairingRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            _held[record.PairingId] = record;
        }

        public void Delete(string pairingId) => _held.Remove(pairingId);
    }
}
