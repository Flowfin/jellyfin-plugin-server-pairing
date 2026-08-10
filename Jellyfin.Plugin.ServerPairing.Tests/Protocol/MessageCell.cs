using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Protocol;

/// <summary>
/// One cell of the transition table in <c>docs/protocol.md</c>. Rows of that table are the
/// state on the receiving side and columns are the message that arrived.
/// </summary>
internal sealed class MessageCell
{
    /// <summary>
    /// Gets the label a failure prints. It names the row and the column rather than the
    /// values, so a red case says which cell of the document disagrees.
    /// </summary>
    public required string Cell { get; init; }

    /// <summary>
    /// Gets the state on the receiving side.
    /// </summary>
    public required PairingState From { get; init; }

    /// <summary>
    /// Gets the message that arrived.
    /// </summary>
    public required PairingMessage Message { get; init; }

    /// <summary>
    /// Gets whether a hello's key is the one already recorded. Not applicable to every other
    /// message and to a hello reaching a state that holds no recorded key.
    /// </summary>
    public OfferedKey Offered { get; init; } = OfferedKey.NotApplicable;

    /// <summary>
    /// Gets the state the document says the pairing is in afterwards.
    /// </summary>
    public required PairingState To { get; init; }

    /// <summary>
    /// Gets what the document says the caller is told.
    /// </summary>
    public required TransitionOutcome Outcome { get; init; }
}
