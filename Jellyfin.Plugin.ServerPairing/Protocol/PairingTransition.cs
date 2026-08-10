namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// One cell of the transition table: what the pairing's state becomes, and what the caller is
/// told.
/// </summary>
/// <remarks>
/// The two halves are independent and one cell needs them to be. A second hello carrying a
/// different key is refused and moves the pairing to <see cref="PairingState.Absent"/> in the
/// same step, so a reader that assumes a refusal leaves the state alone has read the table
/// wrongly.
/// </remarks>
/// <param name="To">The state the pairing is in after the transition.</param>
/// <param name="Outcome">What the caller that caused it is told.</param>
public readonly record struct PairingTransition(PairingState To, TransitionOutcome Outcome)
{
    /// <summary>
    /// Whether this transition moves the pairing out of the state it was in.
    /// </summary>
    /// <param name="from">The state it was in.</param>
    /// <returns>True where the transition changes the state.</returns>
    public bool Moves(PairingState from) => To != from;
}
