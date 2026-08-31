using System;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// The four things a case may do to the next message that crosses, and the only four.
/// </summary>
/// <remarks>
/// Dropping, delaying, duplicating and corrupting are what issue #29 names, and they are the
/// four a network does to a message without anybody's help. Holding them here rather than in
/// each case means a case says what happens to the message and not how the transport is
/// built.
/// <para>
/// EACH ONE IS ARMED FOR EXACTLY ONE MESSAGE AND THEN DISARMS. That is what lets a case send
/// the same message twice, once untouched and once interfered with, and compare the two: a
/// setting that stayed on would make the second half of such a pair impossible to write
/// without a second harness.
/// </para>
/// <para>
/// A delay is a movement of the RECEIVER'S CLOCK and never a wait. Nothing in this plugin
/// reads a real clock, so the only thing a real delay could change about how a message is
/// judged is the instant it is judged at, and that is what this moves. A case that waited
/// would be slow on the day it was written and red on some later one, which is what
/// <c>ClockSourceTests.NoTestSourceFileWaitsForRealTime</c> refuses.
/// </para>
/// </remarks>
internal sealed class MessageInterception
{
    private bool _drop;
    private bool _duplicate;
    private TimeSpan _delay;
    private Func<InFlight, InFlight>? _corrupt;

    /// <summary>
    /// Gets a value indicating whether anything is armed.
    /// </summary>
    public bool Armed => _drop || _duplicate || _delay != TimeSpan.Zero || _corrupt is not null;

    /// <summary>
    /// The next message never arrives, and the sender is told what it is told when a peer
    /// cannot be reached.
    /// </summary>
    public void DropTheNext() => _drop = true;

    /// <summary>
    /// The next message arrives, and the side it arrives at judges it this much later than it
    /// would have.
    /// </summary>
    /// <param name="by">How far the receiver's clock moves before it serves.</param>
    /// <exception cref="ArgumentOutOfRangeException">The span is negative or zero.</exception>
    public void DelayTheNextBy(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(by, TimeSpan.Zero);

        _delay = by;
    }

    /// <summary>
    /// The next message is delivered twice, and the sender is given the first answer.
    /// </summary>
    /// <remarks>
    /// One answer rather than two, because a sender that put one request on the wire reads one
    /// response off it however many copies the far side received. What the second delivery is
    /// worth is read at the receiver, which is where a replay would have to be refused.
    /// </remarks>
    public void DuplicateTheNext() => _duplicate = true;

    /// <summary>
    /// The next message arrives changed.
    /// </summary>
    /// <param name="change">What the message becomes on the way.</param>
    /// <exception cref="ArgumentNullException">The change is null.</exception>
    public void CorruptTheNext(Func<InFlight, InFlight> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        _corrupt = change;
    }

    /// <summary>
    /// Takes what is armed and disarms it.
    /// </summary>
    /// <returns>What applies to this one message.</returns>
    internal Armament Take()
    {
        var armament = new Armament(_drop, _duplicate, _delay, _corrupt);

        _drop = false;
        _duplicate = false;
        _delay = TimeSpan.Zero;
        _corrupt = null;

        return armament;
    }

    /// <summary>
    /// What applies to one message.
    /// </summary>
    /// <param name="Drop">Whether it never arrives.</param>
    /// <param name="Duplicate">Whether it arrives twice.</param>
    /// <param name="Delay">How far the receiver's clock moves before serving it.</param>
    /// <param name="Corrupt">What it becomes on the way, or null.</param>
    internal sealed record Armament(
        bool Drop,
        bool Duplicate,
        TimeSpan Delay,
        Func<InFlight, InFlight>? Corrupt);
}
