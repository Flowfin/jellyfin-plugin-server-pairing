using System;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// How the peer address gets onto a pairing record, and how it does not.
/// </summary>
/// <remarks>
/// The condition is that the address arrives by what a transition is told rather than by a
/// property set after the record exists. Those two produce the same field and fail differently: a
/// record that can be given an address afterwards is a record that exists for a moment without
/// one, and every reader that ran in that moment answered that this server is paired with nobody.
/// </remarks>
public class PairingRecordAddressTests
{
    private const string PairingId = "3b1f0c7d9e2a48561bd0f37ac5e6902f";
    private const string Administrator = "an administrator";
    private const string Address = "https://peer.example";

    private static readonly DateTimeOffset At = new DateTimeOffset(2026, 9, 4, 4, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Nothing on the record can be written after it is constructed. The whole type is read-only,
    /// so an address arrives through the constructor or not at all, and this is what makes the
    /// sentence above a property of the code rather than a convention.
    /// </summary>
    [Fact]
    public void NoMemberOfARecordCanBeSetAfterItExists()
    {
        var settable = typeof(PairingRecord)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is not null)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(settable);
    }

    /// <summary>
    /// The transition writes the address it is told. This is the one way one reaches a record,
    /// and the record it produces carries it from the moment it exists.
    /// </summary>
    [Fact]
    public void ATransitionToldAnAddressWritesIt()
    {
        var records = new InMemoryPairingRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At, Parsed(Address));

        Assert.Equal(Address, records.Read(PairingId)?.PeerAddress);
    }

    /// <summary>
    /// A later transition keeps the address the record already carries without being told it
    /// again. The operator types an address once, and a transition driven by an arriving message
    /// has no address to supply, so a record that dropped it on the first move would lose it at
    /// exactly the moment a peer first answered.
    /// </summary>
    [Fact]
    public void ALaterTransitionCarriesTheAddressForward()
    {
        var records = new InMemoryPairingRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At, Parsed(Address));
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, "peer", At.AddMinutes(1));

        var moved = records.Read(PairingId);

        Assert.Equal(PairingState.Pending, moved?.State);
        Assert.Equal(Address, moved?.PeerAddress);
    }

    /// <summary>
    /// The overload that is told no address keeps the one the record carries rather than clearing
    /// it. Every transition after the opening comes through it, so a null read as "no address"
    /// would empty the field on the first administrative confirmation.
    /// </summary>
    [Fact]
    public void ATransitionToldNoAddressDoesNotClearOne()
    {
        var records = new InMemoryPairingRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At, Parsed(Address));
        machine.Receive(PairingId, PairingMessage.Hello, OfferedKey.NotApplicable, "peer", At.AddMinutes(1));
        machine.Apply(PairingId, LocalEvent.FingerprintConfirmed, Administrator, At.AddMinutes(2));

        Assert.Equal(PairingState.ConfirmedHere, records.Read(PairingId)?.State);
        Assert.Equal(Address, records.Read(PairingId)?.PeerAddress);
    }

    /// <summary>
    /// A transition that writes a record for a pairing nothing was held for, and that is told no
    /// address, writes none. There is nothing to carry forward, and a reader that took an absent
    /// address for a match would refuse every window on the strength of it.
    /// </summary>
    [Fact]
    public void ATransitionWithNothingToCarryWritesNoAddress()
    {
        var records = new InMemoryPairingRecords();
        var machine = new PairingStateMachine(records, new InMemoryUserMappings());

        machine.Apply(PairingId, LocalEvent.WindowOpened, Administrator, At);

        Assert.Equal(PairingState.Offered, records.Read(PairingId)?.State);
        Assert.Null(records.Read(PairingId)?.PeerAddress);
    }

    private static PeerAddress Parsed(string candidate)
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse(candidate, out var address));

        return address!;
    }
}
