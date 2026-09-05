using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Harness;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Jellyfin.Plugin.ServerPairing.Tests.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The action that opens an enrolment window on a running server, which is issue #357.
/// </summary>
/// <remarks>
/// Every part of an opening existed and nothing on a server called it, so the read beside this
/// action answered an empty list on every server and no pairing had ever been in
/// <see cref="PairingState.Offered"/> outside a fixture. The cases here drive the action and then
/// read the store and the listing, because what the rest of the plugin and the page see is those
/// two rather than what the action returned.
/// <para>
/// WHAT IS NOT DRIVEN IS A REQUEST. No host pipeline is stood up, so whether the elevation
/// policy refuses a caller lacking the host's token is measured by nothing here, for the reason
/// <c>AdministrativePlaneControllerTests</c> gives. The principal is handed to the controller the
/// way the host hands one over after authenticating it.
/// </para>
/// </remarks>
public class WindowOpeningTests
{
    private const string Administrator = "an-administrator";

    private const string Peer = "https://peer.example";

    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The first two conditions of issue #357 together: opening a window writes the record the
    /// join writes, and the read of open windows then answers with it. The two are asserted
    /// against each other rather than each against a shape somebody typed twice.
    /// </summary>
    [Fact]
    public void OpeningAWindowWritesTheRecordTheJoinWritesAndTheReadAnswersWithIt()
    {
        var records = new InMemoryPairingRecords();
        var controller = Controller(records);

        var answered = Assert.IsType<ContentResult>(controller.OpenEnrolmentWindow());

        Assert.Equal(201, answered.StatusCode);

        var opened = Opened(answered);

        Assert.True(ProvisionalPairingId.Is(opened.PairingId));
        Assert.Equal(Noon, opened.OpenedAt);

        var record = records.Read(opened.PairingId);

        Assert.NotNull(record);
        Assert.Equal(PairingState.Offered, record.State);
        Assert.Equal(Peer, record.PeerAddress);
        Assert.Equal(Administrator, record.Actor);
        Assert.Equal(Noon, record.At);

        Assert.Equal(opened, Assert.Single(Listed(controller)));
    }

    /// <summary>
    /// The third condition, first half: a window against a peer this server is already paired
    /// with is refused under the word for it, and nothing is written. The refusal is the
    /// window's own, and this asserts that the action carries it rather than swallowing it.
    /// </summary>
    [Fact]
    public void AWindowAgainstAPeerThisServerIsAlreadyPairedWithIsRefusedAndWritesNothing()
    {
        var records = new InMemoryPairingRecords();
        var log = new CapturingLogger();

        records.Write(new PairingRecord(
            "9f8c1d2b3a4e5f60718293a4b5c6d7e8",
            PairingState.Active,
            PairingState.ConfirmedByPeer,
            "FingerprintConfirmed",
            Administrator,
            Noon.AddDays(-1),
            Peer));

        var answered = Assert.IsType<ContentResult>(Controller(records, log: log).OpenEnrolmentWindow());

        Assert.Equal(OpeningAnswer.RefusedStatus, answered.StatusCode);
        Assert.Equal(OpeningAnswer.Body(OpeningRefusal.AlreadyPaired), answered.Content);
        Assert.Single(records.Pairings());
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// The third condition, second half: a lifetime the configuration refused refuses the
    /// opening. The window would otherwise open on the lifetime a server nobody configured runs
    /// on, which is a window of a length the operator did not choose, and
    /// <c>docs/configuration.md</c> says the action reads <c>MayPair</c> for exactly this.
    /// </summary>
    [Fact]
    public void ALifetimeTheConfigurationRefusedRefusesTheOpeningAndWritesNothing()
    {
        var records = new InMemoryPairingRecords();
        var configuration = ConfigurationReading.Of(new PluginConfiguration
        {
            PeerAddress = Peer,
            EnrolmentWindowSeconds = EnrolmentWindow.MaximumLifetimeSeconds + 1,
        });

        Assert.False(configuration.MayPair);

        var answered = Assert.IsType<ContentResult>(Controller(records, configuration).OpenEnrolmentWindow());

        Assert.Equal(OpeningAnswer.RefusedStatus, answered.StatusCode);
        Assert.Equal(OpeningAnswer.Body(OpeningRefusal.ConfigurationRefused), answered.Content);
        Assert.Empty(records.Pairings());
    }

    /// <summary>
    /// A second opening while the first is open is refused under its own word and writes no
    /// second record, which is the window's one-at-a-time property reaching the wire.
    /// </summary>
    [Fact]
    public void ASecondOpeningWhileTheFirstIsOpenIsRefusedAndWritesNoSecondRecord()
    {
        var records = new InMemoryPairingRecords();
        var controller = Controller(records);

        Assert.Equal(201, Assert.IsType<ContentResult>(controller.OpenEnrolmentWindow()).StatusCode);

        var second = Assert.IsType<ContentResult>(controller.OpenEnrolmentWindow());

        Assert.Equal(OpeningAnswer.RefusedStatus, second.StatusCode);
        Assert.Equal(OpeningAnswer.Body(OpeningRefusal.AlreadyOpen), second.Content);
        Assert.Single(records.Pairings());
    }

    /// <summary>
    /// A server nobody has entered a peer address on has nothing to open a window against, and
    /// says so rather than opening one against nothing or answering a fault.
    /// </summary>
    [Fact]
    public void AServerWithNoPeerAddressEnteredIsRefused()
    {
        var records = new InMemoryPairingRecords();

        var answered = Assert.IsType<ContentResult>(
            Controller(records, ConfigurationReading.Of(new PluginConfiguration())).OpenEnrolmentWindow());

        Assert.Equal(OpeningAnswer.RefusedStatus, answered.StatusCode);
        Assert.Equal(OpeningAnswer.Body(OpeningRefusal.NoPeerAddress), answered.Content);
        Assert.Empty(records.Pairings());
    }

    /// <summary>
    /// A principal naming nobody is refused before anything is read, as the named problem the
    /// other state-changing action on this plane answers, and no window opens: the record names
    /// its actor, and a change nobody is named for is the trail's failure and not only the
    /// entry's.
    /// </summary>
    [Fact]
    public void AnOpeningNamingNoAdministratorIsRefusedAndOpensNothing()
    {
        var records = new InMemoryPairingRecords();
        var log = new CapturingLogger();

        var answered = Assert.IsType<ContentResult>(Controller(records, principal: Principal(), log: log).OpenEnrolmentWindow());

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.AdministratorUnidentified), answered.Content);
        Assert.Empty(records.Pairings());
        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A record store that will not read or write is answered as the problem naming that store
    /// and not as a fault, and nothing of the fault reaches the answer, for the reason every
    /// other action on this plane carries a catch.
    /// </summary>
    [Fact]
    public void AStoreThatWillNotReadOrWriteIsAnsweredAsThatProblemAndNotAsAFault()
    {
        var answered = Assert.IsType<ContentResult>(Controller(new UnreadableRecords()).OpenEnrolmentWindow());

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(AdministrativeAnswer.Body(AdministrativeProblem.RecordStoreUnreadable), answered.Content);
        Assert.DoesNotContain(UnreadableRecords.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// The opening is the row <c>docs/logging.md</c> gives an enrolment that was started, with
    /// the fields that row names: the identifier, the peer address and the administrator. It is
    /// written once a record is and not before, which the refusal cases above assert from the
    /// other side.
    /// </summary>
    [Fact]
    public void TheOpeningIsWrittenToTheLogWithTheFieldsItsRowNames()
    {
        var records = new InMemoryPairingRecords();
        var log = new CapturingLogger();

        var opened = Opened(Assert.IsType<ContentResult>(Controller(records, log: log).OpenEnrolmentWindow()));

        var (level, text) = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Information, level);
        Assert.StartsWith("An enrolment was started by an administrator.", text, StringComparison.Ordinal);
        Assert.Contains(opened.PairingId, text, StringComparison.Ordinal);
        Assert.Contains(Peer, text, StringComparison.Ordinal);
        Assert.Contains(Administrator, text, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every spelling of a refusal is a word rather than a sentence, no two refusals share one,
    /// none of them is a word a named problem already uses, and the two answers carry two
    /// statuses, so an operator or a page meeting one can tell it from the other.
    /// </summary>
    [Fact]
    public void EveryRefusalHasItsOwnWireSpellingAndNoneIsAProblem()
    {
        var spellings = Enum.GetValues<OpeningRefusal>().Select(OpeningAnswer.Wire).ToArray();
        var problems = Enum.GetValues<AdministrativeProblem>().Select(AdministrativeAnswer.Wire).ToArray();

        Assert.NotEmpty(spellings);
        Assert.Equal(spellings.Length, spellings.Distinct(StringComparer.Ordinal).Count());
        Assert.All(spellings, spelling => Assert.DoesNotContain(' ', spelling));
        Assert.Empty(spellings.Intersect(problems, StringComparer.Ordinal));
        Assert.NotEqual(AdministrativeAnswer.ProblemStatus, OpeningAnswer.RefusedStatus);
    }

    /// <summary>
    /// A refusal this vocabulary has no word for is refused rather than written into an answer
    /// as a number, and an opening the window did not refuse has no refusal at all. The failure
    /// the second half catches is a default arm answering a window that opened as one that did
    /// not.
    /// </summary>
    [Fact]
    public void ARefusalOutsideTheVocabularyIsRefusedAndAnOpeningIsNotARefusal()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => OpeningAnswer.Wire((OpeningRefusal)7));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpeningAnswer.RefusalFor(WindowOpening.Opened));
    }

    /// <summary>
    /// The window the action answered, deserialised, so what is asserted is what an
    /// administrator receives rather than what the join returned.
    /// </summary>
    /// <param name="answered">The answer.</param>
    /// <returns>The window.</returns>
    private static OpenWindow Opened(ContentResult answered)
    {
        Assert.Equal("application/json", answered.ContentType);

        return JsonSerializer.Deserialize<OpenWindow>(answered.Content ?? string.Empty)
            ?? throw new InvalidOperationException("The action answered nothing a window could be read from.");
    }

    /// <summary>
    /// The windows the read beside the action lists.
    /// </summary>
    /// <param name="controller">The plane.</param>
    /// <returns>The windows it reported.</returns>
    private static List<OpenWindow> Listed(AdministrativePlaneController controller)
    {
        var answered = Assert.IsType<ContentResult>(controller.Windows());

        Assert.Equal(200, answered.StatusCode);

        return JsonSerializer.Deserialize<List<OpenWindow>>(answered.Content ?? "[]") ?? new List<OpenWindow>();
    }

    /// <summary>
    /// A controller whose enrolment is joined over the given record store, under the given
    /// configuration or one holding the peer, asked by the given principal or by an
    /// administrator the host authenticated, with the clock stopped at noon and the log going
    /// where the case says or nowhere.
    /// </summary>
    /// <param name="records">The record store.</param>
    /// <param name="configuration">The configuration, or one holding the peer.</param>
    /// <param name="principal">The principal, or an administrator.</param>
    /// <param name="log">Where the plane's log goes, or nowhere.</param>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller(
        IPairingRecordStore records,
        ConfigurationReading? configuration = null,
        ClaimsPrincipal? principal = null,
        ILogger<AdministrativePlaneController>? log = null)
    {
        var mappings = new InMemoryUserMappings();
        var machine = new PairingStateMachine(records, mappings);
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
            new UserMappings(mappings, machine, NullLogger<UserMappings>.Instance),
            new InMemoryLocalUsers(),
            new Enrolment(new EnrolmentWindow(new RecordedPeers(records)), machine, records),
            configuration ?? ConfigurationReading.Of(new PluginConfiguration { PeerAddress = Peer }),
            new InstanceClock(Noon),
            log ?? NullLogger<AdministrativePlaneController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    /// <summary>
    /// A principal carrying exactly the claims given, authenticated the way the host's handler
    /// authenticates one.
    /// </summary>
    /// <param name="claims">The claims.</param>
    /// <returns>The principal.</returns>
    private static ClaimsPrincipal Principal(params Claim[] claims)
        => new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));

    /// <summary>
    /// A record store whose every operation throws, which is what a file that does not parse
    /// does on a read and a disk that is full does on a write.
    /// </summary>
    private sealed class UnreadableRecords : IPairingRecordStore
    {
        /// <summary>
        /// Text that occurs nowhere else, so a case can assert it did not reach the caller.
        /// </summary>
        public const string Detail = "the-record-store-file-did-not-parse";

        /// <inheritdoc />
        public IReadOnlyList<string> Pairings() => throw new IOException(Detail);

        /// <inheritdoc />
        public PairingRecord? Read(string pairingId) => throw new IOException(Detail);

        /// <inheritdoc />
        public void Write(PairingRecord record) => throw new IOException(Detail);

        /// <inheritdoc />
        public void Delete(string pairingId) => throw new IOException(Detail);
    }

    /// <summary>
    /// A logger that keeps what the plane wrote, as the level and the rendered text.
    /// </summary>
    private sealed class CapturingLogger : ILogger<AdministrativePlaneController>
    {
        /// <summary>
        /// Gets what was written, in order.
        /// </summary>
        public List<(LogLevel Level, string Text)> Written { get; } = new();

        /// <inheritdoc />
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
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
