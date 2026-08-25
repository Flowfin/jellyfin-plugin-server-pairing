using System;
using System.Collections.Generic;
using System.Threading;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The bounded interval during which this server will answer a party it has not authenticated.
/// </summary>
/// <remarks>
/// It is the only moment a stranger gets a free shot, so it is as small as it can be made and
/// it fails closed at every edge: it opens only when an administrator opens it, it closes on
/// the first acceptance, it closes on a timer whether or not it was used, and it closes after
/// a small number of failures.
/// <para>
/// A window is held against the address an administrator entered and against nothing else,
/// which is how <c>docs/protocol.md</c> says a hello is matched to one. There is no pairing
/// identifier to hold it under: the identifier is derived from both public keys and only one
/// of them exists while a window is open, which is why a hello carries 32 zero characters
/// where the identifier goes.
/// </para>
/// <para>
/// No key material is held here. A hello proves possession of the key it offers, the caller
/// verifies that and hands in the answer, and the key itself goes to the record and the key
/// store. A window that held bytes would be a fourth place key material lives, which is what
/// issue #32 is written against.
/// </para>
/// <para>
/// Nothing here reads a clock or a source address. The instant to judge against is an
/// argument, so an expiry is testable without waiting for one, and a failure is counted
/// against the window rather than against wherever a request appeared to come from, because
/// an address a packet carries is not an identity.
/// </para>
/// </remarks>
public sealed class EnrolmentWindow
{
    /// <summary>
    /// How long a window stays open, in seconds, where an operator has not chosen otherwise.
    /// </summary>
    /// <remarks>
    /// Ten minutes. It is long enough for two operators who are already talking to each other
    /// to read an address to one side and press a button on the other, and short enough that a
    /// window somebody opened and forgot is closed before they have left the room. The
    /// configuration surface this belongs on is issue #50 and does not exist, so this is the
    /// value until it does.
    /// </remarks>
    public const int LifetimeSeconds = 600;

    /// <summary>
    /// The longest lifetime this type accepts, in seconds.
    /// </summary>
    /// <remarks>
    /// Half an hour, and a configured value above it is refused rather than clamped, because a
    /// clamp gives an operator a window of a length they did not ask for and no reason to look
    /// for one. Refusing at construction is what makes the bound hold for every caller; issue
    /// #50 owes the same refusal at the moment a configuration file is read.
    /// </remarks>
    public const int MaximumLifetimeSeconds = 1800;

    /// <summary>
    /// How many failed attempts a window takes before it closes.
    /// </summary>
    /// <remarks>
    /// Three, and the third is the one that closes it: it is refused like the two before it
    /// and the window is gone in the same call, so there is no fourth attempt left to make. An
    /// operator who is enrolling watches it happen, so a retry costs them a fresh window and
    /// nothing else; a stranger guessing gets three attempts against a window an administrator
    /// happens to have open, and then the door is shut rather than left ajar.
    /// </remarks>
    public const int FailuresAllowed = 3;

    private readonly IPairedPeers _paired;

    private readonly int _lifetimeSeconds;

    private readonly Dictionary<string, Attempted> _open =
        new Dictionary<string, Attempted>(StringComparer.Ordinal);

    private readonly Lock _gate = new Lock();

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrolmentWindow"/> class with the default
    /// lifetime.
    /// </summary>
    /// <param name="paired">What this server is already paired with.</param>
    public EnrolmentWindow(IPairedPeers paired)
        : this(paired, LifetimeSeconds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrolmentWindow"/> class.
    /// </summary>
    /// <param name="paired">What this server is already paired with.</param>
    /// <param name="lifetimeSeconds">How long a window opened here stays open.</param>
    /// <exception cref="ArgumentNullException"><paramref name="paired"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The lifetime is not positive, or is longer than <see cref="MaximumLifetimeSeconds"/>.
    /// </exception>
    public EnrolmentWindow(IPairedPeers paired, int lifetimeSeconds)
    {
        _paired = paired ?? throw new ArgumentNullException(nameof(paired));

        if (lifetimeSeconds < 1 || lifetimeSeconds > MaximumLifetimeSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lifetimeSeconds),
                lifetimeSeconds,
                "An enrolment window lasts between one second and " + MaximumLifetimeSeconds + " seconds.");
        }

        _lifetimeSeconds = lifetimeSeconds;
    }

    /// <summary>
    /// Opens a window against a peer address.
    /// </summary>
    /// <param name="address">The address an administrator entered.</param>
    /// <param name="at">When the administrator asked.</param>
    /// <returns>Whether it opened, and where it did not, why.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <remarks>
    /// This is the only way a window comes into existence. Nothing arriving from a peer reaches
    /// it, nothing on the startup path calls it, and there is no overload that opens one
    /// without an address a person typed.
    /// </remarks>
    public WindowOpening Open(PeerAddress address, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (_paired.HasPairingWith(address))
        {
            return WindowOpening.AlreadyPaired;
        }

        lock (_gate)
        {
            if (IsOpenAt(address.Value, at))
            {
                return WindowOpening.AlreadyOpen;
            }

            _open[address.Value] = new Attempted(at.AddSeconds(_lifetimeSeconds));

            return WindowOpening.Opened;
        }
    }

    /// <summary>
    /// Presents an arriving hello at whatever window is held for the address it names.
    /// </summary>
    /// <param name="address">The address the window was opened against.</param>
    /// <param name="proof">Whether the hello proved possession of the key it offers.</param>
    /// <param name="at">When it arrived.</param>
    /// <returns>Whether the window accepted it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    /// <remarks>
    /// Acceptance closes the window in the same call, so a second hello against it is refused
    /// even where the arriving key verifies. A failure is counted, and the count reaching
    /// <see cref="FailuresAllowed"/> closes the window as well, so the door shuts on the
    /// attempts rather than staying open until the timer runs out.
    /// </remarks>
    public WindowUse Present(PeerAddress address, VerificationOutcome proof, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_gate)
        {
            if (!IsOpenAt(address.Value, at))
            {
                return WindowUse.Refused;
            }

            if (proof != VerificationOutcome.Verified)
            {
                var attempted = _open[address.Value];

                attempted.Failures++;

                if (attempted.Failures >= FailuresAllowed)
                {
                    _open.Remove(address.Value);
                }

                return WindowUse.Refused;
            }

            _open.Remove(address.Value);

            return WindowUse.Accepted;
        }
    }

    /// <summary>
    /// Closes a window an administrator no longer wants open.
    /// </summary>
    /// <param name="address">The address it was opened against.</param>
    /// <returns>True where a window was held for that address.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    public bool Close(PeerAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_gate)
        {
            return _open.Remove(address.Value);
        }
    }

    /// <summary>
    /// The addresses whose windows have run out of time, dropping each one as it is named.
    /// </summary>
    /// <param name="at">This server's clock.</param>
    /// <returns>The addresses, in no defined order, for a caller to apply the expiry to.</returns>
    /// <remarks>
    /// An elapsed window refuses before this runs, because every reading here judges against
    /// the instant it is given rather than against the last sweep. What this adds is the moment
    /// the half-built record is destroyed: reaching <see cref="PairingState.Absent"/> deletes
    /// it, and a record nobody deletes outlives the window it belongs to. So this is the
    /// caller's cue to raise <see cref="LocalEvent.WindowExpired"/>, and it is the one thing in
    /// this type that has to be driven by something rather than by a request arriving.
    /// </remarks>
    public IReadOnlyList<string> CloseElapsed(DateTimeOffset at)
    {
        lock (_gate)
        {
            List<string>? elapsed = null;

            foreach (var entry in _open)
            {
                if (at >= entry.Value.ClosesAt)
                {
                    elapsed ??= new List<string>();
                    elapsed.Add(entry.Key);
                }
            }

            if (elapsed is null)
            {
                return Array.Empty<string>();
            }

            foreach (var address in elapsed)
            {
                _open.Remove(address);
            }

            return elapsed;
        }
    }

    /// <summary>
    /// The addresses a window is open against.
    /// </summary>
    /// <param name="at">This server's clock.</param>
    /// <returns>The addresses, in no defined order.</returns>
    /// <remarks>
    /// This is what puts an open window in front of an operator, which is the failure the rest
    /// of this type is really about: a window somebody opened and forgot. A window past its
    /// closing instant is not in the list whether or not anything has swept it.
    /// </remarks>
    public IReadOnlyList<string> OpenAddresses(DateTimeOffset at)
    {
        lock (_gate)
        {
            List<string>? open = null;

            foreach (var entry in _open)
            {
                if (at < entry.Value.ClosesAt)
                {
                    open ??= new List<string>();
                    open.Add(entry.Key);
                }
            }

            return open is null ? Array.Empty<string>() : open;
        }
    }

    /// <summary>
    /// Whether a window is held for an address and has not run out of time.
    /// </summary>
    /// <param name="address">The address it was opened against.</param>
    /// <param name="at">This server's clock.</param>
    /// <returns>True where one is open.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
    public bool IsOpen(PeerAddress address, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(address);

        lock (_gate)
        {
            return IsOpenAt(address.Value, at);
        }
    }

    private bool IsOpenAt(string address, DateTimeOffset at)
        => _open.TryGetValue(address, out var attempted) && at < attempted.ClosesAt;

    /// <summary>
    /// One open window: when it stops being one, and how many failures it has taken.
    /// </summary>
    private sealed class Attempted
    {
        public Attempted(DateTimeOffset closesAt)
        {
            ClosesAt = closesAt;
        }

        /// <summary>
        /// Gets the instant from which the window is closed.
        /// </summary>
        public DateTimeOffset ClosesAt { get; }

        /// <summary>
        /// Gets or sets how many attempts have failed against this window.
        /// </summary>
        public int Failures { get; set; }
    }
}
