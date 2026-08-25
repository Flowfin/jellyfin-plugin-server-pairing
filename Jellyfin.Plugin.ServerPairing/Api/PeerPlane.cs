using System;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The five peer paths, and what happens to a request that arrives on one.
/// </summary>
/// <remarks>
/// This type holds the plane and the controller holds the host. Everything decidable about an
/// arriving request is decided here, over values rather than over an <c>HttpContext</c>, so
/// the suite judges the rules without a server and the controller carries only the reading of
/// the request and the writing of the answer.
/// <para>
/// The paths, the limits and the refusal shape are all <c>docs/protocol.md</c>. A difference
/// between that document and this file is a defect in this file.
/// </para>
/// </remarks>
public sealed class PeerPlane
{
    /// <summary>
    /// The prefix every peer path carries.
    /// </summary>
    public const string Prefix = "/ServerPairing";

    /// <summary>
    /// The method every message on this plane arrives as.
    /// </summary>
    public const string Method = "POST";

    /// <summary>
    /// The most bytes an <c>exchange</c> body may carry, which is 1 MiB.
    /// </summary>
    public const int ExchangeBodyLimit = 1024 * 1024;

    /// <summary>
    /// The most bytes the body of any other message may carry, which is 8 KiB.
    /// </summary>
    public const int BodyLimit = 8 * 1024;

    private readonly RequestAuthenticator _authenticator;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerPlane"/> class.
    /// </summary>
    /// <param name="authenticator">What decides whether an arriving request is authentic.</param>
    public PeerPlane(RequestAuthenticator authenticator)
    {
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
    }

    /// <summary>
    /// The exact path a message arrives on.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The path, with no trailing slash and nothing percent-encoded.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The message is not one of the defined values.</exception>
    public static string PathFor(PairingMessage message) => message switch
    {
        PairingMessage.Hello => Prefix + "/hello",
        PairingMessage.Confirm => Prefix + "/confirm",
        PairingMessage.Rotate => Prefix + "/rotate",
        PairingMessage.Revoke => Prefix + "/revoke",
        PairingMessage.Exchange => Prefix + "/exchange",
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>
    /// The most bytes the body of a message may carry.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The limit in bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The message is not one of the defined values.</exception>
    public static int BodyLimitFor(PairingMessage message) => message switch
    {
        PairingMessage.Exchange => ExchangeBodyLimit,
        PairingMessage.Hello or PairingMessage.Confirm or PairingMessage.Rotate or PairingMessage.Revoke => BodyLimit,
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>
    /// Serves one arriving request.
    /// </summary>
    /// <param name="message">The message the path this arrived on belongs to.</param>
    /// <param name="arrived">The request as it arrived.</param>
    /// <returns>What the caller is told, and whether the body was handed past verification.</returns>
    /// <remarks>
    /// The order of the checks is the security property rather than a style. The path is
    /// compared before anything else because a request on the wrong path is not a request on
    /// this plane at all. A body over its limit is refused before a signature is computed, so
    /// a stranger cannot make this server do cryptographic work by sending a large body.
    /// Verification runs before the body is handed on, so nothing richer than bytes exists for
    /// an unauthenticated caller to reach.
    /// <para>
    /// Every answer today is <see cref="RefusalCode.Refused"/>, and that is the transition
    /// table rather than a placeholder. No key store and no record store exist, so every
    /// pairing is <see cref="PairingState.Absent"/>, and the <c>Absent</c> row of that table
    /// is the undistinguished refusal for all five messages.
    /// <c>PeerPlaneTests.TheAbsentRowRefusesEveryMessage</c> is the assertion that ties this
    /// answer to the table instead of to this sentence.
    /// </para>
    /// </remarks>
    public PeerPlaneOutcome Serve(PairingMessage message, ArrivingRequest arrived)
    {
        var path = PathFor(message);

        if (arrived is null
            || !string.Equals(arrived.RawTarget, path, StringComparison.Ordinal)
            || !string.Equals(arrived.Method, Method, StringComparison.Ordinal)
            || arrived.BodyExceededItsLimit)
        {
            return new PeerPlaneOutcome(RefusalCode.Refused, false, ReadOnlyMemory<byte>.Empty);
        }

        // The method and the path are the constants rather than what arrived, and the two are
        // the same bytes: the comparison above refused everything else. Passing the constants
        // is what makes that true to the compiler as well as to a reader.
        var request = new PairingRequest(
            Method,
            path,
            arrived.PairingId ?? string.Empty,
            arrived.Version ?? string.Empty,
            arrived.Timestamp ?? string.Empty,
            arrived.Nonce ?? string.Empty,
            arrived.Body);

        var outcome = _authenticator.VerifyThenRead(request, arrived.Signature, body => body, out var verified);

        if (outcome != VerificationOutcome.Verified)
        {
            return new PeerPlaneOutcome(RefusalCode.Refused, false, ReadOnlyMemory<byte>.Empty);
        }

        return new PeerPlaneOutcome(RefusalCode.Refused, true, verified);
    }
}
