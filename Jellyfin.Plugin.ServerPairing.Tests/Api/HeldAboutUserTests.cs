using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Jellyfin.Plugin.ServerPairing.Tests.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The report half of issue #60: what this plugin holds about one local user, across every
/// pairing, and the audit entry that says somebody asked.
/// </summary>
/// <remarks>
/// The case this file is named for is the one the issue's own notes warn against by name. The
/// mapping store is readable per pairing, so a report for a person is a walk over pairings, and
/// the plugin has two enumerations of them: the key store's, which is every pairing holding key
/// material, and the record store's, which is every pairing a record exists for. A pairing that
/// has not finished enrolling holds no key and may hold a mapping, so a report walked over the
/// key store answers nothing for a user mapped only under such a pairing and looks exactly like a
/// report for a user mapped nowhere. <see cref="APairingThatHoldsNoKeyIsStillInTheReport"/> is
/// the case that reddens on that walk and stays green on the other.
/// <para>
/// WHAT IS NOT ASSERTED HERE. Nothing runs on a server: the host's routing, its elevation policy
/// and its authentication handler are all absent, so the principal every case hands in is one the
/// case built, carrying the claim <c>docs/endpoints.md</c> reads the host setting rather than one
/// the host set. Whether a real request arrives with that claim is a reading of the server's
/// source at two tags, not a measurement made here.
/// </para>
/// </remarks>
public class HeldAboutUserTests
{
    private const string LocalUser = "local-user-anna";
    private const string OtherLocalUser = "local-user-bea";
    private const string PeerUser = "peer-user-7";
    private const string OtherPeerUser = "peer-user-9";
    private const string DisplayName = "Anna Example";
    private const string Administrator = "3f2c9a1e4b5d4c6f8a7b9c0d1e2f3a4b";
    private const string ActivePairing = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string PendingPairing = "0011223344556677889900aabbccddee";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 3, 6, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The second done condition of issue #60. A user mapped under two pairings is reported under
    /// both, another user's mapping is not in the answer, and each entry carries the pairing, the
    /// peer identifier and the cached name.
    /// </summary>
    [Fact]
    public void TheReportForAUserCoversEveryPairingTheUserIsMappedOn()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));
        mappings.Put(new UserMapping(PendingPairing, LocalUser, OtherPeerUser, string.Empty, "an-administrator", At));
        mappings.Put(new UserMapping(ActivePairing, OtherLocalUser, "peer-user-8", "Bea Example", "an-administrator", At));

        var held = Answered(Controller(keys, records, mappings), LocalUser);

        Assert.Equal(2, held.Count);
        Assert.Equal(
            new[] { PendingPairing, ActivePairing },
            held.Select(entry => entry.PairingId).OrderBy(id => id, StringComparer.Ordinal).ToArray());
        Assert.All(held, entry => Assert.Equal(LocalUser, entry.LocalUserId));
        Assert.Contains(held, entry => entry.PairingId == ActivePairing && entry.PeerUserId == PeerUser && entry.CachedPeerDisplayName == DisplayName);
        Assert.Contains(held, entry => entry.PairingId == PendingPairing && entry.PeerUserId == OtherPeerUser);
        Assert.DoesNotContain(held, entry => entry.LocalUserId == OtherLocalUser);
    }

    /// <summary>
    /// The near miss the file is named for. The user is mapped under one pairing only, and that
    /// pairing holds no key because it has not finished enrolling. A report walked over the key
    /// store answers an empty list here; the report walks the record store and answers the
    /// mapping.
    /// </summary>
    [Fact]
    public void APairingThatHoldsNoKeyIsStillInTheReport()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        Assert.DoesNotContain(PendingPairing, keys.Pairings());

        mappings.Put(new UserMapping(PendingPairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));

        var entry = Assert.Single(Answered(Controller(keys, records, mappings), LocalUser));

        Assert.Equal(PendingPairing, entry.PairingId);
        Assert.Equal(PeerUser, entry.PeerUserId);
    }

    /// <summary>
    /// The output says the display name is a cache, by the name of the member that carries it,
    /// and carries the name beside the opaque identifier rather than dropping it. A report that
    /// lists the identifier and omits the readable name has left out the only field in the table
    /// that names a person, which is the sentence <c>docs/data.md</c> writes this against.
    /// </summary>
    [Fact]
    public void TheCachedDisplayNameIsSaidToBeACacheAndIsNotDropped()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));

        var answered = Assert.IsType<ContentResult>(Controller(keys, records, mappings).HeldAbout(LocalUser));
        var content = answered.Content ?? string.Empty;

        Assert.Contains("\"cachedPeerDisplayName\":\"" + DisplayName + "\"", content, StringComparison.Ordinal);
        Assert.Contains("\"peerUserId\":\"" + PeerUser + "\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"peerDisplayName\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// A peer that sent no display name is reported with an empty cache rather than with the
    /// entry dropped. The mapping is still held about the user whatever the peer called them.
    /// </summary>
    [Fact]
    public void AnEmptyCacheIsReportedAsEmptyRatherThanDropped()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, string.Empty, "an-administrator", At));

        var entry = Assert.Single(Answered(Controller(keys, records, mappings), LocalUser));

        Assert.Equal(string.Empty, entry.CachedPeerDisplayName);
    }

    /// <summary>
    /// A user mapped nowhere is answered with an empty list and a 200 rather than with a
    /// problem. An operator asked about somebody this plugin holds nothing on is not meeting a
    /// fault, and an empty answer is the answer.
    /// </summary>
    [Fact]
    public void AUserMappedNowhereIsAnsweredWithAnEmptyListRatherThanAProblem()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        var answered = Assert.IsType<ContentResult>(Controller(keys, records, mappings).HeldAbout(LocalUser));

        Assert.Equal(200, answered.StatusCode);
        Assert.Equal("[]", answered.Content);
    }

    /// <summary>
    /// The report is a read. Every mapping that was in the table before it is in the table after
    /// it, so what an operator is handed is a copy of what is held rather than the act of
    /// removing it, which is the other half of issue #60 and is not built.
    /// </summary>
    [Fact]
    public void TheReportChangesNothingInTheTable()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));
        mappings.Put(new UserMapping(PendingPairing, LocalUser, OtherPeerUser, string.Empty, "an-administrator", At));

        Answered(Controller(keys, records, mappings), LocalUser);

        Assert.Single(mappings.For(ActivePairing));
        Assert.Single(mappings.For(PendingPairing));
        Assert.Equal(0, mappings.Sweeps);
    }

    /// <summary>
    /// One audit entry per report, at the level the table gives the row, naming the administrator
    /// who asked and neither user on either side of any mapping it found.
    /// </summary>
    /// <remarks>
    /// The peer identity is refused by <c>docs/logging.md</c> at every level, and the local one
    /// is not a field the row names. The trail says the question was asked and by whom; the table
    /// is where somebody entitled to the answer reads who was mapped to whom.
    /// </remarks>
    [Fact]
    public void AReportWritesOneEntryNamingWhoAskedAndNeitherUser()
    {
        var mappings = new InMemoryUserMappings();
        var log = new CapturingLogger();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));

        var found = new HeldAboutUser(mappings, log).Report(new[] { ActivePairing, PendingPairing }, LocalUser, Administrator);

        Assert.Single(found);

        var entry = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(Administrator, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(LocalUser, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(PeerUser, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(DisplayName, entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The entry stays one line where the administrator carries a line break, so a report cannot
    /// be made to write further entries under this plugin's name.
    /// </summary>
    /// <remarks>
    /// The administrator is a claim the host set, which is a value from outside this plugin
    /// whatever the host does with it today, and <c>OneLine</c> at the call site is what holds
    /// this. Deleting that call turns this case red. What the value said is still in the entry:
    /// removing the words is a larger decision about what an audit entry may say, and what is
    /// asserted here is that they are one line rather than lines of their own. The break is
    /// built from its codepoint because a literal one does not survive this repository's line
    /// endings across a checkout.
    /// </remarks>
    [Fact]
    public void AnAdministratorCarryingALineBreakStillWritesOneEntryOnOneLine()
    {
        const char CarriageReturn = (char)0x000D;
        const char LineFeed = (char)0x000A;

        var forged = Administrator + CarriageReturn + LineFeed
            + "[Warning] A pairing was revoked by an administrator.";

        var log = new CapturingLogger();

        new HeldAboutUser(new InMemoryUserMappings(), log).Report(new[] { ActivePairing }, LocalUser, forged);

        var entry = Assert.Single(log.Written);

        Assert.DoesNotContain(LineFeed, entry.Text);
        Assert.DoesNotContain(CarriageReturn, entry.Text);
        Assert.Contains("[Warning] A pairing was revoked", entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A report that found nothing is still a report somebody made about a person, and it is
    /// audited. An entry only for the reports that found something would leave the trail unable
    /// to say the question was asked.
    /// </summary>
    [Fact]
    public void AReportThatFindsNothingIsStillAudited()
    {
        var log = new CapturingLogger();

        var found = new HeldAboutUser(new InMemoryUserMappings(), log).Report(new[] { ActivePairing }, LocalUser, Administrator);

        Assert.Empty(found);
        Assert.Single(log.Written);
    }

    /// <summary>
    /// The administrator an entry names is the user the host authenticated, read from the claim
    /// the host sets on every principal, formatted as the host formats it.
    /// </summary>
    [Fact]
    public void TheAdministratorIsTheUserTheHostAuthenticated()
    {
        Assert.Equal(Administrator, RequestingAdministrator.Of(Principal(new Claim(RequestingAdministrator.UserIdClaim, Administrator))));
    }

    /// <summary>
    /// A request made with an API key is named as one. The host gives such a request the empty
    /// identifier, and a trail recording thirty-two zeros as the person who asked is a sentence
    /// nobody can act on.
    /// </summary>
    [Fact]
    public void AnApiKeyIsNamedAsOneRatherThanAsTheEmptyUser()
    {
        var principal = Principal(
            new Claim(RequestingAdministrator.UserIdClaim, Guid.Empty.ToString("N")),
            new Claim(RequestingAdministrator.IsApiKeyClaim, bool.TrueString));

        Assert.Equal(RequestingAdministrator.ApiKey, RequestingAdministrator.Of(principal));
    }

    /// <summary>
    /// A principal carrying no identifier is refused as that problem, and no audit entry is
    /// written, because an entry under nobody is the failure the refusal exists against. The
    /// same holds for an action with no request behind it at all.
    /// </summary>
    [Fact]
    public void ARequestNamingNoAdministratorIsRefusedAndWritesNoEntry()
    {
        var (keys, records, mappings) = TwoPairingsOneWithAKey();
        var log = new CapturingLogger();

        mappings.Put(new UserMapping(ActivePairing, LocalUser, PeerUser, DisplayName, "an-administrator", At));

        var unnamed = Controller(keys, records, new HeldAboutUser(mappings, log), Principal());
        var answered = Assert.IsType<ContentResult>(unnamed.HeldAbout(LocalUser));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.AdministratorUnidentified), answered.Content);
        Assert.Empty(log.Written);

        Assert.Null(RequestingAdministrator.Of(null));
        Assert.Null(RequestingAdministrator.Of(Principal(new Claim(RequestingAdministrator.UserIdClaim, " "))));
    }

    /// <summary>
    /// A record store that will not read is named as that store and not as a fault, and nothing
    /// of the fault reaches the answer.
    /// </summary>
    [Fact]
    public void ARecordStoreThatWillNotReadIsNamedAsThatStore()
    {
        var answered = Assert.IsType<ContentResult>(
            Controller(new InMemoryPairingKeyStore(), new UnreadableRecords(), new InMemoryUserMappings()).HeldAbout(LocalUser));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.RecordStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableRecords.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mapping store that will not read is named as that store rather than as the record store,
    /// so an operator is sent to the right file, and nothing of the fault reaches the answer.
    /// </summary>
    [Fact]
    public void AMappingStoreThatWillNotReadIsNamedAsThatStore()
    {
        var (keys, records, _) = TwoPairingsOneWithAKey();

        var answered = Assert.IsType<ContentResult>(Controller(keys, records, new UnreadableMappings()).HeldAbout(LocalUser));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.MappingStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableMappings.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
        Assert.NotEqual(
            AdministrativeAnswer.Wire(AdministrativeProblem.MappingStoreUnreadable),
            AdministrativeAnswer.Wire(AdministrativeProblem.RecordStoreUnreadable));
    }

    /// <summary>
    /// Two pairings on record, one of them active and holding a key, the other pending and
    /// holding none, and an empty mapping table beside them.
    /// </summary>
    /// <returns>The key store, the record store and the mapping store.</returns>
    private static (InMemoryPairingKeyStore Keys, InMemoryPairingRecords Records, InMemoryUserMappings Mappings) TwoPairingsOneWithAKey()
    {
        var keys = new InMemoryPairingKeyStore();
        var records = new InMemoryPairingRecords();

        keys.Add(ActivePairing, KeyMaterial.Fresh());
        records.Write(new PairingRecord(ActivePairing, PairingState.Active, PairingState.ConfirmedByPeer, "Confirm", "an-administrator", At));
        records.Write(new PairingRecord(PendingPairing, PairingState.Pending, PairingState.Offered, "Hello", "peer", At));

        return (keys, records, new InMemoryUserMappings());
    }

    /// <summary>
    /// What the action answers with, deserialised, so a case reads what an administrator
    /// receives rather than what the store was handed.
    /// </summary>
    /// <param name="controller">The controller.</param>
    /// <param name="localUserId">The user to ask about.</param>
    /// <returns>The entries the action reported.</returns>
    private static List<HeldMapping> Answered(AdministrativePlaneController controller, string localUserId)
    {
        var answered = Assert.IsType<ContentResult>(controller.HeldAbout(localUserId));

        Assert.Equal(200, answered.StatusCode);

        return JsonSerializer.Deserialize<List<HeldMapping>>(answered.Content ?? "[]") ?? new List<HeldMapping>();
    }

    /// <summary>
    /// A controller over the given stores, asked by an administrator the host authenticated,
    /// with the logger going nowhere.
    /// </summary>
    private static AdministrativePlaneController Controller(IPairingKeyStore keys, IPairingRecordStore records, IUserMappingStore mappings)
        => Controller(
            keys,
            records,
            new HeldAboutUser(mappings, NullLogger<HeldAboutUser>.Instance),
            Principal(new Claim(RequestingAdministrator.UserIdClaim, Administrator)));

    /// <summary>
    /// A controller over the given stores and report, asked by the given principal.
    /// </summary>
    private static AdministrativePlaneController Controller(
        IPairingKeyStore keys,
        IPairingRecordStore records,
        HeldAboutUser held,
        ClaimsPrincipal principal)
    {
        var context = new DefaultHttpContext { User = principal };

        return new AdministrativePlaneController(
            keys,
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            held,
            new UserMappings(new InMemoryUserMappings(), new PairingStateMachine(new InMemoryPairingRecords(), new InMemoryUserMappings()), NullLogger<UserMappings>.Instance),
            new InMemoryLocalUsers(),
            NullLogger<AdministrativePlaneController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    /// <summary>
    /// A principal carrying exactly the claims given, authenticated the way the host's handler
    /// authenticates one.
    /// </summary>
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

    /// <summary>
    /// A record store whose walk throws, which is what a file that does not parse does.
    /// </summary>
    private sealed class UnreadableRecords : IPairingRecordStore
    {
        public const string Detail = "the-record-store-file-did-not-parse";

        public IReadOnlyList<string> Pairings() => throw new IOException(Detail);

        public PairingRecord? Read(string pairingId) => throw new IOException(Detail);

        public void Write(PairingRecord record) => throw new IOException(Detail);

        public void Delete(string pairingId) => throw new IOException(Detail);
    }

    /// <summary>
    /// A mapping store whose read throws, which is what a file that does not parse does.
    /// </summary>
    private sealed class UnreadableMappings : IUserMappingStore
    {
        public const string Detail = "the-mapping-store-file-did-not-parse";

        public IReadOnlyList<UserMapping> For(string pairingId) => throw new IOException(Detail);

        public void Put(UserMapping mapping) => throw new IOException(Detail);

        public void Remove(string pairingId, string localUserId) => throw new IOException(Detail);

        public void RemoveEvery(string pairingId) => throw new IOException(Detail);
    }

    private sealed class CapturingLogger : ILogger<HeldAboutUser>
    {
        public List<(LogLevel Level, string Text)> Written { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Written.Add((logLevel, formatter(state, exception)));
        }
    }
}
