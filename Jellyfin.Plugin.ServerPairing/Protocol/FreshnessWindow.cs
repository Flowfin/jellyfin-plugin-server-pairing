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
/// Both numbers are <c>docs/protocol.md</c> and both are constants of the specification rather
/// than secrets, so a caller learns nothing by discovering them that reading the document
/// would not have told them.
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
    /// How far a timestamp may be from this server's clock, in seconds, in either direction.
    /// </summary>
    public const int WindowSeconds = 300;

    /// <summary>
    /// How long a nonce is remembered, in seconds. It is the window in both directions plus
    /// the window again, so a nonce cannot age out while a request carrying it is still inside
    /// the window.
    /// </summary>
    public const int RememberedSeconds = 600;

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

        if (Math.Abs(here - claimed) > WindowSeconds)
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
    private static void DropAged(Dictionary<string, long> nonces, long here)
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
