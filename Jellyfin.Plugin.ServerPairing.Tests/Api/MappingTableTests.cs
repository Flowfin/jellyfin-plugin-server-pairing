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
/// The listing and removal half of issue #40: a pairing's mapping table on the administrative
/// plane, the local users who are unmapped under it, and a removal that goes through the one
/// type every change passes.
/// </summary>
/// <remarks>
/// WHAT IS NOT ASSERTED HERE. Nothing runs on a server: the host's routing, its elevation policy,
/// its authentication handler and its user manager are all absent, so the principal every case
/// hands in is one the case built and the local users are ones the case declared. Whether the
/// host's user manager answers what <c>HostLocalUsers</c> reads from it is
/// <c>HostLocalUsersTests</c>, against a substitute, and not a measurement on a host.
/// </remarks>
public class MappingTableTests
{
    private const string Pairing = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
    private const string OtherPairing = "0011223344556677889900aabbccddee";
    private const string Anna = "3a1c9e7f2b4d4c6f8a7b9c0d1e2f3a01";
    private const string Bea = "3a1c9e7f2b4d4c6f8a7b9c0d1e2f3a02";
    private const string Carl = "3a1c9e7f2b4d4c6f8a7b9c0d1e2f3a03";
    private const string Gone = "3a1c9e7f2b4d4c6f8a7b9c0d1e2f3a99";
    private const string PeerUser = "peer-user-7";
    private const string OtherPeerUser = "peer-user-8";
    private const string DisplayName = "Anna Example";
    private const string Administrator = "3f2c9a1e4b5d4c6f8a7b9c0d1e2f3a4b";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first thing the surface does. Every mapping under the pairing is listed with the
    /// local user, the peer identifier and the cached name, and a mapping under another pairing
    /// is not, because the table is per pairing.
    /// </summary>
    [Fact]
    public void TheListingCoversEveryMappingForThePairingAndNoOther()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));
        mappings.Put(new UserMapping(Pairing, Carl, "peer-user-9", "Carl Example", "an-administrator", At));
        mappings.Put(new UserMapping(OtherPairing, Bea, OtherPeerUser, "Bea Example", "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        Assert.Equal(Pairing, table.PairingId);
        Assert.Equal(2, table.Mappings.Count);
        Assert.Contains(table.Mappings, entry => entry.LocalUserId == Anna && entry.LocalUserName == "anna" && entry.PeerUserId == PeerUser && entry.CachedPeerDisplayName == DisplayName);
        Assert.Contains(table.Mappings, entry => entry.LocalUserId == Carl && entry.LocalUserName == "carl" && entry.PeerUserId == "peer-user-9");
        Assert.DoesNotContain(table.Mappings, entry => entry.LocalUserId == Bea);
    }

    /// <summary>
    /// The fourth done condition of issue #40. Where the display cache holds nothing the listing
    /// shows the peer identifier, and where it holds a name the listing shows the name; no cell
    /// is empty either way, and the cache itself is still carried beside it so a reader can tell
    /// a name from an identifier standing in for one.
    /// </summary>
    [Fact]
    public void TheListingShowsTheIdentifierWhereTheDisplayCacheHoldsNothing()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));
        mappings.Put(new UserMapping(Pairing, Bea, OtherPeerUser, string.Empty, "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        var named = Assert.Single(table.Mappings, entry => entry.LocalUserId == Anna);
        var unnamed = Assert.Single(table.Mappings, entry => entry.LocalUserId == Bea);

        Assert.Equal(DisplayName, named.PeerUserShownAs);
        Assert.Equal(DisplayName, named.CachedPeerDisplayName);
        Assert.Equal(OtherPeerUser, unnamed.PeerUserShownAs);
        Assert.Equal(string.Empty, unnamed.CachedPeerDisplayName);
        Assert.All(table.Mappings, entry => Assert.False(string.IsNullOrWhiteSpace(entry.PeerUserShownAs)));
        Assert.All(table.Mappings, entry => Assert.False(string.IsNullOrWhiteSpace(entry.LocalUserShownAs)));
    }

    /// <summary>
    /// The fifth done condition of issue #40. The local users with no mapping under the pairing
    /// are reported by the listing, by name and by identifier, and a mapped user is not among
    /// them.
    /// </summary>
    [Fact]
    public void UnmappedLocalUsersAreReportedByTheListing()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        Assert.Equal("bea,carl", string.Join(",", table.UnmappedLocalUsers.Select(user => user.LocalUserName)));
        Assert.Equal(Bea + "," + Carl, string.Join(",", table.UnmappedLocalUsers.Select(user => user.LocalUserId)));
        Assert.DoesNotContain(table.UnmappedLocalUsers, user => user.LocalUserId == Anna);
    }

    /// <summary>
    /// A mapping is per pairing, so a user mapped under another pairing is unmapped under this
    /// one and is reported as such. The failure this refuses is an unmapped list computed over
    /// the whole table rather than over the pairing being listed.
    /// </summary>
    [Fact]
    public void AUserMappedOnlyUnderAnotherPairingIsUnmappedHere()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(OtherPairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        Assert.Empty(table.Mappings);
        Assert.Contains(table.UnmappedLocalUsers, user => user.LocalUserId == Anna);
    }

    /// <summary>
    /// A mapping to a local user the host no longer has is listed by its identifier with an
    /// empty name rather than dropped, because a mapping the operator cannot see is one they
    /// cannot remove. It is not among the unmapped users either, because it is not a user this
    /// server has.
    /// </summary>
    [Fact]
    public void ALocalUserTheHostNoLongerHoldsIsListedByItsIdentifier()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(Pairing, Gone, PeerUser, DisplayName, "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        var entry = Assert.Single(table.Mappings);

        Assert.Equal(Gone, entry.LocalUserId);
        Assert.Equal(string.Empty, entry.LocalUserName);
        Assert.Equal(Gone, entry.LocalUserShownAs);
        Assert.DoesNotContain(table.UnmappedLocalUsers, user => user.LocalUserId == Gone);
    }

    /// <summary>
    /// A pairing nothing is held for is not found rather than answered as an empty table with
    /// every user unmapped, because such a table invites mapping under a pairing that does not
    /// exist.
    /// </summary>
    [Fact]
    public void APairingNothingIsHeldForIsNotFound()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        Assert.IsType<NotFoundResult>(Controller(records, mappings, users).Mappings("ffffffffffffffffffffffffffffffff"));
    }

    /// <summary>
    /// A revoked pairing keeps its record and is listed with an empty table, because the state
    /// machine swept its mappings when it ended. That is the answer rather than a fault.
    /// </summary>
    [Fact]
    public void ARevokedPairingIsListedWithAnEmptyTable()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        records.Write(new PairingRecord(Pairing, PairingState.Revoked, PairingState.Active, "AdministratorRevoked", "an-administrator", At));

        var table = Listed(Controller(records, mappings, users), Pairing);

        Assert.Empty(table.Mappings);
        Assert.Equal(3, table.UnmappedLocalUsers.Count);
    }

    /// <summary>
    /// The listing is a read. The table is the same before and after, nothing was swept, and no
    /// audit entry was written, because the dashboard reading the table is where an entitled
    /// operator reads who is mapped to whom rather than an act the trail records.
    /// </summary>
    [Fact]
    public void TheListingChangesNothingAndWritesNoEntry()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();
        var log = new CapturingLogger<UserMappings>();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        Listed(Controller(records, mappings, users, log), Pairing);

        Assert.Single(mappings.For(Pairing));
        Assert.Equal(0, mappings.Sweeps);
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A record store that will not read is named as that store and not as a fault, before the
    /// mapping store is touched, and nothing of the fault reaches the answer.
    /// </summary>
    [Fact]
    public void ARecordStoreThatWillNotReadIsNamedAsThatStoreOnTheListing()
    {
        var answered = Assert.IsType<ContentResult>(
            Controller(new UnreadableRecords(), new InMemoryUserMappings(), new InMemoryLocalUsers()).Mappings(Pairing));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.RecordStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableRecords.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// A mapping store that will not read is named as that store rather than as the record
    /// store, so an operator is sent to the right file.
    /// </summary>
    [Fact]
    public void AMappingStoreThatWillNotReadIsNamedAsThatStoreOnTheListing()
    {
        var (records, _, users) = TwoPairingsAndThreeUsers();

        var answered = Assert.IsType<ContentResult>(Controller(records, new UnreadableMappings(), users).Mappings(Pairing));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.MappingStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableMappings.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The removal half. The mapping and its cache are gone, the answer says so with no body,
    /// and the trail names the administrator the host authenticated as the one who removed it,
    /// the direction it moved, and neither user on either side.
    /// </summary>
    [Fact]
    public void RemovingAMappingRemovesItAndTheTrailNamesWhoRemovedIt()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();
        var log = new CapturingLogger<UserMappings>();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        Assert.IsType<NoContentResult>(Controller(records, mappings, users, log).Unmap(Pairing, Anna));

        Assert.Empty(mappings.For(Pairing));

        var entry = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Contains(Administrator, entry.Text, StringComparison.Ordinal);
        Assert.Contains(MappingDirection.Unmapped.ToString(), entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Anna, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(PeerUser, entry.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(DisplayName, entry.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Removing a mapping that is not there is not found and writes nothing. An entry per call
    /// rather than per change would let anything reaching this path grow the log without a
    /// mapping ever moving.
    /// </summary>
    [Fact]
    public void RemovingAMappingThatIsNotThereIsNotFoundAndWritesNothing()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();
        var log = new CapturingLogger<UserMappings>();

        Assert.IsType<NotFoundResult>(Controller(records, mappings, users, log).Unmap(Pairing, Anna));

        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A removal under a principal naming nobody is refused before the store is touched, and the
    /// mapping stays. A change nobody is named for is the trail's failure and not only the
    /// entry's.
    /// </summary>
    [Fact]
    public void ARemovalNamingNoAdministratorIsRefusedAndRemovesNothing()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();
        var log = new CapturingLogger<UserMappings>();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        var answered = Assert.IsType<ContentResult>(Controller(records, mappings, users, log, Principal()).Unmap(Pairing, Anna));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.AdministratorUnidentified), answered.Content);
        Assert.Single(mappings.For(Pairing));
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A mapping store that will not read or write is named as that store on a removal, and
    /// nothing of the fault reaches the answer.
    /// </summary>
    [Fact]
    public void AMappingStoreThatWillNotWriteIsNamedAsThatStoreOnARemoval()
    {
        var (records, _, users) = TwoPairingsAndThreeUsers();

        var answered = Assert.IsType<ContentResult>(Controller(records, new UnreadableMappings(), users).Unmap(Pairing, Anna));

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.MappingStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableMappings.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The listing is what an administrator receives: the members are spelt as the page reads
    /// them, and the cache is said to be a cache by the name of the member that carries it.
    /// </summary>
    [Fact]
    public void TheAnswerSpellsItsMembersAsThePageReadsThem()
    {
        var (records, mappings, users) = TwoPairingsAndThreeUsers();

        mappings.Put(new UserMapping(Pairing, Anna, PeerUser, DisplayName, "an-administrator", At));

        var answered = Assert.IsType<ContentResult>(Controller(records, mappings, users).Mappings(Pairing));
        var content = answered.Content ?? string.Empty;

        Assert.Contains("\"mappings\":[", content, StringComparison.Ordinal);
        Assert.Contains("\"unmappedLocalUsers\":[", content, StringComparison.Ordinal);
        Assert.Contains("\"cachedPeerDisplayName\":\"" + DisplayName + "\"", content, StringComparison.Ordinal);
        Assert.Contains("\"peerUserShownAs\":\"" + DisplayName + "\"", content, StringComparison.Ordinal);
        Assert.Contains("\"localUserShownAs\":\"anna\"", content, StringComparison.Ordinal);
        Assert.DoesNotContain("\"peerDisplayName\"", content, StringComparison.Ordinal);
    }

    /// <summary>
    /// Two pairings on record, one active and one pending, an empty mapping table, and three
    /// users on this server.
    /// </summary>
    private static (InMemoryPairingRecords Records, InMemoryUserMappings Mappings, InMemoryLocalUsers Users) TwoPairingsAndThreeUsers()
    {
        var records = new InMemoryPairingRecords();

        records.Write(new PairingRecord(Pairing, PairingState.Active, PairingState.ConfirmedByPeer, "Confirm", "an-administrator", At));
        records.Write(new PairingRecord(OtherPairing, PairingState.Pending, PairingState.Offered, "Hello", "peer", At));

        var users = new InMemoryLocalUsers().With(Anna, "anna").With(Bea, "bea").With(Carl, "carl");

        return (records, new InMemoryUserMappings(), users);
    }

    /// <summary>
    /// What the listing answers with, deserialised, so a case reads what an administrator
    /// receives rather than what the store was handed.
    /// </summary>
    private static MappingTable Listed(AdministrativePlaneController controller, string pairingId)
    {
        var answered = Assert.IsType<ContentResult>(controller.Mappings(pairingId));

        Assert.Equal(200, answered.StatusCode);

        return JsonSerializer.Deserialize<MappingTable>(answered.Content ?? string.Empty)
            ?? throw new InvalidOperationException("The listing answered nothing a table could be read from.");
    }

    /// <summary>
    /// A controller over the given stores and users, asked by an administrator the host
    /// authenticated, with the mapping log going where the case says or nowhere.
    /// </summary>
    private static AdministrativePlaneController Controller(
        IPairingRecordStore records,
        IUserMappingStore mappings,
        ILocalUsers users,
        ILogger<UserMappings>? log = null,
        ClaimsPrincipal? principal = null)
    {
        var context = new DefaultHttpContext
        {
            User = principal ?? Principal(new Claim(RequestingAdministrator.UserIdClaim, Administrator)),
        };

        return new AdministrativePlaneController(
            new InMemoryPairingKeyStore(),
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            new HeldAboutUser(mappings, NullLogger<HeldAboutUser>.Instance),
            new UserMappings(mappings, new PairingStateMachine(records, mappings), log ?? NullLogger<UserMappings>.Instance),
            users,
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
    /// A record store whose read throws, which is what a file that does not parse does.
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
    /// A mapping store whose every operation throws, which is what a file that does not parse
    /// or a directory that cannot be written does.
    /// </summary>
    private sealed class UnreadableMappings : IUserMappingStore
    {
        public const string Detail = "the-mapping-store-file-did-not-parse";

        public IReadOnlyList<UserMapping> For(string pairingId) => throw new IOException(Detail);

        public void Put(UserMapping mapping) => throw new IOException(Detail);

        public void Remove(string pairingId, string localUserId) => throw new IOException(Detail);

        public void RemoveEvery(string pairingId) => throw new IOException(Detail);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
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
