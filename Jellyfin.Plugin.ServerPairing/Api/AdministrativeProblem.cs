namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The problems an action on the administrative plane may name.
/// </summary>
/// <remarks>
/// This is the opposite of <see cref="RefusalCode"/> rather than a second copy of it. On the
/// peer plane every cause collapses into one code, because the caller is a stranger and what
/// they learn from an answer has to be nothing. Here the caller has already passed the host's
/// elevation policy, so telling them nothing costs an administrator the reason and buys
/// nobody anything.
/// <para>
/// One member, because one action exists. The vocabulary grows with the actions rather than
/// being written ahead of them: a member nothing produces is a problem no operator can meet
/// and no case can drive.
/// </para>
/// </remarks>
public enum AdministrativeProblem
{
    /// <summary>
    /// The key store could not be read, so what this server holds is unknown. What makes a
    /// store unreadable, and what an operator does about a restored or corrupt one, is issue
    /// #33 and is not decided here; this member is only the answer an administrator gets
    /// instead of a fault.
    /// </summary>
    KeyStoreUnreadable = 0,
}
