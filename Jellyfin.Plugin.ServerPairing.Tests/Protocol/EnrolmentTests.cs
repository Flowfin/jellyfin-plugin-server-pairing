using System;
using System.Linq;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// The join between an enrolment window and the state a pairing is in.
/// </summary>
/// <remarks>
/// Both halves were built and proved and neither reached the other, so opening a window wrote no
/// record and no pairing had ever been in <see cref="PairingState.Offered"/> outside a fixture.
/// Every case here drives <see cref="Enrolment"/> and then reads the record store, rather than
/// asserting on what the join returned, because what the rest of the plugin sees is the store.
/// </remarks>
public class EnrolmentTests
{
    private const string Administrator = "an-administrator";

    private static readonly DateTimeOffset Noon =
        new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private static readonly string[] _thePeer = new[] { "https://peer.example" };

    /// <summary>
    /// The property the issue is named after. Opening a window writes the record the identifier
    /// section of <c>docs/protocol.md</c> describes, so the address an administrator entered is on
    /// the record before any <c>hello</c> arrives - which is the only moment it can be, because
    /// nothing on the wire carries it and no message would be believed if it did.
    /// </summary>
    [Fact]
    public void OpeningAWindowWritesTheRecordWithTheAddressOnIt()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);

        var opened = enrolment.Open(Address("https://peer.example"), Administrator, Noon);

        Assert.Equal(WindowOpening.Opened, opened.Opening);
        Assert.NotNull(opened.PairingId);

        var record = records.Read(opened.PairingId!);

        Assert.NotNull(record);
        Assert.Equal(PairingState.Offered, record.State);
        Assert.Equal(PairingState.Absent, record.CameFrom);
        Assert.Equal("https://peer.example", record.PeerAddress);
        Assert.Equal(Administrator, record.Actor);
        Assert.Equal(Noon, record.At);
    }

    /// <summary>
    /// The record is held under a provisional identifier rather than under a wire one. A window
    /// opens before any peer key has arrived, so the derived identifier does not exist yet, and a
    /// record filed under something a peer could name would be reachable from the pairing plane.
    /// </summary>
    [Fact]
    public void TheRecordIsHeldUnderAProvisionalIdentifier()
    {
        var records = new InMemoryPairingRecords();

        var opened = Join(records).Open(Address("https://peer.example"), Administrator, Noon);

        Assert.True(ProvisionalPairingId.Is(opened.PairingId));
        Assert.False(FieldShape.IsHexField(opened.PairingId));
    }

    /// <summary>
    /// Two windows against two addresses are two records. The identifier is minted per opening,
    /// so nothing collides and neither address ends up filed under the other's handle.
    /// </summary>
    [Fact]
    public void TwoAddressesAreTwoRecords()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);

        var first = enrolment.Open(Address("https://one.example"), Administrator, Noon);
        var second = enrolment.Open(Address("https://two.example"), Administrator, Noon);

        Assert.NotEqual(first.PairingId, second.PairingId);
        Assert.Equal("https://one.example", records.Read(first.PairingId!)?.PeerAddress);
        Assert.Equal("https://two.example", records.Read(second.PairingId!)?.PeerAddress);
    }

    /// <summary>
    /// An opening the window refuses writes nothing. The window answers first and the record
    /// follows it, so a refusal leaves the store exactly as it was; the reverse order would mint
    /// an identifier and write a half-built pairing for every attempt that was turned away.
    /// </summary>
    [Fact]
    public void AnOpeningTheWindowRefusesWritesNoRecord()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);
        var address = Address("https://peer.example");

        Assert.Equal(WindowOpening.Opened, enrolment.Open(address, Administrator, Noon).Opening);

        var held = records.Pairings().ToArray();

        var again = enrolment.Open(address, Administrator, Noon);

        Assert.Equal(WindowOpening.AlreadyOpen, again.Opening);
        Assert.Null(again.PairingId);
        Assert.Equal(held, records.Pairings().ToArray());
    }

    /// <summary>
    /// A window against a peer this server is already paired with is refused, and the refusal is
    /// reached through the record store rather than through a fixture. This is the fifth property
    /// of the enrolment bounds arriving on a server: until the record carried an address and
    /// something answered by it, it held in this project and nowhere else.
    /// </summary>
    [Fact]
    public void AWindowAgainstAPeerAlreadyPairedWithIsRefused()
    {
        var records = new InMemoryPairingRecords();

        records.Write(new PairingRecord(
            "9f8c1d2b3a4e5f60718293a4b5c6d7e8",
            PairingState.Active,
            PairingState.ConfirmedByPeer,
            "Confirm",
            Administrator,
            Noon,
            "https://peer.example"));

        var opened = Join(records).Open(Address("https://peer.example"), Administrator, Noon);

        Assert.Equal(WindowOpening.AlreadyPaired, opened.Opening);
        Assert.Null(opened.PairingId);
    }

    /// <summary>
    /// Closing a window destroys its record in the same call, which is the <c>Absent</c> row of
    /// the local events table and not the <c>Revoked</c> one. A window that closed without being
    /// used leaves no pairing behind, so a record that survived it would say a pairing is being
    /// built that nothing will ever move again.
    /// </summary>
    [Fact]
    public void ClosingAWindowDestroysItsRecord()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);
        var address = Address("https://peer.example");

        var opened = enrolment.Open(address, Administrator, Noon);

        Assert.True(enrolment.Close(address, Administrator, Noon.AddMinutes(1)));

        Assert.Null(records.Read(opened.PairingId!));
        Assert.Empty(records.Pairings());
    }

    /// <summary>
    /// The sweep does the same for a window that ran out of time. An elapsed window already
    /// refuses everything, so what this adds is the moment the half-built record goes.
    /// </summary>
    [Fact]
    public void AWindowThatRanOutOfTimeTakesItsRecordWithIt()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);
        var address = Address("https://peer.example");

        var opened = enrolment.Open(address, Administrator, Noon);

        var elapsed = enrolment.CloseElapsed(Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds));

        Assert.Equal(_thePeer, elapsed.ToArray());
        Assert.Null(records.Read(opened.PairingId!));
    }

    /// <summary>
    /// A window that has not run out of time keeps its record. The floor under the case above:
    /// a sweep that destroyed every offered record would pass it while ending every enrolment an
    /// operator is halfway through.
    /// </summary>
    [Fact]
    public void ASweepBeforeTheWindowElapsesKeepsTheRecord()
    {
        var records = new InMemoryPairingRecords();
        var enrolment = Join(records);

        var opened = enrolment.Open(Address("https://peer.example"), Administrator, Noon);

        Assert.Empty(enrolment.CloseElapsed(Noon.AddSeconds(EnrolmentWindow.LifetimeSeconds - 1)));
        Assert.Equal(PairingState.Offered, records.Read(opened.PairingId!)?.State);
    }

    /// <summary>
    /// A record left in <see cref="PairingState.Offered"/> by a window that is gone is retired
    /// rather than left beside the new one. Windows live in memory and records live in a file, so
    /// a server restarted while a window was open comes back holding the record and not the
    /// window; two half-built pairings for one peer would then sit in the store, one of which
    /// nothing would ever move again.
    /// </summary>
    [Fact]
    public void ARecordWhoseWindowIsGoneIsRetiredWhenTheAddressIsOpenedAgain()
    {
        var records = new InMemoryPairingRecords();
        var address = Address("https://peer.example");

        var before = Join(records).Open(address, Administrator, Noon);

        // A second join over the same store and a fresh window map, which is what a restart
        // leaves behind: the record is on disk and no window is open for it.
        var after = Join(records).Open(address, Administrator, Noon.AddHours(1));

        Assert.Equal(WindowOpening.Opened, after.Opening);
        Assert.NotEqual(before.PairingId, after.PairingId);
        Assert.Null(records.Read(before.PairingId!));
        Assert.Equal(new[] { after.PairingId }, records.Pairings().ToArray());
    }

    /// <summary>
    /// Closing an address whose window is gone destroys the record it left. That is the same
    /// restart case reached from the other side: an administrator who sees a window in the
    /// answer and closes it is asking for the record to go, and a close that only cleared an
    /// in-memory map would leave the thing they were looking at exactly where it was.
    /// </summary>
    [Fact]
    public void ClosingAnAddressWhoseWindowIsGoneStillDestroysTheRecord()
    {
        var records = new InMemoryPairingRecords();
        var address = Address("https://peer.example");

        var opened = Join(records).Open(address, Administrator, Noon);

        Assert.False(Join(records).Close(address, Administrator, Noon.AddHours(1)));
        Assert.Null(records.Read(opened.PairingId!));
    }

    /// <summary>
    /// A pairing that has moved past <c>Offered</c> is not touched by a close. The retirement is
    /// written against one state on purpose: an administrator closing a window must not be a way
    /// to delete an active pairing's record, which would take its mappings with it and leave the
    /// peer paired with a server that has forgotten it.
    /// </summary>
    [Fact]
    public void ClosingDoesNotTouchAPairingThatIsPastOffered()
    {
        var records = new InMemoryPairingRecords();
        var address = Address("https://peer.example");

        records.Write(new PairingRecord(
            "9f8c1d2b3a4e5f60718293a4b5c6d7e8",
            PairingState.Active,
            PairingState.ConfirmedByPeer,
            "Confirm",
            Administrator,
            Noon,
            "https://peer.example"));

        Join(records).Close(address, Administrator, Noon.AddMinutes(1));

        Assert.Equal(PairingState.Active, records.Read("9f8c1d2b3a4e5f60718293a4b5c6d7e8")?.State);
    }

    /// <summary>
    /// A record for another peer is not retired by a window opened against this one. The
    /// retirement matches on the address, and one that matched on the state alone would end
    /// every other enrolment an operator has in flight.
    /// </summary>
    [Fact]
    public void ARecordForAnotherAddressIsNotRetired()
    {
        var records = new InMemoryPairingRecords();

        var other = Join(records).Open(Address("https://other.example"), Administrator, Noon);

        Join(records).Open(Address("https://peer.example"), Administrator, Noon.AddHours(1));

        Assert.Equal(PairingState.Offered, records.Read(other.PairingId!)?.State);
    }

    private static PeerAddress Address(string candidate)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(candidate, out var address));

        return address!;
    }

    private static Enrolment Join(IPairingRecordStore records)
        => new Enrolment(
            new EnrolmentWindow(new RecordedPeers(records)),
            new PairingStateMachine(records, new InMemoryUserMappings()),
            records);
}
