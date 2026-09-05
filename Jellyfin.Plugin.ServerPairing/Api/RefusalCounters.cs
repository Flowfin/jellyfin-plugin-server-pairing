using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// How many requests this server has refused on the pairing plane since it started, and why.
/// </summary>
/// <remarks>
/// One instance per server, held by the composition root and read by the diagnostics action on
/// the administrative plane. A counter held per caller is not a counter, for the same reason
/// <see cref="ArrivalLimit"/> is a singleton.
/// <para>
/// WHAT IS STORED IS A CAUSE AND WHAT IS REPORTED IS BOTH. <see cref="RefusalCause"/> carries
/// the argument for that. A code's number here is derived by summing the causes that map to it,
/// so splitting a cause later leaves every code's number where it was, and an operator reading
/// the payload across two versions is not reading two different measurements under one name.
/// </para>
/// <para>
/// Nothing here names an identifier, an address, a nonce or a body. What it holds is one number
/// per member of two enumerations, so there is nothing in it that could identify a pairing, a
/// person or a peer, and the payload it feeds has the same shape on a server that has never
/// been paired as on one that has. That is a property of the type rather than of the action
/// that renders it: a field able to carry an identifier would have to be added here first.
/// </para>
/// <para>
/// Each counter is a signed 64-bit number and nothing here guards its ceiling. That is a
/// deliberate absence rather than an oversight: a guard nobody can drive is a guard nobody can
/// prove, and this one cannot be driven. A server refusing a million requests a second - which
/// is far past what the machine underneath would serve - reaches
/// <see cref="long.MaxValue"/> after about 292 thousand years, and the arithmetic is the whole
/// argument rather than a measurement. What is claimed is that the number is a count since this
/// instance was made; what is not claimed is anything about a process that has outlived the
/// species.
/// </para>
/// </remarks>
public sealed class RefusalCounters
{
    private readonly long[] _counted = new long[Enum.GetValues<RefusalCause>().Length];

    /// <summary>
    /// Every cause, in the order the enumeration declares them.
    /// </summary>
    /// <returns>The causes.</returns>
    public static IReadOnlyList<RefusalCause> Causes() => Enum.GetValues<RefusalCause>();

    /// <summary>
    /// Every code a refusal on the pairing plane may carry, in the order the enumeration
    /// declares them.
    /// </summary>
    /// <returns>The codes.</returns>
    /// <remarks>
    /// The whole taxonomy rather than the part this tree can produce. A code with no cause
    /// behind it reports zero, which is the true statement that this server has never refused
    /// anything that way, and it is a different statement from the code being absent.
    /// </remarks>
    public static IReadOnlyList<RefusalCode> Codes() => Enum.GetValues<RefusalCode>();

    /// <summary>
    /// The code a caller is told when a refusal has this cause.
    /// </summary>
    /// <param name="cause">The cause.</param>
    /// <returns>The code.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not one of the defined values.</exception>
    /// <remarks>
    /// THIS REMARK SAID EVERY CAUSE MAPS TO <see cref="RefusalCode.Refused"/>. Four do not,
    /// and they are the four the plane reaches only after a request has verified. This method
    /// is still the one place that says which code a cause answers, and it is now load-bearing
    /// rather than a single constant: <see cref="PeerPlane.Serve"/> builds its answer by asking
    /// this rather than by naming a code beside a cause, so counting cannot drift from
    /// refusing in either direction.
    /// </remarks>
    public static RefusalCode CodeFor(RefusalCause cause) => cause switch
    {
        RefusalCause.NotOnThisPlane
            or RefusalCause.BodyOverItsLimit
            or RefusalCause.ArrivalAllowanceSpent
            or RefusalCause.NoRoomToCountTheArrival
            or RefusalCause.DidNotVerify
            or RefusalCause.NotAcceptedInThisState => RefusalCode.Refused,
        RefusalCause.TimestampOutsideTheWindow => RefusalCode.Clock,
        RefusalCause.NonceAlreadySeen => RefusalCode.Replay,
        RefusalCause.NoRoomToRememberTheNonce => RefusalCode.Busy,
        RefusalCause.VersionNotSpoken or RefusalCause.NoVersionInCommon => RefusalCode.Version,
        RefusalCause.BodyDidNotParse => RefusalCode.Malformed,
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    /// <summary>
    /// The name a cause is reported under.
    /// </summary>
    /// <param name="cause">The cause.</param>
    /// <returns>The name.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not one of the defined values.</exception>
    /// <remarks>
    /// Written out rather than derived from the member name, for the reason
    /// <see cref="Refusal.Wire(RefusalCode)"/> gives about the wire: a name a reader has learned
    /// is not something a rename should move. This one never reaches the wire, and an operator
    /// still quotes it into a support thread.
    /// </remarks>
    public static string Name(RefusalCause cause) => cause switch
    {
        RefusalCause.NotOnThisPlane => "not-on-this-plane",
        RefusalCause.BodyOverItsLimit => "body-over-its-limit",
        RefusalCause.ArrivalAllowanceSpent => "arrival-allowance-spent",
        RefusalCause.NoRoomToCountTheArrival => "no-room-to-count-the-arrival",
        RefusalCause.DidNotVerify => "did-not-verify",
        RefusalCause.NotAcceptedInThisState => "not-accepted-in-this-state",
        RefusalCause.TimestampOutsideTheWindow => "timestamp-outside-the-window",
        RefusalCause.NonceAlreadySeen => "nonce-already-seen",
        RefusalCause.NoRoomToRememberTheNonce => "no-room-to-remember-the-nonce",
        RefusalCause.VersionNotSpoken => "version-not-spoken",
        RefusalCause.BodyDidNotParse => "body-did-not-parse",
        RefusalCause.NoVersionInCommon => "no-version-in-common",
        _ => throw new ArgumentOutOfRangeException(nameof(cause)),
    };

    /// <summary>
    /// Counts one refusal.
    /// </summary>
    /// <param name="cause">Why the request was refused.</param>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not one of the defined values.</exception>
    public void Record(RefusalCause cause)
    {
        // Asked for its refusal rather than for its answer: a cause outside the enumeration
        // stops here instead of being counted into a slot no reader can place.
        CodeFor(cause);

        // Interlocked rather than locked, because this sits on the path a flood takes and a
        // lock there is the thing a flood would contend for.
        Interlocked.Increment(ref _counted[(int)cause]);
    }

    /// <summary>
    /// How many refusals have had this cause.
    /// </summary>
    /// <param name="cause">The cause.</param>
    /// <returns>The count.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The cause is not one of the defined values.</exception>
    public long Counted(RefusalCause cause)
    {
        CodeFor(cause);

        return Interlocked.Read(ref _counted[(int)cause]);
    }

    /// <summary>
    /// How many refusals have carried this code.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The count, which is the sum of the causes that map to the code.</returns>
    /// <remarks>
    /// Derived rather than stored, which is what keeps a code's number where it was when a
    /// cause is split. Two reads of two causes are not one instant, so a total taken while
    /// requests are arriving is a floor rather than a still picture. That is what any counter
    /// read without stopping the server is, and no lock here would change it for the pair of
    /// numbers an operator compares across two requests anyway.
    /// </remarks>
    public long CountedFor(RefusalCode code)
    {
        long total = 0;

        foreach (var cause in Causes().Where(cause => CodeFor(cause) == code))
        {
            total += Counted(cause);
        }

        return total;
    }
}
