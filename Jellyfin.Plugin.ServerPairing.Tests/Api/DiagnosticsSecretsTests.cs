using System;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Jellyfin.Plugin.ServerPairing.Tests.Harness;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// What an operator pastes into a support thread, driven by a run that made real secrets.
/// </summary>
/// <remarks>
/// The diagnostics payload is the one surface this plugin builds for an operator to copy
/// somewhere public, so it is the surface where a leak reaches the most readers. Issue #51
/// asks that a run through the in-process harness assert the payload carries no secret the run
/// created, and that is what this file does.
/// <para>
/// EVERY CASE HERE ASSERTS BOTH HALVES IN THE SAME RUN. A case that only searched for the
/// secret would pass over an empty payload, over a payload from a run that made no secret, and
/// over a run whose messages never crossed - three greens that look exactly like the one worth
/// having. So each case also asserts that the payload carries the counts the run produced,
/// which is what says the search had something to search.
/// </para>
/// <para>
/// WHAT THIS IS NOT. #51's second condition asks for the FULL LIFECYCLE, and there is none:
/// nothing derives a key pair, which is #18, and no route ends a pairing, which is #24, so the
/// harness seeds the key an enrolment would have produced. What runs here is a signed exchange
/// and a set of refusals, not an enrolment, a rotation and a revocation.
/// </para>
/// <para>
/// AND WHAT AN ABSENCE IS WORTH IS BOUNDED BY THE ENCODINGS SOMEBODY ENUMERATED.
/// <see cref="SeededPairing"/> carries three spellings of the key, and a secret escaping
/// through a fourth passes every case here. That is the same limit <c>docs/logging.md</c>
/// states about the test #13 asks for, and it is not closed by anything in this file.
/// </para>
/// </remarks>
public class DiagnosticsSecretsTests
{
    private const string PairingId = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";

    private static readonly DateTimeOffset Start = DateTimeOffset.FromUnixTimeSeconds(1786000000);

    /// <summary>
    /// A run that verifies a message, refuses one for its body and refuses one for its
    /// signature, and then the payload an administrator would read. None of the three
    /// spellings of the key is in it, none of the nonces is, and none of the signatures is -
    /// and the payload carries the six causes the run moved, so the search had a payload.
    /// </summary>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task ThePayloadCarriesTheRunAndNoneOfWhatTheRunCreated()
    {
        using var both = new PairedInstances(Start);

        var seeded = both.PairBothSides(PairingId);

        // One that verifies, one refused before a signature is computed, and one refused at
        // verification. Three different paths through the plane, so the payload is not the
        // shape one call produces.
        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body()).ConfigureAwait(true);

        await both.Left
            .SendAsync(PairingMessage.Hello, PairingId, new byte[PeerPlane.BodyLimit + 1])
            .ConfigureAwait(true);

        both.TowardsRight.CorruptTheNext(flight => flight.WithHeader(
            PairedInstance.NonceHeader,
            new string('a', FieldShape.HexFieldLength)));

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body()).ConfigureAwait(true);

        var payload = DiagnosticsOf(both.Right);

        // The floor. Without these the search below is a search of whatever happened to be
        // there, and an empty string passes every absence anybody writes.
        Assert.Contains("\"not-accepted-in-this-state\":1", payload, StringComparison.Ordinal);
        Assert.Contains("\"body-over-its-limit\":1", payload, StringComparison.Ordinal);
        Assert.Contains("\"did-not-verify\":1", payload, StringComparison.Ordinal);

        Assert.DoesNotContain(seeded.AsHex, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.AsUpperHex, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.AsBase64, payload, StringComparison.Ordinal);

        Assert.NotEmpty(both.Left.Sent);

        foreach (var sent in both.Left.Sent)
        {
            Assert.DoesNotContain(sent.Nonce, payload, StringComparison.Ordinal);
            Assert.DoesNotContain(sent.Signature, payload, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The pairing identifier is not among what must be absent, and this case says so rather
    /// than leaving it to be inferred from the case above not looking for it.
    /// </summary>
    /// <remarks>
    /// <c>docs/logging.md</c> permits a pairing identifier and refuses key material, so the two
    /// are on opposite sides of the same list. A later edit that swept the identifier out of
    /// this surface to be safe would be removing the one value that makes the counters
    /// attributable, and this case is what it would have to argue with.
    /// </remarks>
    /// <returns>The running case.</returns>
    [Fact]
    public async Task TheIdentifierIsNotASecretAndTheSurfaceMayCountAgainstIt()
    {
        using var both = new PairedInstances(Start);

        var seeded = both.PairBothSides(PairingId);

        await both.Left.SendAsync(PairingMessage.Exchange, PairingId, Body()).ConfigureAwait(true);

        Assert.Equal(1, both.Right.Arrivals.Counted(seeded.PairingId));
        Assert.Equal(PairingId, seeded.PairingId);

        var payload = DiagnosticsOf(both.Right);

        Assert.Contains("\"identifiersBeingCounted\":1", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.AsHex, payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A run that made a secret and sent nothing still produces a payload, and it is the
    /// payload of a server nothing has reached. This is the case that would go red if the
    /// counters ever started at something other than zero, which is what would make every
    /// number above unreadable.
    /// </summary>
    [Fact]
    public void AServerNothingHasReachedReportsZeroAndNoSecret()
    {
        using var both = new PairedInstances(Start);

        var seeded = both.PairBothSides(PairingId);

        var payload = DiagnosticsOf(both.Right);

        Assert.Contains("\"refused\":0", payload, StringComparison.Ordinal);
        Assert.Contains("\"identifiersBeingCounted\":0", payload, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.AsHex, payload, StringComparison.Ordinal);
        Assert.DoesNotContain(seeded.AsBase64, payload, StringComparison.Ordinal);
    }

    /// <summary>
    /// A body every message's limit accepts, whose bytes are recognisable in an assertion.
    /// </summary>
    /// <returns>The bytes.</returns>
    private static byte[] Body()
        => System.Text.Encoding.ASCII.GetBytes("{\"probe\":\"diagnostics\"}");

    /// <summary>
    /// The bytes the diagnostics action answers with for one side, read through the action
    /// rather than off the counters, so what is searched is what an administrator receives.
    /// </summary>
    /// <param name="side">The side an administrator is looking at.</param>
    /// <returns>The serialised payload.</returns>
    private static string DiagnosticsOf(PairedInstance side)
    {
        var controller = new AdministrativePlaneController(
            side.Keys,
            new Tests.Protocol.InMemoryPairingRecords(),
            side.Refusals,
            side.Arrivals,
            NullLogger<AdministrativePlaneController>.Instance);

        var answered = Assert.IsType<ContentResult>(controller.Diagnostics());

        return answered.Content ?? string.Empty;
    }
}
