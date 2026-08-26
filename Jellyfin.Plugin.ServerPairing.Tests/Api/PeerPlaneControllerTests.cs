using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Protocol;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MediaBrowser.Controller;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.ServerPairing.Tests.Api;

/// <summary>
/// The half of the plane that touches the host: reading a request and writing the answer.
/// </summary>
/// <remarks>
/// What an arriving request means is <c>PeerPlaneTests</c>. What is asserted here is the
/// reading: which bytes of the request reach the plane, and that a body is never read past the
/// limit for its message type. Both are places where the host would otherwise decide for this
/// plugin.
/// </remarks>
public class PeerPlaneControllerTests
{
    /// <summary>
    /// The text the induced fault carries. It is written to be unmistakable in an answer:
    /// finding it there is finding this exception rather than any word a refusal might
    /// legitimately hold.
    /// </summary>
    private const string Marker = "the body stream went away, 9f8c1d2b3a4e5f60";

    /// <summary>
    /// Every message, so a case walks the five rather than naming one.
    /// </summary>
    /// <returns>The five messages.</returns>
    public static TheoryData<PairingMessage> EveryMessage()
    {
        var data = new TheoryData<PairingMessage>();

        foreach (var message in Enum.GetValues<PairingMessage>())
        {
            data.Add(message);
        }

        return data;
    }

    /// <summary>
    /// One action per path, each a POST, each on the path the specification fixes. This is the
    /// case that fails if an action is added, removed or re-routed, and it reads the
    /// attributes rather than the source text so a route spelled through a constant is still
    /// judged by what it resolves to.
    /// </summary>
    [Fact]
    public void FiveActionsAreRoutedAtTheFivePaths()
    {
        var prefix = typeof(PeerPlaneController)
            .GetCustomAttributes<RouteAttribute>()
            .Single()
            .Template;

        Assert.Equal("ServerPairing", prefix);

        var routed = typeof(PeerPlaneController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .SelectMany(method => method.GetCustomAttributes<HttpPostAttribute>()
                .Select(post => "/" + prefix + "/" + post.Template))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var fixedByTheSpecification = Enum.GetValues<PairingMessage>()
            .Select(PeerPlane.PathFor)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(fixedByTheSpecification, routed);
    }

    /// <summary>
    /// The target reaching the plane is the one on the request line, not the one routing
    /// produced. Routing has already dropped a trailing slash, decoded a percent-encoded byte
    /// and matched without regard to case by the time an action runs, so a controller reading
    /// the routed path would accept every deviation this protocol refuses.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task TheTargetReachingThePlaneIsTheOneOnTheRequestLine(PairingMessage message)
    {
        var path = PeerPlane.PathFor(message);

        foreach (var target in new[] { path, path + "/", path + "?probe=1", path.ToUpperInvariant() })
        {
            // The routed path is the normalised one in every case, which is what a controller
            // reading Request.Path would see and what would make the four indistinguishable.
            var controller = ControllerFor(target, routedPath: path, body: Array.Empty<byte>());

            var arrived = await controller.Arriving(message).ConfigureAwait(true);

            Assert.Equal(target, arrived.RawTarget);
        }
    }

    /// <summary>
    /// The five header values reach the plane, and a header carried twice reaches it as absent
    /// rather than as its first value.
    /// </summary>
    [Fact]
    public async Task TheFiveHeadersReachThePlaneAndADoubledOneDoesNot()
    {
        var path = PeerPlane.PathFor(PairingMessage.Confirm);
        var controller = ControllerFor(path, path, Array.Empty<byte>());

        controller.Request.Headers["X-Pairing-Id"] = "9f8c1d2b3a4e5f60718293a4b5c6d7e8";
        controller.Request.Headers["X-Pairing-Version"] = "1";
        controller.Request.Headers["X-Pairing-Timestamp"] = "1786000000";
        controller.Request.Headers["X-Pairing-Nonce"] = "0123456789abcdef0123456789abcdef";
        controller.Request.Headers["X-Pairing-Signature"] = "c2lnbmF0dXJl";

        var arrived = await controller.Arriving(PairingMessage.Confirm).ConfigureAwait(true);

        Assert.Equal("9f8c1d2b3a4e5f60718293a4b5c6d7e8", arrived.PairingId);
        Assert.Equal("1", arrived.Version);
        Assert.Equal("1786000000", arrived.Timestamp);
        Assert.Equal("0123456789abcdef0123456789abcdef", arrived.Nonce);
        Assert.Equal("c2lnbmF0dXJl", arrived.Signature);

        controller.Request.Headers["X-Pairing-Nonce"] = new[] { "0123456789abcdef0123456789abcdef", "fedcba9876543210fedcba9876543210" };
        controller.Request.Body = new MemoryStream(Array.Empty<byte>());

        var doubled = await controller.Arriving(PairingMessage.Confirm).ConfigureAwait(true);

        Assert.Null(doubled.Nonce);
    }

    /// <summary>
    /// A header this plane does not name never reaches it, so nothing downstream can come to
    /// depend on one the signature does not cover.
    /// </summary>
    [Fact]
    public async Task AHeaderThisPlaneDoesNotNameDoesNotReachIt()
    {
        var path = PeerPlane.PathFor(PairingMessage.Revoke);
        var controller = ControllerFor(path, path, Array.Empty<byte>());

        controller.Request.Headers["X-Pairing-Extra"] = "smuggled";
        controller.Request.Headers.Authorization = "Bearer something";

        var arrived = await controller.Arriving(PairingMessage.Revoke).ConfigureAwait(true);

        Assert.Null(arrived.PairingId);
        Assert.Null(arrived.Signature);
    }

    /// <summary>
    /// A body at the limit for its message type is read whole and is not marked as over it.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task ABodyAtTheLimitIsReadWhole(PairingMessage message)
    {
        var limit = PeerPlane.BodyLimitFor(message);
        var body = Filled(limit);

        var read = await PeerPlaneController.ReadBounded(new MemoryStream(body), limit, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.False(read.Exceeded);
        Assert.Equal(body, read.Body.ToArray());
    }

    /// <summary>
    /// One byte more than the limit is refused, and nothing of the body is carried forward.
    /// One byte is the least that separates a body at the limit from one over it, which is the
    /// mistake an off-by-one in the reader would make.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task OneByteOverTheLimitIsRefusedAndCarriesNothingForward(PairingMessage message)
    {
        var limit = PeerPlane.BodyLimitFor(message);

        var read = await PeerPlaneController.ReadBounded(new MemoryStream(Filled(limit + 1)), limit, CancellationToken.None)
            .ConfigureAwait(true);

        Assert.True(read.Exceeded);
        Assert.True(read.Body.IsEmpty);
    }

    /// <summary>
    /// The reader stops at the limit rather than draining the stream, so a body far larger
    /// than the limit costs this server the limit and no more. What is asserted is the count
    /// of bytes the stream was asked for, which is the only form of "did not read past" that
    /// is a measurement rather than an inference.
    /// </summary>
    [Fact]
    public async Task TheReaderStopsAtTheLimitRatherThanDrainingTheStream()
    {
        var limit = PeerPlane.BodyLimit;
        var counted = new CountingStream(Filled(limit * 16));

        var read = await PeerPlaneController.ReadBounded(counted, limit, CancellationToken.None).ConfigureAwait(true);

        Assert.True(read.Exceeded);
        Assert.True(counted.Delivered <= limit + 1);
    }

    /// <summary>
    /// A body arriving in pieces is assembled rather than truncated at the first read, which
    /// is what a stream does over a real connection.
    /// </summary>
    [Fact]
    public async Task ABodyArrivingInPiecesIsAssembled()
    {
        var body = Filled(9000);

        var read = await PeerPlaneController.ReadBounded(
            new DribblingStream(body, 7),
            PeerPlane.ExchangeBodyLimit,
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(read.Exceeded);
        Assert.Equal(body, read.Body.ToArray());
    }

    /// <summary>
    /// No body at all is a body of zero bytes rather than a refusal, because four of the five
    /// messages carry no body and the specification says empty means zero bytes.
    /// </summary>
    [Fact]
    public async Task NoBodyAtAllIsABodyOfZeroBytes()
    {
        var read = await PeerPlaneController.ReadBounded(
            new MemoryStream(Array.Empty<byte>()),
            PeerPlane.BodyLimit,
            CancellationToken.None).ConfigureAwait(true);

        Assert.False(read.Exceeded);
        Assert.True(read.Body.IsEmpty);
    }

    /// <summary>
    /// The answer is the refusal shape: the status the taxonomy fixes, the one-member body,
    /// and JSON as the media type. Every path answers the same way, which is the property the
    /// taxonomy exists for rather than an accident of there being one code today.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task EveryPathAnswersWithTheRefusalShape(PairingMessage message)
    {
        var path = PeerPlane.PathFor(message);
        var controller = ControllerFor(path, path, Encoding.ASCII.GetBytes("{}"));

        var answered = await Invoke(controller, message).ConfigureAwait(true);

        var content = Assert.IsType<ContentResult>(answered);

        Assert.Equal(403, content.StatusCode);
        Assert.Equal("application/json", content.ContentType);
        Assert.Equal("{\"code\":\"refused\"}", content.Content);
    }

    /// <summary>
    /// The host finds this controller by scanning the plugin assembly and builds it from the
    /// container, so what it needs has to be registered or the five paths answer with a server
    /// error instead of a refusal. This constructs it the way the framework does, out of what
    /// the registrator added and nothing else.
    /// </summary>
    [Fact]
    public void TheHostCanBuildTheControllerFromWhatTheRegistratorAdds()
    {
        var services = new ServiceCollection();

        // What the container already holds before any plugin is asked for anything. The
        // host is a generic host with Serilog on it, and the plugin registrators run
        // against that same collection. Read at the two lines this plugin builds against
        // rather than assumed:
        //
        //     gh api -H "Accept: application/vnd.github.raw" \
        //       "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Program.cs?ref=v10.11.9" \
        //       | grep -nE 'CreateDefaultBuilder|Init\(services\)|UseSerilog\(\)'
        //     168:                _jellyfinHost = Host.CreateDefaultBuilder()
        //     170:                    .ConfigureServices(services => appHost.Init(services))
        //     181:                    .UseSerilog()
        //
        // The same three at v12.0-rc3 are 169, 171 and 182. That the builder registers the
        // logging services is the generic host's own behaviour rather than something read
        // out of the server's tree, which is why this line stands here in the host's place.
        //
        // It stands in for the host and not for the registrator: everything else below
        // comes from the registrator, and a dependency it forgets still fails here.
        services.AddLogging();

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(ActivatorUtilities.CreateInstance<PeerPlaneController>(scope.ServiceProvider));
    }

    private static Task<IActionResult> Invoke(PeerPlaneController controller, PairingMessage message) => message switch
    {
        PairingMessage.Hello => controller.Hello(),
        PairingMessage.Confirm => controller.Confirm(),
        PairingMessage.Rotate => controller.Rotate(),
        PairingMessage.Revoke => controller.Revoke(),
        PairingMessage.Exchange => controller.Exchange(),
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    private static byte[] Filled(int length)
    {
        var bytes = new byte[length];

        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static PeerPlaneController ControllerFor(string? rawTarget, string routedPath, byte[] body)
        => ControllerOver(rawTarget, routedPath, new MemoryStream(body), NullLogger<PeerPlaneController>.Instance, CancellationToken.None);

    private static PeerPlaneController ControllerOver(
        string? rawTarget,
        string routedPath,
        Stream body,
        ILogger<PeerPlaneController> logger,
        CancellationToken aborted)
    {
        var context = new DefaultHttpContext();

        context.Request.Method = PeerPlane.Method;
        context.Request.Path = routedPath;
        context.Request.Body = body;
        context.RequestAborted = aborted;

        var feature = context.Features.Get<IHttpRequestFeature>();

        if (feature is not null)
        {
            feature.RawTarget = rawTarget!;
        }

        return new PeerPlaneController(new PeerPlane(new RequestAuthenticator(new NoPairingKeys())), logger)
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
    }

    /// <summary>
    /// A fault on this side is answered with the bytes a stranger gets, and the two answers
    /// are compared with each other rather than each with a literal. A framework error page,
    /// a different status or a different media type would separate a path that faults from a
    /// path that refuses, which is the one thing the single refusal code exists to prevent.
    /// </summary>
    /// <param name="message">The message.</param>
    [Theory]
    [MemberData(nameof(EveryMessage))]
    public async Task AFaultIsAnsweredWithTheSameBytesAsAnOrdinaryRefusal(PairingMessage message)
    {
        var path = PeerPlane.PathFor(message);

        var ordinary = Assert.IsType<ContentResult>(
            await Invoke(ControllerFor(path, path, Encoding.ASCII.GetBytes("{}")), message).ConfigureAwait(true));

        var faulted = Assert.IsType<ContentResult>(
            await Invoke(
                ControllerOver(path, path, new FaultingStream(Marker), new CapturingLogger(), CancellationToken.None),
                message).ConfigureAwait(true));

        Assert.Equal(ordinary.StatusCode, faulted.StatusCode);
        Assert.Equal(ordinary.ContentType, faulted.ContentType);
        Assert.Equal(ordinary.Content, faulted.Content);
    }

    /// <summary>
    /// The detail of the fault is in the log and none of it is in the answer. Both halves are
    /// asserted against the same run, because a change that stops logging and a change that
    /// starts leaking are repaired in opposite directions, and a test watching one of them
    /// passes while the other happens.
    /// </summary>
    [Fact]
    public async Task TheDetailOfAFaultIsLoggedAndNoneOfItIsAnswered()
    {
        var path = PeerPlane.PathFor(PairingMessage.Rotate);
        var log = new CapturingLogger();

        var answered = Assert.IsType<ContentResult>(
            await Invoke(
                ControllerOver(path, path, new FaultingStream(Marker), log, CancellationToken.None),
                PairingMessage.Rotate).ConfigureAwait(true));

        var written = Assert.Single(log.Written);

        Assert.Equal(LogLevel.Error, written.Level);
        Assert.Equal(Marker, Assert.IsType<IOException>(written.Fault).Message);
        Assert.Contains(nameof(PairingMessage.Rotate), written.Text, StringComparison.Ordinal);

        Assert.DoesNotContain(Marker, answered.Content, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(IOException), answered.Content, StringComparison.Ordinal);
        Assert.Equal(Refusal.Body(RefusalCode.Refused), answered.Content);
    }

    /// <summary>
    /// A caller that went away is not a fault of this side. Its request is cancelled through
    /// the abort token, nobody is left to answer, and an error line for each disconnect fills
    /// an operator log with the network rather than with this plugin. The cancellation leaves
    /// the action rather than being caught, and nothing is written.
    /// </summary>
    [Fact]
    public async Task ACallerThatWentAwayIsNotLoggedAsAFault()
    {
        var path = PeerPlane.PathFor(PairingMessage.Exchange);
        var log = new CapturingLogger();

        using var gone = new CancellationTokenSource();
        await gone.CancelAsync().ConfigureAwait(true);

        var controller = ControllerOver(path, path, new AbandonedStream(gone.Token), log, gone.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Invoke(controller, PairingMessage.Exchange)).ConfigureAwait(true);

        Assert.Empty(log.Written);
    }

    /// <summary>
    /// A cancellation the caller did not ask for is a fault of this side and is caught with
    /// the rest. This is the one-change neighbour of the case above: the same exception type,
    /// the same path, and an abort token nobody cancelled, which is the whole of what the
    /// condition on the catch separates.
    /// </summary>
    [Fact]
    public async Task ACancellationTheCallerDidNotAskForIsAFault()
    {
        var path = PeerPlane.PathFor(PairingMessage.Exchange);
        var log = new CapturingLogger();

        using var elsewhere = new CancellationTokenSource();
        await elsewhere.CancelAsync().ConfigureAwait(true);

        var answered = Assert.IsType<ContentResult>(
            await Invoke(
                ControllerOver(path, path, new AbandonedStream(elsewhere.Token), log, CancellationToken.None),
                PairingMessage.Exchange).ConfigureAwait(true));

        Assert.Equal(Refusal.Body(RefusalCode.Refused), answered.Content);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Written).Level);
    }

    private sealed class FaultingStream : Stream
    {
        private readonly string _saying;

        public FaultingStream(string saying)
        {
            _saying = saying;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
            => throw new IOException(_saying);

        public override int Read(byte[] buffer, int offset, int count) => throw new IOException(_saying);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class AbandonedStream : Stream
    {
        private readonly CancellationToken _cancelled;

        public AbandonedStream(CancellationToken cancelled)
        {
            _cancelled = cancelled;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            _cancelled.ThrowIfCancellationRequested();

            return ValueTask.FromResult(0);
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            _cancelled.ThrowIfCancellationRequested();

            return 0;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CapturingLogger : ILogger<PeerPlaneController>
    {
        public List<(LogLevel Level, string Text, Exception? Fault)> Written { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);

            Written.Add((logLevel, formatter(state, exception), exception));
        }
    }

    private sealed class CountingStream : MemoryStream
    {
        public CountingStream(byte[] bytes)
            : base(bytes)
        {
        }

        public int Delivered { get; private set; }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);

            Delivered += read;

            return read;
        }
    }

    private sealed class DribblingStream : MemoryStream
    {
        private readonly int _atMost;

        public DribblingStream(byte[] bytes, int atMost)
            : base(bytes)
        {
            _atMost = atMost;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => base.ReadAsync(buffer[..Math.Min(_atMost, buffer.Length)], cancellationToken);
    }
}
