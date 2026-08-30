using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Mapping;

/// <summary>
/// The mapping table is the part of this plugin that decides where one person's data goes,
/// and every property here is about it being a decision somebody made rather than one the
/// plugin worked out.
/// </summary>
public class UserMappingTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string AnotherPairing = "0011223344556677889900aabbccddee";
    private const string Administrator = "administrator";
    private const string Peer = "peer";
    private const string LocalUser = "local-user-1";
    private const string PeerUser = "peer-user-1";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A mapping cannot exist without a pairing. An identifier nothing is held for is
    /// <see cref="PairingState.Absent"/>, and mapping a user under it is refused rather than
    /// stored against a pairing that does not exist.
    /// </summary>
    [Fact]
    public void AMappingCannotExistWithoutAPairing()
    {
        var mappings = new InMemoryUserMappings();
        var subject = new UserMappings(mappings, MachineWith(mappings));

        Assert.Equal(
            MappingOutcome.NoSuchPairing,
            subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));

        Assert.Empty(mappings.For(PairingId));
    }

    /// <summary>
    /// A revoked pairing keeps its record on purpose, so its state is readable rather than
    /// absent. Mapping under it is still refused, and with a different answer, because an
    /// administrator told only that something failed goes looking in the wrong place.
    /// </summary>
    [Fact]
    public void AMappingIsRefusedUnderARevokedPairing()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(PairingState.Revoked, machine.StateOf(PairingId));
        Assert.Equal(
            MappingOutcome.PairingIsOver,
            subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));

        Assert.Empty(mappings.For(PairingId));
    }

    /// <summary>
    /// The transition into <see cref="PairingState.Revoked"/> removes that pairing's
    /// mappings. This asserts on the transition rather than on the record being gone, because
    /// a revoked pairing keeps its record: a mapping table wired to record deletion would
    /// survive every revocation, which is the failure this property exists for.
    /// </summary>
    [Fact]
    public void ReachingRevokedRemovesThatPairingsMappings()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));
        Assert.Single(mappings.For(PairingId));

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(PairingState.Revoked, machine.StateOf(PairingId));
        Assert.Empty(mappings.For(PairingId));
        Assert.NotNull(machine.RecordOf(PairingId));
    }

    /// <summary>
    /// The transition into <see cref="PairingState.Absent"/> does the same. This is the path
    /// that is easier to forget, because nothing was revoked: an enrolment window expiring
    /// takes a half-built pairing to <c>Absent</c>, and an administrator may have mapped
    /// users before both confirmations were in.
    /// </summary>
    [Fact]
    public void ReachingAbsentRemovesThatPairingsMappings()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        Assert.Equal(MappingOutcome.Mapped, subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At));
        Assert.Single(mappings.For(PairingId));

        machine.Apply(PairingId, LocalEvent.WindowExpired, Administrator, At);

        Assert.Equal(PairingState.Absent, machine.StateOf(PairingId));
        Assert.Empty(mappings.For(PairingId));
        Assert.Null(machine.RecordOf(PairingId));
    }

    /// <summary>
    /// Ending one pairing takes its mappings and nobody else's. A sweep keyed on the wrong
    /// thing would pass every assertion above and empty the whole table.
    /// </summary>
    [Fact]
    public void EndingOnePairingLeavesAnotherPairingsMappingsAlone()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        Open(machine, AnotherPairing);
        subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At);
        subject.Map(AnotherPairing, LocalUser, PeerUser, "Anna", Administrator, At);

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Empty(mappings.For(PairingId));
        Assert.Single(mappings.For(AnotherPairing));
    }

    /// <summary>
    /// The display cache is removed with the mapping it belongs to, by both routes that
    /// remove one. A cache of peer usernames is personal data sitting next to a table that
    /// deliberately holds none, so it may not outlive the row it decorates.
    /// </summary>
    [Fact]
    public void TheDisplayCacheGoesWithTheMappingItBelongsTo()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        subject.Map(PairingId, LocalUser, PeerUser, "Anna Example", Administrator, At);

        Assert.Equal("Anna Example", subject.Of(PairingId, LocalUser)!.PeerDisplayName);

        subject.Unmap(PairingId, LocalUser);

        Assert.Null(subject.Of(PairingId, LocalUser));
        Assert.DoesNotContain(
            mappings.For(PairingId),
            m => m.PeerDisplayName.Contains("Anna", StringComparison.Ordinal));

        subject.Map(PairingId, LocalUser, PeerUser, "Anna Example", Administrator, At);
        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.DoesNotContain(
            mappings.For(PairingId),
            m => m.PeerDisplayName.Contains("Anna", StringComparison.Ordinal));
    }

    /// <summary>
    /// The sweep reached the store rather than the store having been empty already. A table
    /// that was never written and one that was emptied look the same from the outside, and
    /// only the second is this guard working.
    /// </summary>
    [Fact]
    public void EndingAPairingReachesTheMappingStore()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);

        Open(machine);

        var before = mappings.Sweeps;

        machine.Apply(PairingId, LocalEvent.AdministratorRevoked, Administrator, At);

        Assert.Equal(before + 1, mappings.Sweeps);
    }

    /// <summary>
    /// A mapping records who decided it and when, because a table saying Anna here is Anna
    /// there answers none of the questions asked after the wrong history arrives somewhere.
    /// </summary>
    [Fact]
    public void EveryMappingRecordsTheAdministratorWhoDecidedIt()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        subject.Map(PairingId, LocalUser, PeerUser, "Anna", Administrator, At);

        var mapping = subject.Of(PairingId, LocalUser);

        Assert.NotNull(mapping);
        Assert.Equal(Administrator, mapping!.Actor);
        Assert.Equal(At, mapping.At);
    }

    /// <summary>
    /// A user with no mapping is not synced, silently and by default. The answer for one is
    /// null rather than a guess, which is the fail-closed direction: the alternative is
    /// deciding which peer user somebody is from a name.
    /// </summary>
    [Fact]
    public void AnUnmappedUserHasNoMappingRatherThanAGuessedOne()
    {
        var mappings = new InMemoryUserMappings();
        var machine = MachineWith(mappings);
        var subject = new UserMappings(mappings, machine);

        Open(machine);
        subject.Map(PairingId, "local-user-2", "peer-user-2", "Anna", Administrator, At);

        Assert.Null(subject.Of(PairingId, LocalUser));
    }

    /// <summary>
    /// No code path creates a mapping without an explicit administrator action. There is one
    /// way into the store, it is <see cref="UserMappings.Map"/>, and it takes the
    /// administrator as a required argument, so nothing can reach a write without naming who
    /// decided.
    /// </summary>
    /// <remarks>
    /// Asserted over the plugin source rather than over behaviour, because what is being
    /// refused is the existence of a second route rather than the result of taking one. The
    /// day something calls the store directly this reds, which is the day the model quietly
    /// stops being an administrator decision.
    /// </remarks>
    [Fact]
    public void NoPluginSourceFileWritesAMappingOutsideTheDecisionSurface()
    {
        var offenders = Reaching(".Put(")
            .Where(o => !o.StartsWith("UserMappings.cs:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(Array.Empty<string>(), offenders);
    }

    /// <summary>
    /// Only the state machine sweeps a pairing's mappings. The sweep is what makes a mapping
    /// unable to outlive its pairing, and it belongs at the transition rather than anywhere a
    /// caller might decide to call it.
    /// </summary>
    /// <remarks>
    /// A second sweep site is how this property dies quietly: a caller that empties the table
    /// when it thinks a pairing has ended makes the removal depend on that caller being right,
    /// and the state machine is the one type that knows.
    /// </remarks>
    [Fact]
    public void OnlyTheStateMachineSweepsAPairingsMappings()
    {
        var offenders = Reaching(".RemoveEvery(")
            .Where(o => !o.StartsWith("PairingStateMachine.cs:", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(Array.Empty<string>(), offenders);
        Assert.NotEmpty(Reaching(".RemoveEvery("));
    }

    /// <summary>
    /// Nothing in the plugin infers a mapping from how alike two names are. This is the
    /// mechanism behind the story this whole model exists to prevent, and it is refused by
    /// name rather than left to review.
    /// </summary>
    [Fact]
    public void NoPluginSourceFileMatchesUsersByName()
    {
        var inference = new[]
        {
            "Levenshtein", "FuzzyMatch", "SimilarityOf", "BestMatchFor", "MatchUsersByName",
        };

        var offenders = new List<string>();

        foreach (var file in SourceFiles(PluginSourceDirectory()))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                foreach (var call in inference.Where(c => lines[i].Contains(c, StringComparison.Ordinal)))
                {
                    offenders.Add(
                        Path.GetFileName(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture) + ": " + call);
                }
            }
        }

        Assert.Equal(Array.Empty<string>(), offenders.OrderBy(o => o, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The two scans above pass trivially if the walk finds nothing, which is what happens
    /// the day somebody moves a project. This fixes the floor.
    /// </summary>
    [Fact]
    public void TheScanActuallyReadsFiles()
    {
        var files = SourceFiles(PluginSourceDirectory());

        Assert.NotEmpty(files);
        Assert.Contains(files, f => Path.GetFileName(f) == "UserMappings.cs");
        Assert.Contains(files, f => Path.GetFileName(f) == "PairingStateMachine.cs");
    }

    /// <summary>
    /// A state machine cannot be built without somewhere to put the mappings, so there is no
    /// way to construct one that ends pairings and leaves their mappings behind.
    /// </summary>
    [Fact]
    public void AStateMachineCannotBeBuiltWithoutAMappingStore()
        => Assert.Throws<ArgumentNullException>(() => new PairingStateMachine(new NoRecords(), null!));

    private static PairingStateMachine MachineWith(IUserMappingStore mappings)
        => new PairingStateMachine(new InMemoryRecords(), mappings);

    private static void Open(PairingStateMachine machine, string pairingId = PairingId)
    {
        machine.Apply(pairingId, LocalEvent.WindowOpened, Administrator, At);
        machine.Receive(pairingId, PairingMessage.Hello, OfferedKey.NotApplicable, Peer, At);
    }

    /// <summary>
    /// Every line of the plugin source that reaches one member of the mapping store through a
    /// held reference to one.
    /// </summary>
    /// <param name="call">The member, written as it appears at a call site.</param>
    /// <returns>One entry per occurrence, as file and line number.</returns>
    /// <remarks>
    /// The field name is required on the line as well as the member name, so that a method
    /// called <c>Put</c> on some other type is not read as a write to this store. That is the
    /// bound on this scan: it reads text rather than resolving a symbol, so a call spread
    /// across two lines is invisible to it. It catches the shape somebody actually writes.
    /// </remarks>
    private static string[] Reaching(string call)
    {
        var found = new List<string>();

        foreach (var file in SourceFiles(PluginSourceDirectory()))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains("_mappings", StringComparison.Ordinal)
                    && lines[i].Contains(call, StringComparison.Ordinal))
                {
                    found.Add(Path.GetFileName(file) + ":" + (i + 1).ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return found.OrderBy(f => f, StringComparer.Ordinal).ToArray();
    }

    private static string[] SourceFiles(string directory)
        => Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(Segment("obj"), StringComparison.Ordinal))
            .Where(f => !f.Contains(Segment("bin"), StringComparison.Ordinal))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    private static string Segment(string name)
        => $"{Path.DirectorySeparatorChar}{name}{Path.DirectorySeparatorChar}";

    private static string PluginSourceDirectory()
        => Path.Join(RepositoryRoot(), "Jellyfin.Plugin.ServerPairing");

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
            && !File.Exists(Path.Join(directory.FullName, "Jellyfin.Plugin.ServerPairing.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("The repository root was not found above the test assembly.");
    }

    private sealed class NoRecords : IPairingRecordStore
    {
        public PairingRecord? Read(string pairingId) => null;

        public void Write(PairingRecord record)
        {
        }

        public void Delete(string pairingId)
        {
        }
    }

    private sealed class InMemoryRecords : IPairingRecordStore
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
