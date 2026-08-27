using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The timestamp window and the nonce store, which only stop a replay together.
/// </summary>
/// <remarks>
/// A correctly signed request that is captured and sent again is still correctly signed, so
/// nothing the authenticator does refuses one. The window bounds how long a captured request
/// stays useful and the nonce store makes it usable once inside that window. Either alone
/// leaves the other's gap open.
/// <para>
/// Neither number is a secret. <c>docs/protocol.md</c> gives the default and the bound, so a
/// caller learns nothing by discovering what this server accepts that reading the document
/// would not have told them.
/// <para>
/// The span is the operator's, because two home servers disagree by seconds without anything
/// being wrong and by minutes when one of them has no time source, and no single number is
/// right for both a server on a time service and a server on a box that lost its clock.
/// <c>TimestampWindowSeconds</c> on the plugin configuration is where one is chosen. How long
/// a nonce is remembered follows it rather than being chosen beside it: it is the span taken
/// in both directions, so the two cannot be set into a state where a nonce ages out while a
/// request carrying it would still be accepted.
/// </para>
/// </para>
/// <para>
/// Nothing here reads a clock. The instant to judge against is an argument, so a skew is
/// testable without waiting for one, and which clock supplies it is issue #26.
/// </para>
/// <para>
/// The store is not persisted. A restart forgets it, and a request replayed across a restart
/// inside the window is accepted. That is a real gap, it is named in the document rather than
/// left out, and nothing here closes it.
/// </para>
/// </remarks>
public sealed class FreshnessWindow
{
    /// <summary>
    /// How far a timestamp may be from this server's clock, in seconds, in either direction,
    /// where an operator has not chosen otherwise.
    /// </summary>
    public const int WindowSeconds = 300;

    /// <summary>
    /// The widest span this type accepts, in seconds.
    /// </summary>
    /// <remarks>
    /// A quarter of an hour, which is far past what two clocks drift to and inside what a
    /// server whose clock was never set can be off by. It is a bound rather than a preference:
    /// the span is exactly how long a captured request stays useful to whoever captured it, so
    /// every second added to it is a second of replay window bought, and an operator who
    /// cannot pair inside a quarter of an hour of skew has a clock problem rather than a
    /// pairing problem.
    /// </remarks>
    public const int MaximumWindowSeconds = 900;

    /// <summary>
    /// The most nonces remembered for one pairing at once.
    /// </summary>
    /// <remarks>
    /// The bound is on entries rather than on the size of some structure nobody measured, and
    /// it is per pairing so that one peer cannot crowd out another's. Reaching it means a
    /// pairing sustained more than <c>4096 / 600</c> requests a second, near seven, for ten
    /// minutes. Two servers exchanging watch state do not, and something that does is the case
    /// this bound exists for.
    /// </remarks>
    public const int NoncesPerPairing = 4096;

    private readonly Dictionary<string, Dictionary<string, long>> _seen =
        new Dictionary<string, Dictionary<string, long>>(StringComparer.Ordinal);

    private readonly Lock _gate = new Lock();

    private readonly int _windowSeconds;

    /// <summary>
    /// Initializes a new instance of the <see cref="FreshnessWindow"/> class with the span an
    /// operator who has chosen nothing gets.
    /// </summary>
    public FreshnessWindow()
        : this(WindowSeconds)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FreshnessWindow"/> class.
    /// </summary>
    /// <param name="windowSeconds">How far a timestamp may be from this server's clock, in
    /// either direction.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The span is not positive, or is wider than <see cref="MaximumWindowSeconds"/>.
    /// </exception>
    public FreshnessWindow(int windowSeconds)
    {
        if (windowSeconds < 1 || windowSeconds > MaximumWindowSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(windowSeconds),
                windowSeconds,
                "A freshness window is between one second and " + MaximumWindowSeconds + " seconds.");
        }

        _windowSeconds = windowSeconds;
    }

    /// <summary>
    /// Gets how far a timestamp may be from this server's clock, in seconds, in either
    /// direction.
    /// </summary>
    public int AcceptedSkewSeconds => _windowSeconds;

    /// <summary>
    /// Gets how long a nonce is remembered, in seconds.
    /// </summary>
    /// <remarks>
    /// The window taken in both directions, which is the widest gap there can be between the
    /// first arrival of a request and the last instant a copy of it would still be inside the
    /// window, so a nonce cannot age out while a request carrying it would still be accepted.
    /// It is derived rather than configured for exactly that reason: the two set apart are two
    /// numbers an operator can put into a state where the store forgets a replay it is there
    /// to refuse.
    /// </remarks>
    public int RememberedSeconds => _windowSeconds * 2;

    /// <summary>
    /// How many nonces are remembered for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <returns>The count, which is zero where nothing is remembered.</returns>
    /// <remarks>
    /// This exists so that the bound can be asserted rather than believed, and so that the
    /// diagnostics surface in issue #51 has a number to show. It says nothing about any nonce.
    /// </remarks>
    public int Remembered(string pairingId)
    {
        lock (_gate)
        {
            return _seen.TryGetValue(pairingId, out var nonces) ? nonces.Count : 0;
        }
    }

    /// <summary>
    /// How many pairings hold a remembered nonce.
    /// </summary>
    /// <returns>The count.</returns>
    /// <remarks>
    /// This grows with the number of pairings that have sent a request since the process
    /// started, and no further. Only a request that verified reaches here, so the pairings it
    /// can count are the ones the key store holds, and a pairing that ends is dropped by
    /// <see cref="Forget(string)"/>.
    /// </remarks>
    public int PairingsRemembered()
    {
        lock (_gate)
        {
            return _seen.Count;
        }
    }

    /// <summary>
    /// Drops everything remembered for a pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier.</param>
    /// <remarks>
    /// A pairing that has ended has no future request to replay, so what is held for it is
    /// held for nothing. Revocation is issue #24 and is what calls this.
    /// </remarks>
    public void Forget(string pairingId)
    {
        lock (_gate)
        {
            _seen.Remove(pairingId);
        }
    }

    /// <summary>
    /// Whether a request is fresh, and remembers its nonce where it is.
    /// </summary>
    /// <param name="pairingId">The pairing the request arrived on.</param>
    /// <param name="nonce">The nonce it carries.</param>
    /// <param name="timestamp">The timestamp it carries, as the header value.</param>
    /// <param name="now">This server's clock.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// The timestamp is judged before the nonce, so a replay that arrives outside the window
    /// is refused for the timestamp rather than for the nonce. That ordering is what makes the
    /// two reasons distinguishable to whoever is reading the log, and it also means a request
    /// nothing will accept never reaches the store and cannot fill it.
    /// </remarks>
    public FreshnessOutcome Judge(string pairingId, string nonce, string timestamp, DateTimeOffset now)
    {
        if (!FieldShape.IsUnsignedInteger(timestamp, FieldShape.TimestampDigitLimit)
            || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var claimed))
        {
            return FreshnessOutcome.Malformed;
        }

        if (!FieldShape.IsHexField(nonce))
        {
            return FreshnessOutcome.Malformed;
        }

        var here = now.ToUnixTimeSeconds();

        if (Math.Abs(here - claimed) > _windowSeconds)
        {
            return FreshnessOutcome.OutsideTheWindow;
        }

        lock (_gate)
        {
            return Remember(pairingId, nonce, here);
        }
    }

    private FreshnessOutcome Remember(string pairingId, string nonce, long here)
    {
        if (!_seen.TryGetValue(pairingId, out var nonces))
        {
            nonces = new Dictionary<string, long>(StringComparer.Ordinal);
            _seen[pairingId] = nonces;
        }

        DropAged(nonces, here);

        if (nonces.ContainsKey(nonce))
        {
            return FreshnessOutcome.AlreadySeen;
        }

        if (nonces.Count >= NoncesPerPairing)
        {
            return FreshnessOutcome.NoRoomToRemember;
        }

        nonces[nonce] = here;

        return FreshnessOutcome.Fresh;
    }

    /// <summary>
    /// Drops the nonces this pairing has held longer than they are remembered for.
    /// </summary>
    /// <remarks>
    /// Eviction is by age and by nothing else. A nonce is never dropped to make room, because
    /// dropping one that is still inside the window is exactly the replay this type exists to
    /// refuse, and an attacker who can cause it can choose which one.
    /// <para>
    /// A clock that moves backwards makes an entry look newer rather than older, so it is kept
    /// longer than it needs to be and never forgotten early. That is the safe direction of the
    /// two.
    /// </para>
    /// </remarks>
    /// <param name="nonces">The pairing's nonces.</param>
    /// <param name="here">This server's clock, in seconds since the epoch.</param>
    private void DropAged(Dictionary<string, long> nonces, long here)
    {
        if (nonces.Count == 0)
        {
            return;
        }

        List<string>? stale = null;

        foreach (var entry in nonces)
        {
            if (here - entry.Value > RememberedSeconds)
            {
                stale ??= new List<string>();
                stale.Add(entry.Key);
            }
        }

        if (stale is null)
        {
            return;
        }

        foreach (var key in stale)
        {
            nonces.Remove(key);
        }
    }
}
