using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The only way out of this plugin, holding every request to the address the operator
/// approved.
/// </summary>
/// <remarks>
/// One type owns the outbound side so that the rules below hold for every request rather than
/// for the ones whoever wrote them remembered. No other type in the plugin holds an
/// <see cref="HttpClient"/> or a handler in a field or a property, and a test reads the
/// compiled assembly for that rather than trusting it. What that reading cannot see is a
/// client built inside a method and used there, so the rule is wider than the assertion.
/// <para>
/// The handler is a constructor argument, so a suite drives this over an
/// <see cref="HttpMessageHandler"/> it wrote and no test opens a socket. That is the seam
/// <c>docs/testing.md</c> names for the transport.
/// </para>
/// <para>
/// What this refuses, and why each one is here rather than left to a default. A redirect is
/// refused rather than followed, because following one sends an authenticated request to an
/// address no operator approved. The answer is read against a byte limit and the read stops at
/// the limit, so a peer cannot spend this server's memory. Decompression is off, so a bounded
/// answer cannot expand past its bound after the bound was checked. Cookies are off, because
/// nothing on this plane has a session. Both timeouts are set, so a peer that accepts a
/// connection and then says nothing releases the thread anyway.
/// </para>
/// </remarks>
public sealed class PeerChannel : IDisposable
{
    /// <summary>
    /// The byte limit for every message but <see cref="PairingMessage.Exchange"/>, from the
    /// field table in <c>docs/protocol.md</c>.
    /// </summary>
    public const int BodyByteLimit = 8 * 1024;

    /// <summary>
    /// The byte limit for <see cref="PairingMessage.Exchange"/>, from the same table.
    /// </summary>
    public const int ExchangeBodyByteLimit = 1024 * 1024;

    private readonly HttpMessageHandler _handler;
    private readonly HttpClient _client;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerChannel"/> class.
    /// </summary>
    /// <param name="handler">The handler every request goes through.</param>
    public PeerChannel(HttpMessageHandler handler)
    {
        _handler = handler;
        _client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TotalTimeout
        };
    }

    /// <summary>
    /// Gets how long a connection may take to establish.
    /// </summary>
    /// <remarks>
    /// A computed property rather than a static readonly field, because a static field holding
    /// state is what <c>StaticStateTests</c> refuses across this assembly and the rule is not
    /// worth an exception for two durations.
    /// </remarks>
    public static TimeSpan ConnectTimeout => TimeSpan.FromSeconds(5);

    /// <summary>
    /// Gets how long a whole request may take, connection included.
    /// </summary>
    public static TimeSpan TotalTimeout => TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets the total timeout the client was built with, so a test can read what the plugin
    /// runs with rather than what it was told.
    /// </summary>
    public TimeSpan Timeout => _client.Timeout;

    /// <summary>
    /// Gets the connect timeout on the handler this instance actually sends through, or null
    /// where it was given a handler that has no such setting.
    /// </summary>
    /// <remarks>
    /// Read off the handler rather than restated, so a test resolving the channel the plugin
    /// registered sees the value that will be used and not the value it was meant to get. A
    /// suite driving this over its own handler gets null, which is the honest answer.
    /// </remarks>
    public TimeSpan? ConnectTimeoutInUse => (_handler as SocketsHttpHandler)?.ConnectTimeout;

    /// <summary>
    /// Gets whether the handler this instance sends through follows a redirect, or null where
    /// it was given a handler that has no such setting.
    /// </summary>
    public bool? FollowsRedirectsInUse => (_handler as SocketsHttpHandler)?.AllowAutoRedirect;

    /// <summary>
    /// Builds the handler the plugin runs against a real peer.
    /// </summary>
    /// <returns>The handler.</returns>
    public static SocketsHttpHandler CreateHandler()
        => new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = ConnectTimeout,
            AutomaticDecompression = DecompressionMethods.None,
            UseCookies = false,
            UseProxy = false,
            MaxResponseHeadersLength = 16
        };

    /// <summary>
    /// Posts one message to the approved address and reads at most the limit back.
    /// </summary>
    /// <param name="address">The address the operator approved for this pairing.</param>
    /// <param name="path">The pairing plane path, exact, as <c>docs/protocol.md</c> gives it.</param>
    /// <param name="body">The request body.</param>
    /// <param name="headers">The authentication headers, or null where there are none yet.</param>
    /// <param name="byteLimit">The most answer bytes to read.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>What came back.</returns>
    /// <exception cref="ArgumentException">A path this protocol does not accept. It is a
    /// programming mistake rather than a peer's doing, and it throws rather than refusing
    /// quietly, because a path that is itself an absolute URI would otherwise send the
    /// request somewhere other than the approved address.</exception>
    public async Task<PeerReply> SendAsync(
        PeerAddress address,
        string path,
        ReadOnlyMemory<byte> body,
        IReadOnlyDictionary<string, string>? headers,
        int byteLimit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(byteLimit);

        if (!FieldShape.IsPath(path))
        {
            throw new ArgumentException("Not a pairing plane path.", nameof(path));
        }

        // Concatenated rather than resolved against a base, because URI resolution treats a
        // path that is itself absolute as a replacement for the whole address.
        var target = new Uri(address.Value + path, UriKind.Absolute);

        using var request = new HttpRequestMessage(HttpMethod.Post, target)
        {
            Content = new ReadOnlyMemoryContent(body)
        };

        if (headers is not null)
        {
            foreach (var header in headers)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        try
        {
            using var response = await _client
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            var status = (int)response.StatusCode;

            if (status >= 300 && status <= 399)
            {
                return PeerReply.Refused(PeerReplyOutcome.Redirected, status);
            }

            var declared = response.Content.Headers.ContentLength;

            if (declared.HasValue && declared.Value > byteLimit)
            {
                return PeerReply.Refused(PeerReplyOutcome.TooLarge, status);
            }

            return await ReadBoundedAsync(response, status, byteLimit, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException)
        {
            return PeerReply.Refused(PeerReplyOutcome.Unreachable, 0);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return PeerReply.Refused(PeerReplyOutcome.TimedOut, 0);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    /// <summary>
    /// Reads the answer, stopping one byte past the limit.
    /// </summary>
    /// <param name="response">The response whose headers have arrived.</param>
    /// <param name="status">The status it carried.</param>
    /// <param name="byteLimit">The most bytes to keep.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>The answer, or a refusal where it was too large.</returns>
    /// <remarks>
    /// The one byte past the limit is what tells a body that is exactly the limit apart from
    /// one that is longer. Nothing beyond it is read, so an endless answer costs this server
    /// the limit and then ends.
    /// </remarks>
    private static async Task<PeerReply> ReadBoundedAsync(
        HttpResponseMessage response,
        int status,
        int byteLimit,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[byteLimit + 1];
        var filled = 0;

        var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);

        await using (stream.ConfigureAwait(false))
        {
            while (filled < buffer.Length)
            {
                var read = await stream
                    .ReadAsync(buffer.AsMemory(filled, buffer.Length - filled), cancellationToken)
                    .ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }

                filled += read;
            }
        }

        return filled > byteLimit
            ? PeerReply.Refused(PeerReplyOutcome.TooLarge, status)
            : PeerReply.Answered(status, new ReadOnlyMemory<byte>(buffer, 0, filled));
    }
}
