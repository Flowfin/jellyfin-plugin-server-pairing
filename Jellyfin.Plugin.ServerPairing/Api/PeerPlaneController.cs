using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The five peer paths the specification fixes, served against the host's own routing.
/// </summary>
/// <remarks>
/// This type reads the request and writes the answer. What an arriving request means is
/// <see cref="PeerPlane"/>, which decides over values rather than over an
/// <see cref="HttpContext"/>, so every rule this plane has is judged by the suite without a
/// server standing behind it.
/// <para>
/// The plane is anonymous to the host on purpose. A peer holds this plugin's own credential
/// and none of the host's, so a request here carries no token the server's authentication
/// would recognise, and requiring one would refuse every peer before this plugin saw it. The
/// credential that decides a request on this plane is the pairing key, checked in
/// <see cref="PeerPlane"/> and nowhere else. That is <c>docs/endpoints.md</c>'s distinction
/// between the peer plane and the administrative one, and issue #27 owns the table that
/// states it per endpoint.
/// </para>
/// </remarks>
[ApiController]
[AllowAnonymous]
[Route("ServerPairing")]
public sealed class PeerPlaneController : ControllerBase
{
    private readonly PeerPlane _plane;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerPlaneController"/> class.
    /// </summary>
    /// <param name="plane">The plane that decides what an arriving request means.</param>
    public PeerPlaneController(PeerPlane plane)
    {
        _plane = plane ?? throw new ArgumentNullException(nameof(plane));
    }

    /// <summary>
    /// The first message of an enrolment.
    /// </summary>
    /// <returns>The answer.</returns>
    [HttpPost("hello")]
    public Task<IActionResult> Hello() => Serve(PairingMessage.Hello);

    /// <summary>
    /// The peer's operator has compared the fingerprint.
    /// </summary>
    /// <returns>The answer.</returns>
    [HttpPost("confirm")]
    public Task<IActionResult> Confirm() => Serve(PairingMessage.Confirm);

    /// <summary>
    /// A replacement key, with the instant the old one stops verifying.
    /// </summary>
    /// <returns>The answer.</returns>
    [HttpPost("rotate")]
    public Task<IActionResult> Rotate() => Serve(PairingMessage.Rotate);

    /// <summary>
    /// The peer ends the pairing.
    /// </summary>
    /// <returns>The answer.</returns>
    [HttpPost("revoke")]
    public Task<IActionResult> Revoke() => Serve(PairingMessage.Revoke);

    /// <summary>
    /// Traffic for a consumer, opaque to this layer.
    /// </summary>
    /// <returns>The answer.</returns>
    [HttpPost("exchange")]
    public Task<IActionResult> Exchange() => Serve(PairingMessage.Exchange);

    /// <summary>
    /// Reads a body without reading past a limit.
    /// </summary>
    /// <param name="stream">The body stream.</param>
    /// <param name="limit">The most bytes the body may carry.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The bytes read, and whether the body was longer than the limit.</returns>
    /// <remarks>
    /// The buffer grows towards the limit rather than starting at it, so a caller naming the
    /// largest message cannot make this server allocate a megabyte per request by sending
    /// nothing. The read stops one byte past the limit, which is the least that separates a
    /// body at the limit from one over it, and that byte is discarded with the rest.
    /// </remarks>
    public static async Task<(ReadOnlyMemory<byte> Body, bool Exceeded)> ReadBounded(
        Stream stream,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var buffer = new byte[Math.Min(limit + 1, 8192)];
        var total = 0;

        while (true)
        {
            if (total == buffer.Length)
            {
                if (total > limit)
                {
                    return (ReadOnlyMemory<byte>.Empty, true);
                }

                Array.Resize(ref buffer, Math.Min(buffer.Length * 2, limit + 1));
            }

            var read = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);

            if (read == 0)
            {
                return (new ReadOnlyMemory<byte>(buffer, 0, total), false);
            }

            total += read;

            if (total > limit)
            {
                return (ReadOnlyMemory<byte>.Empty, true);
            }
        }
    }

    /// <summary>
    /// Reads the request this controller was reached by, as the values the plane judges.
    /// </summary>
    /// <param name="message">The message the path this arrived on belongs to.</param>
    /// <returns>The request as it arrived.</returns>
    /// <remarks>
    /// The target comes from <see cref="IHttpRequestFeature.RawTarget"/> rather than from
    /// <c>Request.Path</c>, and that is the whole reason this method is separable and tested.
    /// The routed path has already been normalised by the time an action runs: a trailing
    /// slash is gone, a percent-encoded byte is decoded, and the match ignored case. Judging
    /// the normalised path would accept every deviation the specification refuses, and the
    /// answer would look identical from outside, because every answer this plane gives today
    /// is the same refusal.
    /// <para>
    /// A header carried more than once is read as absent rather than as its first value. Two
    /// values for one covered field is a request two readers can disagree about, and this is
    /// the plane where that disagreement is a signature over different bytes.
    /// </para>
    /// <para>
    /// <see cref="NonActionAttribute"/> is the whole reason this method is not a sixth
    /// endpoint. A public instance method declared on a controller is an action unless it
    /// says so otherwise, whatever HTTP attributes it does or does not carry. Without this
    /// attribute the host's own action discovery routes it at the controller's own template,
    /// under no method constraint, on a class marked <see cref="AllowAnonymousAttribute"/> -
    /// a reachable endpoint that is not <see cref="Serve"/> and therefore not the refusal
    /// every named path gives. It is public because the suite drives it directly, which is
    /// what makes the attribute load-bearing rather than tidiness.
    /// </para>
    /// </remarks>
    [NonAction]
    public async Task<ArrivingRequest> Arriving(PairingMessage message)
    {
        var body = await ReadBounded(
            Request.Body,
            PeerPlane.BodyLimitFor(message),
            HttpContext.RequestAborted).ConfigureAwait(false);

        return new ArrivingRequest(
            HttpContext.Features.Get<IHttpRequestFeature>()?.RawTarget,
            Request.Method,
            Header("X-Pairing-Id"),
            Header("X-Pairing-Version"),
            Header("X-Pairing-Timestamp"),
            Header("X-Pairing-Nonce"),
            Header("X-Pairing-Signature"),
            body.Body,
            body.Exceeded);
    }

    private async Task<IActionResult> Serve(PairingMessage message)
    {
        var outcome = _plane.Serve(message, await Arriving(message).ConfigureAwait(false));

        return new ContentResult
        {
            StatusCode = Refusal.Status,
            ContentType = "application/json",
            Content = Refusal.Body(outcome.Code),
        };
    }

    private string? Header(string name)
        => Request.Headers.TryGetValue(name, out var values) && values.Count == 1 ? values[0] : null;
}
