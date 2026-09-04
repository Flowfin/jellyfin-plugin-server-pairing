using System;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Mapping;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// Issue #25's fourth condition: the supported set is defined once, and the dashboard, the
/// refusal message and the negotiation all read that same list.
/// </summary>
/// <remarks>
/// A supported set written down three times is a set that disagrees with itself the first time
/// one of the three is edited, and the disagreement is silent: a peer refused for a version it
/// does speak, or accepted for one it does not, looks like a key problem to both operators. So
/// the three readers are held against one another here rather than each against a constant.
/// <para>
/// THE NEGOTIATION'S BAND IS DERIVED RATHER THAN READ. Comparing the payload against
/// <see cref="SupportedVersions.Range"/> only says that two expressions naming one field agree,
/// which they cannot fail to do. What is asked instead is which versions the shipping overload
/// actually selects, and the endpoints of that band are then held equal to the two numbers an
/// administrator reads.
/// </para>
/// <para>
/// AND THE TWO ENDPOINTS ARE THE SAME NUMBER ON THIS BUILD, which is why one case drives
/// <see cref="DiagnosticsAnswer.Of"/> with a range whose endpoints differ. A case using only
/// the shipping constants passes with the two members swapped, and swapping them is the
/// mistake somebody actually makes.
/// </para>
/// <para>
/// WHAT IS NOT ASSERTED HERE is the version a PEER speaks, which issue #25 also asks the
/// dashboard to show. No peer has ever answered this server, nothing keeps a selected version,
/// and a member reporting one would report an invention. That half of the rule waits on a
/// plane that negotiates, and nothing here claims it.
/// </para>
/// </remarks>
public class SupportedVersionsOnEverySurfaceTests
{
    /// <summary>
    /// The member a <c>hello</c>, a version refusal and the diagnostics payload all spell the
    /// low endpoint with. One spelling across the three is the point rather than an incidental.
    /// </summary>
    private const string Low = "versionLow";

    /// <summary>
    /// The same for the high endpoint.
    /// </summary>
    private const string High = "versionHigh";

    /// <summary>
    /// What an administrator reads on the dashboard is the set this build declares. The payload
    /// is taken through the action rather than off the type, so what is judged is what a request
    /// receives.
    /// </summary>
    [Fact]
    public void TheDashboardAnswersWithTheDeclaredSet()
    {
        var answered = Payload();

        Assert.Equal(SupportedVersions.Lowest, answered.GetProperty(Low).GetInt32());
        Assert.Equal(SupportedVersions.Highest, answered.GetProperty(High).GetInt32());
    }

    /// <summary>
    /// The two members are not interchangeable. This build speaks one version, so both endpoints
    /// are the same number and a payload carrying the high endpoint twice is byte-identical to a
    /// correct one; the range is therefore handed in, and here it is handed one whose endpoints
    /// differ.
    /// </summary>
    [Fact]
    public void TheEndpointsArriveInTheOrderTheyWereGiven()
    {
        var answered = JsonSerializer.Serialize(
            DiagnosticsAnswer.Of(new RefusalCounters(), new ArrivalLimit(), new VersionRange(3, 7)));

        using var document = JsonDocument.Parse(answered);

        Assert.Equal(3, document.RootElement.GetProperty(Low).GetInt32());
        Assert.Equal(7, document.RootElement.GetProperty(High).GetInt32());
    }

    /// <summary>
    /// The dashboard and the refusal message name one range. The version refusal is the only
    /// body the taxonomy lets carry anything besides a code, so it is the other place this
    /// build's range leaves the server, and the two reaching a reader must not differ.
    /// </summary>
    [Fact]
    public void TheDashboardAndTheRefusalNameOneRange()
    {
        var answered = Payload();

        using var refused = JsonDocument.Parse(Refusal.Body(RefusalCode.Version));

        Assert.Equal(refused.RootElement.GetProperty(Low).GetInt32(), answered.GetProperty(Low).GetInt32());
        Assert.Equal(refused.RootElement.GetProperty(High).GetInt32(), answered.GetProperty(High).GetInt32());
    }

    /// <summary>
    /// The dashboard and the negotiation name one range, and the negotiation's is derived from
    /// what it selects rather than read from the field the payload already reads. The high
    /// endpoint is what the shipping overload settles on against a peer offering everything; the
    /// low endpoint is the smallest version it will still accept alone, asserted from both sides
    /// so a band one wider or one narrower than the payload fails.
    /// </summary>
    [Fact]
    public void TheDashboardAndTheNegotiationNameOneRange()
    {
        var answered = Payload();
        var low = answered.GetProperty(Low).GetInt32();
        var high = answered.GetProperty(High).GetInt32();

        var everything = VersionNegotiation.Select(new VersionRange(0, int.MaxValue));

        Assert.Equal(VersionOutcome.Selected, everything.Outcome);
        Assert.Equal(high, everything.Version);

        Assert.Equal(VersionOutcome.Selected, VersionNegotiation.Select(new VersionRange(low, low)).Outcome);
        Assert.Equal(VersionOutcome.NoVersionInCommon, VersionNegotiation.Select(new VersionRange(low - 1, low - 1)).Outcome);
    }

    /// <summary>
    /// The floor under the four above. Each of them reads two members out of an answer, and an
    /// answer that had lost them, or an action that had stopped answering, would make those
    /// assertions read nothing rather than fail. So the answer is required to parse and to carry
    /// both members as numbers, under the spelling a peer is told the same two numbers in.
    /// </summary>
    [Fact]
    public void TheActionAnsweredAndBothMembersAreThere()
    {
        var answered = Payload();

        Assert.Equal(JsonValueKind.Number, answered.GetProperty(Low).ValueKind);
        Assert.Equal(JsonValueKind.Number, answered.GetProperty(High).ValueKind);
        Assert.Contains("\"" + Low + "\":", Refusal.Body(RefusalCode.Version), StringComparison.Ordinal);
        Assert.Contains("\"" + High + "\":", Refusal.Body(RefusalCode.Version), StringComparison.Ordinal);
    }

    /// <summary>
    /// The diagnostics payload an administrator receives, read through the action rather than
    /// off the type, so a controller handing the wrong range fails here rather than passing on a
    /// type that was never asked.
    /// </summary>
    /// <returns>The parsed payload.</returns>
    private static JsonElement Payload()
    {
        var records = new Tests.Protocol.InMemoryPairingRecords();

        var controller = new AdministrativePlaneController(
            new InMemoryPairingKeyStore(),
            records,
            new RefusalCounters(),
            new ArrivalLimit(),
            new HeldAboutUser(new InMemoryUserMappings(), NullLogger<HeldAboutUser>.Instance),
            new UserMappings(
                new InMemoryUserMappings(),
                new PairingStateMachine(records, new InMemoryUserMappings()),
                NullLogger<UserMappings>.Instance),
            new InMemoryLocalUsers(),
            NullLogger<AdministrativePlaneController>.Instance);

        var answered = Assert.IsType<ContentResult>(controller.Diagnostics());

        return JsonDocument.Parse(answered.Content ?? string.Empty).RootElement.Clone();
    }
}
