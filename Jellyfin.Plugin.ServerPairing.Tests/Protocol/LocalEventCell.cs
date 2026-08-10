using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// One cell of the local events table in <c>docs/protocol.md</c>, which covers what happens on
/// this side without any message arriving.
/// </summary>
internal sealed class LocalEventCell
{
    /// <summary>
    /// Gets the label a failure prints.
    /// </summary>
    public required string Cell { get; init; }

    /// <summary>
    /// Gets the state on this side.
    /// </summary>
    public required PairingState From { get; init; }

    /// <summary>
    /// Gets what happened here.
    /// </summary>
    public required LocalEvent Event { get; init; }

    /// <summary>
    /// Gets the state the document says the pairing is in afterwards.
    /// </summary>
    public required PairingState To { get; init; }

    /// <summary>
    /// Gets whether the document accepts the event in this state.
    /// </summary>
    public required TransitionOutcome Outcome { get; init; }
}
