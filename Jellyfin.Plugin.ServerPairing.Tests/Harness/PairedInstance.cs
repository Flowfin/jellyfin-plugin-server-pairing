using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.ServerPairing.Api;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// One of the two servers, as far as this plugin is concerned.
/// </summary>
/// <remarks>
/// Everything a request is judged against on one side is held here and nowhere else: its own
/// key store on its own path, its own configuration, its own clock, its own arrival limit and
/// its own refusal counters. Two instances therefore share nothing, which is the property that
/// makes a case about skew or about one side's state mean anything.
/// <para>
/// The header names below are the ones <c>docs/protocol.md</c> fixes and the ones
/// <see cref="PeerPlaneController"/> reads. Nothing compares the two lists directly, because
/// the controller reads them as literals and no reflection reaches them; what pins them is the
/// round trip, since a name spelled differently here arrives as an absent field, fails the
/// field shape and is refused. A case asserting a message verified is therefore also the case
/// that would fail if a name drifted.
/// </para>
/// </remarks>
internal sealed class PairedInstance : IDisposable
{
    /// <summary>
    /// The header carrying the pairing identifier.
    /// </summary>
    public const string PairingIdHeader = "X-Pairing-Id";

    /// <summary>
    /// The header carrying the protocol version.
    /// </summary>
    public const string VersionHeader = "X-Pairing-Version";

    /// <summary>
    /// The header carrying the sender's instant.
    /// </summary>
    public const string TimestampHeader = "X-Pairing-Timestamp";

    /// <summary>
    /// The header carrying the nonce.
    /// </summary>
    public const string NonceHeader = "X-Pairing-Nonce";

    /// <summary>
    /// The header carrying the signature.
    /// </summary>
    public const string SignatureHeader = "X-Pairing-Signature";

    private readonly string _directory;
    private readonly List<Delivery> _delivered = new List<Delivery>();

    private PeerChannel? _channel;
    private PeerAddress? _peer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PairedInstance"/> class.
    /// </summary>
    /// <param name="name">What this side is called in an assertion message.</param>
    /// <param name="address">The address the other side reaches this one at.</param>
    /// <param name="startsAt">The instant this side's clock starts at.</param>
    /// <param name="arrivals">How much of the plane one identifier may use here.</param>
    public PairedInstance(string name, PeerAddress address, DateTimeOffset startsAt, ArrivalLimit arrivals)
    {
        Name = name;
        Address = address;
        Clock = new InstanceClock(startsAt);
        Arrivals = arrivals;
        Log = new CapturedLog();
        Configuration = new PluginConfiguration();
        Refusals = new RefusalCounters();

        // A directory of this side's own under the platform's temporary path, so the two
        // stores cannot meet and nothing is written where a real server would keep one.
        //
        // NOT CREATED HERE, AND THAT IS THE POINT RATHER THAN AN OMISSION. The store makes
        // its own directory at the mode it requires and REFUSES one it finds wider than
        // that. A harness that created it first would hand the store a directory at the
        // platform's default, which on Linux is wider, so every case would fail on the
        // store's own guard - which is what the first CI run of this file did, on the
        // net9.0 job, while every case passed on the machine it was written on because
        // Windows has no such mode to be wider.
        _directory = Path.Join(
            Path.GetTempPath(),
            "server-pairing-harness-" + Guid.NewGuid().ToString("n"));

        KeyStoreFile = Path.Join(_directory, KeyStorePath.FileName);
        Keys = new FilePairingKeyStore(KeyStoreFile);
        Plane = new PeerPlane(new RequestAuthenticator(new StoreBackedKeys(Keys)), Arrivals, Refusals);
    }

    /// <summary>
    /// Gets what this side is called.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the address the other side sends to.
    /// </summary>
    public PeerAddress Address { get; }

    /// <summary>
    /// Gets this side's clock, which a case moves independently of the other side's.
    /// </summary>
    public InstanceClock Clock { get; }

    /// <summary>
    /// Gets the file this side's keys are held in.
    /// </summary>
    public string KeyStoreFile { get; }

    /// <summary>
    /// Gets this side's key store.
    /// </summary>
    public IPairingKeyStore Keys { get; }

    /// <summary>
    /// Gets this side's configuration.
    /// </summary>
    public PluginConfiguration Configuration { get; }

    /// <summary>
    /// Gets how much of the plane one identifier may use here.
    /// </summary>
    public ArrivalLimit Arrivals { get; }

    /// <summary>
    /// Gets what this side has refused and why, since it was built.
    /// </summary>
    /// <remarks>
    /// This is the one place a case reads whether an arriving request VERIFIED, and it is the
    /// plugin's own instrument rather than a channel the harness opened. Every answer on this
    /// plane is the same refusal by design, so nothing a sender receives separates a request
    /// that verified from one that did not.
    /// <see cref="RefusalCause.NotAcceptedInThisState"/> is recorded only after verification
    /// succeeded and <see cref="RefusalCause.DidNotVerify"/> only when it failed, so the pair
    /// is what a case asserts on.
    /// </remarks>
    public RefusalCounters Refusals { get; }

    /// <summary>
    /// Gets what this side wrote to its log.
    /// </summary>
    public CapturedLog Log { get; }

    /// <summary>
    /// Gets the plane arriving requests are served by.
    /// </summary>
    public PeerPlane Plane { get; }

    /// <summary>
    /// Gets what has arrived here, oldest first.
    /// </summary>
    public IReadOnlyList<Delivery> Delivered => _delivered;

    /// <summary>
    /// Gets the address this side sends to.
    /// </summary>
    /// <exception cref="InvalidOperationException">The two sides have not been joined.</exception>
    public PeerAddress Peer
        => _peer ?? throw new InvalidOperationException("This instance has not been joined to the other one.");

    /// <summary>
    /// Gets the only way out of this side.
    /// </summary>
    /// <exception cref="InvalidOperationException">The two sides have not been joined.</exception>
    public PeerChannel Channel
        => _channel ?? throw new InvalidOperationException("This instance has not been joined to the other one.");

    /// <summary>
    /// Signs a message with the key this side holds for a pairing and puts it on the wire.
    /// </summary>
    /// <param name="message">Which of the five messages this is.</param>
    /// <param name="pairingId">The pairing it is sent under.</param>
    /// <param name="body">The body bytes.</param>
    /// <param name="cancellationToken">The caller's cancellation token.</param>
    /// <returns>What came back, or what a channel says when nothing did.</returns>
    /// <exception cref="InvalidOperationException">This side holds no key for that pairing.</exception>
    /// <remarks>
    /// The timestamp is this side's clock and the nonce is fresh for every send, so two sends
    /// of one message differ in the bytes that are signed. A case that wants the same bytes
    /// twice arranges it with <see cref="MessageInterception.DuplicateTheNext"/>, which is a
    /// duplicate on the wire rather than a second send, and those are different events.
    /// </remarks>
    public async Task<PeerReply> SendAsync(
        PairingMessage message,
        string pairingId,
        ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken = default)
    {
        var key = Keys.Live(pairingId, Clock.Now)
            ?? throw new InvalidOperationException(
                Name + " holds no key for pairing " + pairingId + ", so there is nothing to sign with.");

        var request = new PairingRequest(
            PeerPlane.Method,
            PeerPlane.PathFor(message),
            pairingId,
            SupportedVersions.Highest.ToString(CultureInfo.InvariantCulture),
            Clock.Now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
            Convert.ToHexString(RandomNumberGenerator.GetBytes(FieldShape.HexFieldLength / 2)).ToLowerInvariant(),
            body);

        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [PairingIdHeader] = request.PairingId,
            [VersionHeader] = request.Version,
            [TimestampHeader] = request.Timestamp,
            [NonceHeader] = request.Nonce,
            [SignatureHeader] = RequestAuthenticator.Sign(request, key.Span),
        };

        return await Channel.SendAsync(
            Peer,
            request.Path,
            body,
            headers,
            PeerChannel.BodyByteLimit,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _channel?.Dispose();

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    /// <summary>
    /// Joins this side to the other one.
    /// </summary>
    /// <param name="peer">The address this side sends to.</param>
    /// <param name="channel">The channel that carries it there.</param>
    internal void JoinTo(PeerAddress peer, PeerChannel channel)
    {
        _peer = peer;
        _channel = channel;
    }

    /// <summary>
    /// Records that something arrived here and was served.
    /// </summary>
    /// <param name="delivery">What arrived.</param>
    internal void Record(Delivery delivery) => _delivered.Add(delivery);
}
