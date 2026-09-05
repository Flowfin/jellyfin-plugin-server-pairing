namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The two kinds of value a body member may carry.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> gives every named member a JSON type, and the set it uses is these
/// two. There is no boolean member, no null member and no nested member, so a value of any
/// other kind is refused by <see cref="BodyObject"/> rather than being carried as a third kind
/// nothing reads.
/// </remarks>
public enum BodyValueKind
{
    /// <summary>
    /// A JSON string. Every member whose limit is a length or an alphabet is one of these.
    /// </summary>
    Text = 0,

    /// <summary>
    /// A JSON number. The two version members are these, because the one refusal body that
    /// carries a range writes them as numbers and a peer reads one spelling of a range rather
    /// than two.
    /// </summary>
    Number = 1,
}
