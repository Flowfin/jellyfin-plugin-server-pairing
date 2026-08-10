namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What a transition produced for the caller that caused it.
/// </summary>
/// <remarks>
/// Three values, and they are the two refusal codes the error taxonomy in
/// <c>docs/protocol.md</c> lets a state decide between, plus acceptance. Which of the two
/// refusals a caller sees is the whole of what the taxonomy protects: the undistinguished one
/// says nothing at all, and the other is only ever handed to a caller that already verified.
/// </remarks>
public enum TransitionOutcome
{
    /// <summary>
    /// Refused with the undistinguished code. Every cause that produces this value produces
    /// the same bytes, so a caller learns nothing from it.
    /// </summary>
    Refused = 0,

    /// <summary>
    /// The message is accepted in this state and is answered.
    /// </summary>
    Answered = 1,

    /// <summary>
    /// Refused with the state code, which says the message is not accepted in this state. Only
    /// a caller that already holds a verifying key ever sees it.
    /// </summary>
    WrongState = 2,
}
