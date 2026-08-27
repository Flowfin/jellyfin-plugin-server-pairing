using System;
using System.Collections.Generic;
using System.Threading;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// How many requests one claimed pairing identifier may put on the peer plane inside a window.
/// </summary>
/// <remarks>
/// This bounds the work a stranger can make this server do. It is therefore consulted before a
/// signature is computed, which is the same reason the body limit is checked before one, and
/// it is why the identifier it counts against is the claimed one rather than a verified one:
/// after verification the work is already spent.
/// <para>
/// It is a second bound beside the nonce store in <see cref="FreshnessWindow"/> rather than
/// the same one. That bound is per pairing, is reached only by a request that has already
/// verified, and exists so that a replay cannot be forgotten while it is still inside the
/// freshness window. This one is reached by anything that survives the shape checks, verified
/// or not. Neither can do the other's job, and the two answer with different codes: a full
/// nonce store is <c>busy</c>, which only a caller holding a verifying key ever sees, and this
/// is the undistinguished refusal, because the caller it usually answers has authenticated
/// nothing.
/// </para>
/// <para>
/// Counting against the claimed identifier means a stranger who knows a pairing's identifier
/// can spend that pairing's allowance. That is real, it is not repaired here, and the
/// alternative is worse: one allowance for the whole plane lets any flood starve every pairing
/// at once, where this confines it to the identifier the flood claims. What makes the
/// confinement worth having is that the identifier is derived from both public keys, so it is
/// not a secret and it is also not something a stranger can produce for a pairing they do not
/// already know.
/// </para>
/// <para>
/// Nothing here reads a clock. The instant to judge against is an argument, as it is
/// everywhere else in this plugin, and which clock supplies it at the edge is issue #26.
/// </para>
/// <para>
/// What this does not buy is availability. No limit does: a flood large enough is refused and
/// the refusals are still answered, and a peer sharing the flooded identifier is refused with
/// it. What it buys is that a flood stops at a counter instead of reaching a signature
/// computation per request.
/// </para>
/// </remarks>
public sealed class ArrivalLimit
{
    /// <summary>
    /// How long an allowance is counted over, in seconds.
    /// </summary>
    /// <remarks>
    /// The window is fixed rather than sliding: it starts at the first arrival counted into it
    /// and every arrival inside it counts against the same allowance. The price is at the
    /// boundary, where a caller may spend one allowance at the end of a window and another at
    /// the start of the next, so twice the allowance can arrive inside one span of this length.
    /// A sliding window would remove that and would cost one remembered instant per arrival
    /// instead of one counter per identifier, which is the memory this bound exists to hold
    /// down.
    /// </remarks>
    public const int WindowSeconds = 60;

    /// <summary>
    /// How many requests one pairing identifier may put on this plane inside a window.
    /// </summary>
    /// <remarks>
    /// One a second sustained, which is far above what two servers exchanging watch state do
    /// and far below what a flood does. No real pairing has been measured against it, because
    /// nothing in this plugin sends a message yet, and the number is argued here rather than
    /// picked.
    /// </remarks>
    public const int ArrivalsPerPairing = 60;

    /// <summary>
    /// How many requests may arrive inside a window claiming the enrolment identifier, or
    /// claiming nothing this protocol can read an identifier out of.
    /// </summary>
    /// <remarks>
    /// This is the harder limit, and it is harder because it is the one a stranger reaches
    /// without knowing anything. Every <c>hello</c> carries the same identifier, so they share
    /// one allowance, and an enrolment is a handful of requests between two operators sitting
    /// at two screens. The cost is stated rather than hidden: while a stranger is spending this
    /// allowance, a genuine <c>hello</c> arriving in the same window is refused with them.
    /// </remarks>
    public const int ArrivalsPerEnrolment = 6;

    /// <summary>
    /// The most identifiers counted at once.
    /// </summary>
    /// <remarks>
    /// The bound is on entries rather than on the size of some structure nobody measured, and
    /// it is what stops a stranger claiming a fresh identifier per request from growing this
    /// table without end. Reaching it needs 4096 identifiers to have arrived inside one
    /// window; a server pairs with a handful.
    /// </remarks>
    public const int PairingsCounted = 4096;

    /// <summary>
    /// The identifier every <c>hello</c> carries, which is 32 zero characters because the real
    /// one is derived from both public keys and a sender holds only one of them.
    /// </summary>
    public const string EnrolmentPairingId = "00000000000000000000000000000000";

    /// <summary>
    /// The one counter everything this protocol cannot read an identifier out of is counted under.
    /// It is not 32 characters, so no identifier a peer may send can collide with it.
    /// </summary>
    private const string Unreadable = "unreadable";

    private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>(StringComparer.Ordinal);

    private readonly Lock _gate = new Lock();

    /// <summary>
    /// The allowance an identifier has, which is the harder one for the enrolment identifier
    /// and for anything this protocol cannot read an identifier out of.
    /// </summary>
    /// <param name="pairingId">The identifier a request claimed, as it arrived.</param>
    /// <returns>The number of arrivals allowed inside one window.</returns>
    public static int AllowanceFor(string? pairingId)
    {
        var counted = CountedUnder(pairingId);

        return string.Equals(counted, EnrolmentPairingId, StringComparison.Ordinal)
            || string.Equals(counted, Unreadable, StringComparison.Ordinal)
                ? ArrivalsPerEnrolment
                : ArrivalsPerPairing;
    }

    /// <summary>
    /// How many identifiers are being counted.
    /// </summary>
    /// <returns>The count.</returns>
    /// <remarks>
    /// This exists so the bound can be asserted rather than believed, and so the diagnostics
    /// surface in issue #51 has a number to show. It names no identifier.
    /// </remarks>
    public int Counting()
    {
        lock (_gate)
        {
            return _windows.Count;
        }
    }

    /// <summary>
    /// How many arrivals are counted against an identifier in the window it is in.
    /// </summary>
    /// <param name="pairingId">The identifier a request claimed, as it arrived.</param>
    /// <returns>The count, which is zero where nothing is counted for it.</returns>
    public int Counted(string? pairingId)
    {
        lock (_gate)
        {
            return _windows.TryGetValue(CountedUnder(pairingId), out var window) ? window.Count : 0;
        }
    }

    /// <summary>
    /// Counts one arrival and says whether it is inside the allowance for the identifier it
    /// claims.
    /// </summary>
    /// <param name="pairingId">The identifier the request claimed, as it arrived.</param>
    /// <param name="now">This server's clock.</param>
    /// <returns>The outcome.</returns>
    /// <remarks>
    /// A refused arrival is not counted again. The count stops at the allowance, so a caller
    /// that keeps sending inside a window neither overflows the counter nor pushes its window
    /// further out, and the allowance comes back a window after the first arrival rather than a
    /// window after the last.
    /// </remarks>
    public ArrivalOutcome Admit(string? pairingId, DateTimeOffset now)
    {
        var counted = CountedUnder(pairingId);
        var allowance = AllowanceFor(pairingId);
        var here = now.ToUnixTimeSeconds();

        lock (_gate)
        {
            if (_windows.TryGetValue(counted, out var window))
            {
                if (Elapsed(window, here))
                {
                    _windows[counted] = new Window(here, 1);

                    return ArrivalOutcome.Admitted;
                }

                if (window.Count >= allowance)
                {
                    return ArrivalOutcome.TooMany;
                }

                _windows[counted] = new Window(window.Started, window.Count + 1);

                return ArrivalOutcome.Admitted;
            }

            if (_windows.Count >= PairingsCounted)
            {
                DropElapsed(here);

                if (_windows.Count >= PairingsCounted)
                {
                    return ArrivalOutcome.NoRoomToCount;
                }
            }

            _windows[counted] = new Window(here, 1);

            return ArrivalOutcome.Admitted;
        }
    }

    /// <summary>
    /// Whether a window has run out at an instant.
    /// </summary>
    /// <param name="window">The window.</param>
    /// <param name="here">This server's clock, in seconds since the epoch.</param>
    /// <returns>True where the window has run out.</returns>
    /// <remarks>
    /// A clock that moves backwards makes a window look younger than it is, so it is kept
    /// longer rather than ending early, and an allowance already spent is not handed back by
    /// the time moving underneath it. That is the safe direction of the two.
    /// </remarks>
    private static bool Elapsed(Window window, long here) => here - window.Started >= WindowSeconds;

    /// <summary>
    /// Which counter an arriving identifier is counted under.
    /// </summary>
    /// <param name="pairingId">The identifier a request claimed, as it arrived.</param>
    /// <returns>
    /// The identifier itself where this protocol can read one, and one shared counter where it
    /// cannot.
    /// </returns>
    /// <remarks>
    /// Everything that is not a pairing identifier is counted together. A request carrying one
    /// this protocol cannot read can never verify, so separating those by what they carry would
    /// hand a stranger a fresh allowance per malformed spelling and this table one entry per
    /// spelling.
    /// </remarks>
    private static string CountedUnder(string? pairingId)
        => FieldShape.IsHexField(pairingId) ? pairingId! : Unreadable;

    /// <summary>
    /// Drops the identifiers whose window has run out.
    /// </summary>
    /// <param name="here">This server's clock, in seconds since the epoch.</param>
    /// <remarks>
    /// Eviction is by age and by nothing else. Dropping a window that has not run out to make
    /// room would hand its allowance back, and a stranger claiming a fresh identifier per
    /// request is exactly who would cause it, so the allowance handed back would be somebody
    /// else's.
    /// </remarks>
    private void DropElapsed(long here)
    {
        List<string>? gone = null;

        foreach (var entry in _windows)
        {
            if (Elapsed(entry.Value, here))
            {
                gone ??= new List<string>();
                gone.Add(entry.Key);
            }
        }

        if (gone is null)
        {
            return;
        }

        foreach (var identifier in gone)
        {
            _windows.Remove(identifier);
        }
    }

    private readonly struct Window
    {
        public Window(long started, int count)
        {
            Started = started;
            Count = count;
        }

        public long Started { get; }

        public int Count { get; }
    }
}
