using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Protocol;
using MediaBrowser.Controller;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// Everything this plugin sends leaves through one type, so the rules that keep a request on
/// the approved address are asserted once against that type rather than per caller.
/// </summary>
/// <remarks>
/// No test here opens a socket. The handler is a constructor argument and every case drives
/// the channel over a handler written in this file.
/// </remarks>
public class PeerChannelTests
{
    private const string Path = "/ServerPairing/hello";

    /// <summary>
    /// A redirect is a peer moving the conversation to an address no operator approved, so it
    /// is an answer to refuse rather than a hop to take. The theory covers every redirect
    /// status, because a handler that follows one of them follows all of them.
    /// </summary>
    /// <param name="status">The redirect status the peer answers with.</param>
    /// <returns>A task that completes when the case has run.</returns>
    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    [InlineData(303)]
    [InlineData(307)]
    [InlineData(308)]
    public async Task ARedirectIsRefusedRatherThanFollowed(int status)
    {
        var handler = new ScriptedHandler(_ =>
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new ByteArrayContent(Array.Empty<byte>())
            };

            response.Headers.Location = new Uri("https://elsewhere.example.net/ServerPairing/hello");
            return response;
        });

        using var channel = new PeerChannel(handler);

        var reply = await SendAsync(channel).ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Redirected, reply.Outcome);
        Assert.Equal(status, reply.StatusCode);
        Assert.Empty(reply.Body.ToArray());

        // The refusal is worth nothing if the hop was taken before it was made, so the count
        // and the address are asserted rather than the outcome alone.
        Assert.Equal("https://peer.example.org/ServerPairing/hello", Assert.Single(handler.Requested));
    }

    /// <summary>
    /// The bound holds against a peer that does not say how much it is sending. What proves
    /// it was not buffered is the count of bytes the peer was allowed to hand over, which is
    /// the limit and the one byte that tells a body at the limit from a longer one.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnUndeclaredAnswerOverTheBoundIsRefusedWithoutBeingFullyBuffered()
    {
        var body = new CountingStream(PeerChannel.BodyByteLimit * 64);
        using var channel = new PeerChannel(new ScriptedHandler(_ => Answer(body, declaredLength: null)));

        var reply = await SendAsync(channel).ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.TooLarge, reply.Outcome);
        Assert.Empty(reply.Body.ToArray());
        Assert.Equal(PeerChannel.BodyByteLimit + 1, body.Served);
    }

    /// <summary>
    /// A peer that declares a body over the bound is refused on the declaration, so the body
    /// is not read at all. This is the cheap half of the rule and it is the half a peer
    /// sending a very large answer will hit.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AnAnswerThatDeclaresItselfTooLargeIsRefusedBeforeItIsRead()
    {
        var body = new CountingStream(PeerChannel.BodyByteLimit * 2);
        using var channel = new PeerChannel(new ScriptedHandler(
            _ => Answer(body, declaredLength: PeerChannel.BodyByteLimit + 1)));

        var reply = await SendAsync(channel).ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.TooLarge, reply.Outcome);
        Assert.Equal(0, body.Served);
    }

    /// <summary>
    /// The near miss the bound exists for. A body of exactly the limit is an answer and a body
    /// one byte longer is not, so an off-by-one in either direction reds one of these two.
    /// </summary>
    /// <param name="size">How many bytes the peer sends.</param>
    /// <param name="outcome">What the channel is required to make of it.</param>
    /// <returns>A task that completes when the case has run.</returns>
    [Theory]
    [InlineData(PeerChannel.BodyByteLimit - 1, PeerReplyOutcome.Answered)]
    [InlineData(PeerChannel.BodyByteLimit, PeerReplyOutcome.Answered)]
    [InlineData(PeerChannel.BodyByteLimit + 1, PeerReplyOutcome.TooLarge)]
    public async Task TheBoundIsTheLimitAndNotOneByteEitherSideOfIt(int size, PeerReplyOutcome outcome)
    {
        var body = new CountingStream(size);
        using var channel = new PeerChannel(new ScriptedHandler(_ => Answer(body, declaredLength: null)));

        var reply = await SendAsync(channel).ConfigureAwait(true);

        Assert.Equal(outcome, reply.Outcome);
        Assert.Equal(outcome == PeerReplyOutcome.Answered ? size : 0, reply.Body.Length);
    }

    /// <summary>
    /// The larger bound belongs to one message type and is not the bound every caller gets,
    /// so a caller asking for the small one is held to the small one.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task EachCallerGetsTheBoundItAskedFor()
    {
        var body = new CountingStream(PeerChannel.BodyByteLimit + 1);
        using var channel = new PeerChannel(new ScriptedHandler(_ => Answer(body, declaredLength: null)));

        var large = await channel.SendAsync(
            Approved(),
            Path,
            ReadOnlyMemory<byte>.Empty,
            null,
            PeerChannel.ExchangeBodyByteLimit,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(PeerReplyOutcome.Answered, large.Outcome);
        Assert.Equal(PeerChannel.BodyByteLimit + 1, large.Body.Length);
    }

    /// <summary>
    /// A path is appended to the approved address rather than resolved against it, because
    /// resolution treats an absolute value as a replacement for the whole address. Anything
    /// that is not a plane path throws before a request is built, so no request leaves.
    /// </summary>
    /// <param name="path">What a caller passed as the path.</param>
    /// <returns>A task that completes when the case has run.</returns>
    [Theory]
    [InlineData("https://elsewhere.example.net/ServerPairing/hello")]
    [InlineData("ServerPairing/hello")]
    [InlineData("/ServerPairing/hello?to=elsewhere.example.net")]
    [InlineData("/ServerPairing/hello#elsewhere")]
    [InlineData("")]
    public async Task ANonPathNeverBecomesARequest(string path)
    {
        var handler = new ScriptedHandler(_ => Answer(new CountingStream(0), declaredLength: null));
        using var channel = new PeerChannel(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => channel.SendAsync(
            Approved(),
            path,
            ReadOnlyMemory<byte>.Empty,
            null,
            PeerChannel.BodyByteLimit,
            CancellationToken.None)).ConfigureAwait(true);

        Assert.Empty(handler.Requested);
    }

    /// <summary>
    /// A protocol-relative value names a host to a URI parser and names a path to string
    /// concatenation, and this channel concatenates. So the one form that would escape the
    /// approved address under resolution stays on it here, which is the whole reason the
    /// target is built the way it is.
    /// </summary>
    /// <returns>A task that completes when the case has run.</returns>
    [Fact]
    public async Task AProtocolRelativePathStaysOnTheApprovedAddress()
    {
        var handler = new ScriptedHandler(_ => Answer(new CountingStream(0), declaredLength: null));
        using var channel = new PeerChannel(handler);

        await channel.SendAsync(
            Approved(),
            "//elsewhere.example.net/ServerPairing/hello",
            ReadOnlyMemory<byte>.Empty,
            null,
            PeerChannel.BodyByteLimit,
            CancellationToken.None).ConfigureAwait(true);

        Assert.Equal("peer.example.org", new Uri(Assert.Single(handler.Requested)).Host);

        // What the same value does under resolution, which is the mistake being avoided.
        Assert.Equal(
            "elsewhere.example.net",
            new Uri(Approved().Uri, "//elsewhere.example.net/ServerPairing/hello").Host);
    }

    /// <summary>
    /// The timeouts are read off the channel the registrator put in the container, so this
    /// asserts what the plugin will run with rather than what the constants say. The handler
    /// is the plugin's own, because the container built it.
    /// </summary>
    [Fact]
    public void TheChannelThePluginRegistersCarriesBothTimeoutsAndRefusesRedirects()
    {
        var services = new ServiceCollection();
        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider();
        var channel = provider.GetRequiredService<PeerChannel>();

        Assert.Equal(PeerChannel.TotalTimeout, channel.Timeout);
        Assert.Equal(PeerChannel.ConnectTimeout, channel.ConnectTimeoutInUse);
        Assert.False(channel.FollowsRedirectsInUse);

        // Not the framework defaults, which are what this assertion exists to tell it from.
        using var untouched = new HttpClient();
        Assert.NotEqual(untouched.Timeout, channel.Timeout);
        Assert.NotEqual(new SocketsHttpHandler().ConnectTimeout, channel.ConnectTimeoutInUse);
        Assert.True(new SocketsHttpHandler().AllowAutoRedirect);
    }

    /// <summary>
    /// The handler the plugin runs against a real peer also turns off what a peer could
    /// otherwise use to spend memory or to carry state across requests.
    /// </summary>
    [Fact]
    public void TheHandlerTurnsOffDecompressionCookiesAndTheProxy()
    {
        using var handler = PeerChannel.CreateHandler();

        Assert.Equal(DecompressionMethods.None, handler.AutomaticDecompression);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
    }

    /// <summary>
    /// One type owns the outbound side. A second client anywhere in the plugin is a request
    /// path none of the rules above reach, and it would be built by somebody who did not know
    /// this type was here. Read off the compiled assembly rather than the source text.
    /// </summary>
    [Fact]
    public void NoOtherTypeInThePluginHoldsAClientOrAHandler()
    {
        Assert.Equal(new[] { nameof(PeerChannel) }, HoldersOfAClient(typeof(PeerChannel).Assembly.GetTypes()));
    }

    /// <summary>
    /// The scan above passes over an assembly holding exactly one client, which it would also
    /// do if it found none at all and the expectation were wrong. This runs the same scan over
    /// a type that holds a second one, and it has to see it.
    /// </summary>
    [Fact]
    public void TheScanSeesASecondClientWhenThereIsOne()
    {
        var seen = HoldersOfAClient(new[] { typeof(PeerChannel), typeof(ASecondClient) });

        Assert.Equal(new[] { nameof(ASecondClient), nameof(PeerChannel) }, seen);
    }

    /// <summary>
    /// Names the types holding an <see cref="HttpClient"/> or an
    /// <see cref="HttpMessageHandler"/> in a field or a property.
    /// </summary>
    /// <param name="types">The types to read.</param>
    /// <returns>Their names, sorted, without duplicates.</returns>
    private static string[] HoldersOfAClient(IEnumerable<Type> types)
    {
        const BindingFlags Everything = BindingFlags.Public
            | BindingFlags.NonPublic
            | BindingFlags.Instance
            | BindingFlags.Static
            | BindingFlags.DeclaredOnly;

        static bool IsAClient(Type t)
            => typeof(HttpClient).IsAssignableFrom(t) || typeof(HttpMessageHandler).IsAssignableFrom(t);

        return types
            .Where(t => t.GetCustomAttribute<CompilerGeneratedAttribute>() is null)
            .Where(t => t.GetFields(Everything).Any(f => IsAClient(f.FieldType))
                || t.GetProperties(Everything).Any(p => IsAClient(p.PropertyType)))
            .Select(t => t.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The address every case here sends to.
    /// </summary>
    /// <returns>The approved address.</returns>
    private static PeerAddress Approved()
    {
        Assert.Equal(PeerAddressOutcome.Accepted, PeerAddress.Parse("https://peer.example.org", out var address));
        return address!;
    }

    /// <summary>
    /// One request with the ordinary bound.
    /// </summary>
    /// <param name="channel">The channel under test.</param>
    /// <returns>What came back.</returns>
    private static Task<PeerReply> SendAsync(PeerChannel channel)
        => channel.SendAsync(
            Approved(),
            Path,
            ReadOnlyMemory<byte>.Empty,
            null,
            PeerChannel.BodyByteLimit,
            CancellationToken.None);

    /// <summary>
    /// A two hundred carrying a stream, optionally declaring a length that may be a lie.
    /// </summary>
    /// <param name="body">The stream the peer serves.</param>
    /// <param name="declaredLength">What the peer says it is sending, or null to say nothing.</param>
    /// <returns>The response.</returns>
    private static HttpResponseMessage Answer(Stream body, long? declaredLength)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(body)
        };

        response.Content.Headers.ContentLength = declaredLength;
        return response;
    }

    /// <summary>
    /// The shape <see cref="NoOtherTypeInThePluginHoldsAClientOrAHandler"/> refuses. It is in
    /// the test assembly, which that scan does not read, so it proves the scan without being
    /// caught by it.
    /// </summary>
    private sealed class ASecondClient : IDisposable
    {
        private readonly HttpClient _mine = new HttpClient();

        public void Dispose() => _mine.Dispose();

        public override string ToString() => _mine.ToString() ?? nameof(ASecondClient);
    }

    /// <summary>
    /// Answers each request from a function, and records where each one was sent.
    /// </summary>
    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _answer;
        private readonly List<string> _requested = new List<string>();

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        {
            _answer = answer;
        }

        /// <summary>
        /// Gets every address a request was sent to, in order.
        /// </summary>
        public IReadOnlyList<string> Requested => _requested;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requested.Add(request.RequestUri!.AbsoluteUri);
            return Task.FromResult(_answer(request));
        }
    }

    /// <summary>
    /// A body of a given size that counts what was actually taken from it, so a bound can be
    /// asserted on the reading rather than on the outcome it produced.
    /// </summary>
    private sealed class CountingStream : Stream
    {
        private readonly long _size;
        private long _served;

        public CountingStream(long size)
        {
            _size = size;
        }

        /// <summary>
        /// Gets how many bytes a reader has been given.
        /// </summary>
        public long Served => _served;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _size;

        public override long Position
        {
            get => _served;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
            => Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            var remaining = _size - _served;

            if (remaining <= 0)
            {
                return 0;
            }

            // Short reads on purpose: a stream that hands over everything asked for in one go
            // would let a bound that only checks the first read pass.
            var take = (int)Math.Min(Math.Min(buffer.Length, remaining), 1024);

            buffer[..take].Fill(0x61);
            _served += take;
            return take;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
