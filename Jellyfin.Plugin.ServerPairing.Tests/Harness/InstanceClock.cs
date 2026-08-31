using System;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// One side's clock, which a test moves rather than waits for.
/// </summary>
/// <remarks>
/// Each instance in the harness holds its own, so the two can be moved apart and skew is a
/// thing a case arranges rather than a thing it hopes for. Nothing here reads a real clock:
/// the instant is whatever was last set, which is the shape <c>ClockSourceTests</c> exists to
/// keep this plugin in.
/// <para>
/// It is a <see cref="TimeProvider"/> because that is what the controller at the edge takes,
/// so the clock a case moves is the clock the request is served against rather than a second
/// one beside it.
/// </para>
/// </remarks>
internal sealed class InstanceClock : TimeProvider
{
    private DateTimeOffset _now;

    /// <summary>
    /// Initializes a new instance of the <see cref="InstanceClock"/> class.
    /// </summary>
    /// <param name="start">The instant this side starts at.</param>
    public InstanceClock(DateTimeOffset start)
    {
        _now = start;
    }

    /// <summary>
    /// Gets the instant this side believes it is.
    /// </summary>
    public DateTimeOffset Now => _now;

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow() => _now;

    /// <summary>
    /// Moves this side forward.
    /// </summary>
    /// <param name="by">How far forward.</param>
    /// <exception cref="ArgumentOutOfRangeException">The span is negative.</exception>
    /// <remarks>
    /// Forward only. A clock that can be wound back would let a case arrange a state no
    /// server reaches by running, and the interesting backward case - two servers disagreeing
    /// about the time - is arranged by moving the other side forward instead.
    /// </remarks>
    public void Advance(TimeSpan by)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(by, TimeSpan.Zero);

        _now += by;
    }
}
