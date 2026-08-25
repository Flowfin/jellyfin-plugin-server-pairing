using System;
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
    {
        var context = new DefaultHttpContext();

        context.Request.Method = PeerPlane.Method;
        context.Request.Path = routedPath;
        context.Request.Body = new MemoryStream(body);

        var feature = context.Features.Get<IHttpRequestFeature>();

        if (feature is not null)
        {
            feature.RawTarget = rawTarget!;
        }

        return new PeerPlaneController(new PeerPlane(new RequestAuthenticator(new NoPairingKeys())))
        {
            ControllerContext = new ControllerContext { HttpContext = context },
        };
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
