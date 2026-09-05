using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Jellyfin.Plugin.ServerPairing.Tests.Protocol;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The seventh property of issue #18: while an enrolment window is open the plugin says so.
/// </summary>
/// <remarks>
/// The other six properties bound a window - it opens only when an administrator opens it, it
/// closes on the first use, on a timer and after a small number of failures - and none of them
/// helps an operator who does not know one is open. This is the read that tells them.
/// <para>
/// It is asked of the record store rather than of <see cref="EnrolmentWindow"/>, because a
/// pairing in <see cref="PairingState.Offered"/> is what an administrator opening a window
/// produces, and <c>docs/protocol.md</c> took that answer over the other one for exactly this
/// reason: a dashboard reads one surface rather than the window type for a pairing somebody has
/// started and the record store for every pairing that finished.
/// </para>
/// <para>
/// THIS REMARK SAID NO CASE HERE ASSERTS THAT A WINDOW OPENED APPEARS IN THIS ANSWER, BECAUSE
/// NOTHING JOINED THE WINDOW TO THE STATE MACHINE. <see cref="Enrolment"/> is that join and one
/// case below opens a window through it rather than writing the record itself, so the reader and
/// the producer are asserted against each other rather than each against a shape somebody typed
/// twice.
/// </para>
/// <para>
/// The other cases go on writing their own records, and that is deliberate. A producer cannot
/// make a record in a state it never produces, so the case that says a pairing which is not
/// offered is not an open window has to write those states by hand or assert nothing about six
/// of the eight.
/// </para>
/// <para>
/// THIS REMARK SAID NOTHING ON A SERVER CALLED THE PRODUCER AND NAMED ISSUE #53 FOR THE ENDPOINT
/// THAT WOULD. <c>WindowOpeningTests</c> is that action, which is issue #357, and is where the
/// read is asserted to answer what the action opened; the cases here stay about the read alone.
/// </para>
/// </remarks>
public class OpenWindowTests
{
    private static readonly DateTimeOffset Opened =
        new DateTimeOffset(2026, 9, 2, 21, 14, 0, TimeSpan.Zero);

    /// <summary>
    /// The property this file is named after. A pairing an administrator opened a window for
    /// is in the answer, with the instant it opened, so the plugin says a window is open rather
    /// than leaving it to a line written once in a log at the moment it opened.
    /// </summary>
    [Fact]
    public void WhileAWindowIsOpenThePluginSaysSo()
    {
        var records = new InMemoryPairingRecords();
        var pairingId = ProvisionalPairingId.Mint();

        records.Write(new PairingRecord(
            pairingId,
            PairingState.Offered,
            PairingState.Absent,
            "AdministratorOpenedWindow",
            "an-administrator",
            Opened,
            "https://peer.example"));

        var open = Answered(records);

        var window = Assert.Single(open);

        Assert.Equal(pairingId, window.PairingId);
        Assert.Equal(Opened, window.OpenedAt);
    }

    /// <summary>
    /// The answer carries a window that was opened rather than a record that was written. The two
    /// cases either side of this one write the record they then read, so both would pass against a
    /// producer that writes a record the reader cannot see; this one opens a window through
    /// <see cref="Enrolment"/> and asks the action, which is the half of the seventh property of
    /// issue #18 its reader could not supply on its own.
    /// </summary>
    [Fact]
    public void AWindowOpenedThroughTheProducerIsInTheAnswer()
    {
        var records = new InMemoryPairingRecords();

        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example", out var address));

        var enrolment = new Enrolment(
            new EnrolmentWindow(new RecordedPeers(records)),
            new PairingStateMachine(records, new InMemoryUserMappings()),
            records);

        var opened = enrolment.Open(address!, "an-administrator", Opened);

        var window = Assert.Single(Answered(records));

        Assert.Equal(opened.PairingId, window.PairingId);
        Assert.Equal(Opened, window.OpenedAt);
    }

    /// <summary>
    /// A window closed through the producer leaves the answer, in the same call it closes. An
    /// operator reading this page after closing one must not be shown the window they just shut,
    /// which is what a reader over a record nobody destroyed would show them.
    /// </summary>
    [Fact]
    public void AWindowClosedThroughTheProducerLeavesTheAnswer()
    {
        var records = new InMemoryPairingRecords();

        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example", out var address));

        var enrolment = new Enrolment(
            new EnrolmentWindow(new RecordedPeers(records)),
            new PairingStateMachine(records, new InMemoryUserMappings()),
            records);

        enrolment.Open(address!, "an-administrator", Opened);
        enrolment.Close(address!, "an-administrator", Opened.AddMinutes(1));

        Assert.Empty(Answered(records));
    }

    /// <summary>
    /// The floor under the first case. A reader that answered every record would say a window
    /// is open on a server whose only pairing is working, which is the answer an operator would
    /// act on by going looking for a window nobody opened.
    /// </summary>
    [Fact]
    public void APairingThatIsNotOfferedIsNotAnOpenWindow()
    {
        var records = new InMemoryPairingRecords();

        foreach (var state in Enum.GetValues<PairingState>().Where(state => state != PairingState.Offered))
        {
            records.Write(new PairingRecord(
                "pairing-" + state,
                state,
                PairingState.Absent,
                state.ToString(),
                "an-administrator",
                Opened,
                "https://peer.example"));
        }

        Assert.Empty(Answered(records));
    }

    /// <summary>
    /// A server nobody has opened a window on says so rather than saying nothing. The action
    /// answers an empty list and a 200, so the page renders a sentence an operator can read
    /// instead of leaving the line blank, which reads as a page that failed to load.
    /// </summary>
    [Fact]
    public void AServerWithNoWindowOpenAnswersAnEmptyListRatherThanAProblem()
    {
        var answered = Assert.IsType<ContentResult>(Controller(new InMemoryPairingRecords()).Windows());

        Assert.Equal(200, answered.StatusCode);
        Assert.Equal("[]", answered.Content);
    }

    /// <summary>
    /// A record store that will not read is answered as the problem that names which store
    /// failed, not as a fault. An exception leaving an action reaches the host's own pipeline,
    /// which on a server with the developer page turned on answers an administrator with a
    /// stack trace naming this plugin's types instead of with one sentence they can act on.
    /// </summary>
    [Fact]
    public void AStoreThatWillNotReadIsAnsweredAsThatProblemAndNotAsAFault()
    {
        var answered = Assert.IsType<ContentResult>(Controller(new UnreadableRecords()).Windows());

        Assert.Equal(AdministrativeAnswer.ProblemStatus, answered.StatusCode);
        Assert.Equal(
            AdministrativeAnswer.Body(AdministrativeProblem.RecordStoreUnreadable),
            answered.Content);
    }

    /// <summary>
    /// The two stores get two answers. A key store that will not read and a record store that
    /// will not read send an operator to two different files, so an answer naming the wrong one
    /// is worse than no answer: it is a sentence they can act on, pointed at the wrong disk.
    /// </summary>
    [Fact]
    public void TheTwoStoresAreNamedSeparatelyWhenTheyFail()
    {
        Assert.NotEqual(
            AdministrativeAnswer.Wire(AdministrativeProblem.KeyStoreUnreadable),
            AdministrativeAnswer.Wire(AdministrativeProblem.RecordStoreUnreadable));
    }

    /// <summary>
    /// The detail of the fault does not reach the caller. What a store throws can carry a path
    /// and what was in it, and an administrative answer is a thing an operator pastes into a
    /// support thread.
    /// </summary>
    [Fact]
    public void NothingOfTheFaultReachesTheAnswer()
    {
        var answered = Assert.IsType<ContentResult>(Controller(new UnreadableRecords()).Windows());

        Assert.DoesNotContain(UnreadableRecords.Detail, answered.Content ?? string.Empty, StringComparison.Ordinal);
    }

    /// <summary>
    /// What the action answers with, deserialised, so the cases above read what an
    /// administrator receives rather than what the store was handed.
    /// </summary>
    /// <param name="records">The record store to answer from.</param>
    /// <returns>The windows the action reported.</returns>
    private static List<OpenWindow> Answered(IPairingRecordStore records)
    {
        var answered = Assert.IsType<ContentResult>(Controller(records).Windows());

        Assert.Equal(200, answered.StatusCode);

        return JsonSerializer.Deserialize<List<OpenWindow>>(answered.Content ?? "[]")
            ?? new List<OpenWindow>();
    }

    /// <summary>
    /// A controller over a given record store, with an empty key store beside it and the logger
    /// going nowhere.
    /// </summary>
    /// <param name="records">The record store.</param>
    /// <returns>The controller.</returns>
    private static AdministrativePlaneController Controller(IPairingRecordStore records)
        => new AdministrativePlaneController(
            new InMemoryPairingKeyStore(),
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            new HeldAboutUser(new InMemoryUserMappings(), NullLogger<HeldAboutUser>.Instance),
            new UserMappings(new InMemoryUserMappings(), new PairingStateMachine(new InMemoryPairingRecords(), new InMemoryUserMappings()), NullLogger<UserMappings>.Instance),
            new InMemoryLocalUsers(),
            PlaneDependencies.EnrolmentOver(records),
            PlaneDependencies.NothingEntered(),
            PlaneDependencies.StoppedClock(),
            NullLogger<AdministrativePlaneController>.Instance);

    /// <summary>
    /// A record store whose walk throws, which is what a file that does not parse does.
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
}
