using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// Two servers' worth of this plugin in one test process, joined by a transport that opens no
/// socket.
/// </summary>
/// <remarks>
/// This is the harness issue #29 asks for and it is the replacement <c>docs/testing.md</c>
/// names for booting two real Jellyfin servers. Each side gets its own key store on its own
/// temporary path, its own configuration, its own clock, its own arrival limit and its own
/// refusal counters, and a message from one reaches the other's real controller: the header
/// reading, the bounded body read and the answer writing are the plugin's own code rather than
/// anything written here.
/// <para>
/// WHAT IT DOES NOT PROVE. The real HTTP stack, the real serialiser and the real Jellyfin
/// routing are absent, so a message this harness accepts is one this plugin's own types
/// accepted rather than one that survived a round trip through a host. That gap is #70, which
/// exercises a packaged plugin over real HTTP, and nothing here closes it.
/// </para>
/// <para>
/// WHAT IT DOES NOT DO IS ENROL. <see cref="PairBothSides"/> puts a key into both stores
/// directly. It derives nothing, no key pair is generated, no fingerprint is computed and no
/// operator confirms anything, so it is the state an enrolment would leave behind rather than
/// an enrolment. What it starts from is a key nobody agreed on, and the issue that would agree
/// one is the ceremony in #19. THIS SAID "THE ENROLMENT IS #18 AND THE CEREMONY IS #19": #18 is
/// the window and it is in the tree, so naming it here read as two absences where there is one. Revocation is #24, and there is no
/// route that ends a pairing, so the full run from enrolment through revocation that this
/// issue's first condition asks for cannot be written yet and is not written here.
/// </para>
/// </remarks>
internal sealed class PairedInstances : IDisposable
{
    private readonly List<PairedInstance> _sides;

    /// <summary>
    /// Initializes a new instance of the <see cref="PairedInstances"/> class.
    /// </summary>
    /// <param name="startsAt">The instant both clocks start at, before a case moves either.</param>
    /// <param name="arrivalsPerPairing">
    /// How many arrivals one identifier gets per window on each side. The default is the
    /// plugin's own; a case about the limit passes a smaller one so it does not have to send
    /// sixty messages to reach it.
    /// </param>
    /// <param name="arrivalWindowSeconds">How long that window is on each side.</param>
    public PairedInstances(
        DateTimeOffset startsAt,
        int arrivalsPerPairing = ArrivalLimit.ArrivalsPerPairing,
        int arrivalWindowSeconds = ArrivalLimit.WindowSeconds)
    {
        Left = new PairedInstance(
            "left",
            AddressOf("https://left.invalid"),
            startsAt,
            new ArrivalLimit(arrivalWindowSeconds, arrivalsPerPairing, Math.Min(arrivalsPerPairing, ArrivalLimit.ArrivalsPerEnrolment)));

        Right = new PairedInstance(
            "right",
            AddressOf("https://right.invalid"),
            startsAt,
            new ArrivalLimit(arrivalWindowSeconds, arrivalsPerPairing, Math.Min(arrivalsPerPairing, ArrivalLimit.ArrivalsPerEnrolment)));

        _sides = new List<PairedInstance> { Left, Right };

        Left.JoinTo(Right.Address, new PeerChannel(new InProcessTransport(Right, TowardsRight)));
        Right.JoinTo(Left.Address, new PeerChannel(new InProcessTransport(Left, TowardsLeft)));
    }

    /// <summary>
    /// Gets one of the two sides.
    /// </summary>
    public PairedInstance Left { get; }

    /// <summary>
    /// Gets the other.
    /// </summary>
    public PairedInstance Right { get; }

    /// <summary>
    /// Gets what happens to the next message travelling from left to right.
    /// </summary>
    public MessageInterception TowardsRight { get; } = new MessageInterception();

    /// <summary>
    /// Gets what happens to the next message travelling from right to left.
    /// </summary>
    public MessageInterception TowardsLeft { get; } = new MessageInterception();

    /// <summary>
    /// Puts one key into both stores under one identifier.
    /// </summary>
    /// <param name="pairingId">The identifier both sides will use.</param>
    /// <returns>The identifier and the key bytes, so a case can assert their absence.</returns>
    /// <remarks>
    /// THIS IS NOT AN ENROLMENT AND MUST NOT BE READ AS ONE. It writes the state an enrolment
    /// would leave behind, so that everything downstream of a key existing can be exercised
    /// before the ceremony in #19 exists. What it skips is every part that carries the trust:
    /// the key pair, the
    /// exchange, the fingerprint and the two confirmations. A case built on this proves what
    /// happens once two servers hold a shared key and proves nothing about how they came to.
    /// </remarks>
    public SeededPairing PairBothSides(string pairingId)
    {
        var bytes = RandomNumberGenerator.GetBytes(KeyMaterial.Length);

        Left.Keys.Add(pairingId, KeyMaterial.From(bytes));
        Right.Keys.Add(pairingId, KeyMaterial.From(bytes));

        // The bytes are handed back rather than zeroed, and that is the one place this
        // harness deliberately keeps a secret alive. A case asserting that a surface does
        // not carry a key has to hold the key to look for it, and a case that searched for
        // a value nothing ever created would pass by asserting the absence of nothing.
        return new SeededPairing(pairingId, bytes);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var side in _sides)
        {
            side.Dispose();
        }
    }

    /// <summary>
    /// Parses an address this harness uses, refusing to carry on if the plugin will not have it.
    /// </summary>
    /// <param name="candidate">The address.</param>
    /// <returns>The parsed address.</returns>
    /// <exception cref="InvalidOperationException">The plugin refused the address.</exception>
    private static PeerAddress AddressOf(string candidate)
    {
        var outcome = PeerAddress.Parse(candidate, out var address);

        return outcome == PeerAddressOutcome.Accepted && address is not null
            ? address
            : throw new InvalidOperationException(
                "The harness address " + candidate + " is one this plugin refuses: " + outcome + ".");
    }

    /// <summary>
    /// The wire between the two sides, which hands one side's request to the other side's
    /// controller and never opens a socket.
    /// </summary>
    private sealed class InProcessTransport : HttpMessageHandler
    {
        private readonly PairedInstance _receiver;
        private readonly MessageInterception _interception;

        public InProcessTransport(PairedInstance receiver, MessageInterception interception)
        {
            _receiver = receiver;
            _interception = interception;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            var target = request.RequestUri?.PathAndQuery ?? string.Empty;

            // The route is resolved from the path the SENDER used and the corruption below may
            // still change the target the receiver reads. That is a proxy rewriting the request
            // line rather than a different endpoint being reached, which is the case the plane
            // refuses by comparing the raw target rather than the routed path.
            var message = Enum.GetValues<PairingMessage>()
                .Cast<PairingMessage?>()
                .FirstOrDefault(candidate => string.Equals(
                    PeerPlane.PathFor(candidate!.Value),
                    target,
                    StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    "The harness was asked to carry " + target + ", which is not a path on this plane.");

            var body = request.Content is null
                ? ReadOnlyMemory<byte>.Empty
                : await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

            var headers = request.Headers
                .ToDictionary(header => header.Key, header => string.Join(",", header.Value), StringComparer.Ordinal);

            var armament = _interception.Take();
            var flight = new InFlight(target, headers, body);

            if (armament.Corrupt is not null)
            {
                flight = armament.Corrupt(flight);
            }

            if (armament.Drop)
            {
                // What a sender is told when nothing arrives. PeerChannel turns this into
                // PeerReplyOutcome.Unreachable, which is the answer a real dropped message
                // produces once the connection fails.
                throw new HttpRequestException("The harness dropped this message before it arrived.");
            }

            if (armament.Delay != TimeSpan.Zero)
            {
                _receiver.Clock.Advance(armament.Delay);
            }

            var first = await DeliverAsync(message, flight, cancellationToken).ConfigureAwait(false);

            if (armament.Duplicate)
            {
                await DeliverAsync(message, flight, cancellationToken).ConfigureAwait(false);
            }

            return new HttpResponseMessage((HttpStatusCode)first.StatusCode)
            {
                Content = new StringContent(first.Body, Encoding.UTF8, "application/json"),
            };
        }

        /// <summary>
        /// Hands one message to the receiving side's controller and records what came back.
        /// </summary>
        /// <param name="message">Which of the six messages this is.</param>
        /// <param name="flight">The message as it arrives.</param>
        /// <param name="cancellationToken">The caller's cancellation token.</param>
        /// <returns>What that side answered.</returns>
        private async Task<Delivery> DeliverAsync(
            PairingMessage message,
            InFlight flight,
            CancellationToken cancellationToken)
        {
            var context = new DefaultHttpContext();

            context.Request.Method = PeerPlane.Method;
            context.Request.Path = PeerPlane.PathFor(message);
            context.RequestAborted = cancellationToken;

            foreach (var header in flight.Headers)
            {
                context.Request.Headers[header.Key] = header.Value;
            }

            var feature = context.Features.Get<IHttpRequestFeature>();

            if (feature is not null)
            {
                feature.RawTarget = flight.Path;
            }

            var servedAt = _receiver.Clock.Now;

            using var body = new MemoryStream(flight.Body.ToArray(), writable: false);

            context.Request.Body = body;

            var controller = new PeerPlaneController(_receiver.Plane, _receiver.Clock, _receiver.Log)
            {
                ControllerContext = new ControllerContext { HttpContext = context },
            };

            var answered = (ContentResult)await Serve(controller, message).ConfigureAwait(false);

            var delivery = new Delivery(
                message,
                servedAt,
                answered.StatusCode ?? 0,
                answered.Content ?? string.Empty);

            _receiver.Record(delivery);

            return delivery;
        }

        /// <summary>
        /// Calls the action the path belongs to, so the answer comes from the routed method
        /// rather than from a switch this harness holds over the plane.
        /// </summary>
        /// <param name="controller">The receiving side's controller.</param>
        /// <param name="message">Which of the six messages this is.</param>
        /// <returns>The action's result.</returns>
        private static Task<IActionResult> Serve(PeerPlaneController controller, PairingMessage message)
            => message switch
            {
                PairingMessage.Hello => controller.Hello(),
                PairingMessage.Confirm => controller.Confirm(),
                PairingMessage.Rotate => controller.Rotate(),
                PairingMessage.Revoke => controller.Revoke(),
                PairingMessage.Exchange => controller.Exchange(),
                PairingMessage.Unpair => controller.Unpair(),
                _ => throw new ArgumentOutOfRangeException(
                    nameof(message),
                    message,
                    string.Create(CultureInfo.InvariantCulture, $"There is no action for {message}.")),
            };
    }
}
