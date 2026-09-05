using System;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Harness;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The three dependencies a controller factory hands the administrative plane for the action
/// that opens a window, where the case is not about that action.
/// </summary>
/// <remarks>
/// One place rather than a copy per file, so a factory whose cases never open a window still
/// builds a plane the container could build: an enrolment joined over the same record store the
/// case reads, a configuration nobody has entered a peer on, and a clock that does not move.
/// <c>WindowOpeningTests</c> builds its own, because there the three are what is under test.
/// </remarks>
internal static class PlaneDependencies
{
    /// <summary>
    /// The enrolment joined over a record store, with the window refusing against the pairings
    /// that store holds.
    /// </summary>
    /// <param name="records">The record store the case reads.</param>
    /// <returns>The join.</returns>
    public static Enrolment EnrolmentOver(IPairingRecordStore records)
        => new Enrolment(
            new EnrolmentWindow(new RecordedPeers(records)),
            new PairingStateMachine(records, new InMemoryUserMappings()),
            records);

    /// <summary>
    /// A configuration nobody has entered anything on, which is one that pairs with nobody.
    /// </summary>
    /// <returns>The reading.</returns>
    public static ConfigurationReading NothingEntered() => ConfigurationReading.Of(new PluginConfiguration());

    /// <summary>
    /// A clock that does not move.
    /// </summary>
    /// <returns>The clock.</returns>
    public static TimeProvider StoppedClock()
        => new InstanceClock(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));
}
