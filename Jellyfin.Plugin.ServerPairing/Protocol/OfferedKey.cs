namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Whether the public key on an arriving hello is the one already recorded for this pairing.
/// </summary>
/// <remarks>
/// The transition table gives hello two different answers in the half-built states, and the
/// thing that separates them is this comparison. It arrives as an input rather than being made
/// here: comparing two keys is the ceremony's work, in issue #19, and the state machine is
/// about what each answer means. THIS NAMED #18 BESIDE IT, and #18 is the window: it decides
/// when a hello may arrive at all, not whether the key on one is the recorded one.
/// </remarks>
public enum OfferedKey
{
    /// <summary>
    /// No key was offered, which is every message that is not a hello, and a hello reaching a
    /// state that has no recorded key to compare against.
    /// </summary>
    NotApplicable = 0,

    /// <summary>
    /// The offered key is the one already recorded. The peer is retrying, which the network
    /// makes ordinary.
    /// </summary>
    Identical = 1,

    /// <summary>
    /// The offered key differs from the one recorded. This closes the window and destroys the
    /// half-built pairing, which is the single-use half of issue #18.
    /// </summary>
    Different = 2,
}
