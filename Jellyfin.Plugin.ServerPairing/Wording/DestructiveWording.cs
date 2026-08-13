namespace Jellyfin.Plugin.ServerPairing.Wording;

/// <summary>
/// What an operator reads before something is destroyed.
/// </summary>
/// <remarks>
/// Each sentence names what goes and says whether it can be undone, because the answer is
/// no in every case here and an operator is entitled to know that before pressing rather
/// than after.
/// </remarks>
public static class DestructiveWording
{
    /// <summary>
    /// Revoking, which is issue #24: unilateral, immediate and terminal.
    /// </summary>
    public const string Revoke =
        "Revoking ends this pairing here and now and destroys the key that verified it. It " +
        "does not wait for the other server and works when that server is unreachable. It " +
        "cannot be undone: pairing these two servers again means a fresh enrolment and a " +
        "fresh comparison.";

    /// <summary>
    /// Unpairing, which is issue #56: revoking here plus asking the peer to do the same.
    /// </summary>
    public const string Unpair =
        "Unpairing asks the other server to end the pairing as well, then ends it here. If " +
        "that server refuses or cannot be reached this side still ends, and what this " +
        "server already sent is on the other server for its operator to remove. It cannot " +
        "be undone.";

    /// <summary>
    /// Closing an open enrolment window before anything has used it.
    /// </summary>
    public const string CloseWindow =
        "Closing the window stops this server answering that address. Nothing from an " +
        "enrolment that did not finish is kept. Opening another window later is starting " +
        "again rather than undoing this.";

    /// <summary>
    /// Removing a mapping, which stops what has not moved and reaches nothing that has.
    /// </summary>
    public const string RemoveMapping =
        "Removing this mapping stops anything further moving for that user. What already " +
        "arrived under it stays on the user it arrived on, and removing that is done " +
        "wherever it was stored. This cannot be undone from here.";

    /// <summary>
    /// Changing a mapping, which reads like a repair and is not one. The consequence is
    /// argued in <c>docs/data.md</c>, which names issue #54 as owing the sentence.
    /// </summary>
    public const string ChangeMapping =
        "Changing this mapping changes where the next transfer goes and nothing else. " +
        "Everything that arrived under the old mapping stays on the user it arrived on. " +
        "Setting the old mapping back later does not undo that.";

    /// <summary>
    /// Removing everything held about one user, which is issue #60.
    /// </summary>
    public const string RemoveUser =
        "Removing this user removes every mapping held for them here and asks each paired " +
        "plugin to delete what it stored for them. What the other server holds is that " +
        "operator's to remove. It cannot be undone.";
}
