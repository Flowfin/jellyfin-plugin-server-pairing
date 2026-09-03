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
/// The vocabulary grows with the actions rather than being written ahead of them: a member
/// nothing produces is a problem no operator can meet and no case can drive. THIS PARAGRAPH SAID
/// THERE IS ONE MEMBER BECAUSE ONE ACTION EXISTS, and then that there are two. There are four.
/// The second arrived with the read of the open enrolment windows, and the third and fourth with
/// the report of what is held about one user, which reads a third store and writes an audit
/// entry that has to name somebody.
/// </para>
/// <para>
/// The three stores get a member each rather than sharing one. They are three files answering
/// three questions, which is why the plugin writes them separately: a key store that will not
/// read is not a reason an operator cannot be told what state a pairing is in, and an answer
/// naming one store when another is the broken one sends them to the wrong file.
/// </para>
/// </remarks>
public enum AdministrativeProblem
{
    /// <summary>
    /// The key store could not be read, so what this server holds is unknown. What makes a
    /// store unreadable is decided where the store is: a file that is there and is not a key
    /// store is refused, which is
    /// <see cref="Jellyfin.Plugin.ServerPairing.KeyStore.StoreDamagedException"/>, and a file
    /// in a newer format is refused separately. What an operator does about a RESTORED or
    /// COPIED store is issue #33 and is not decided here. This member is only the answer an
    /// administrator gets instead of a fault, and it is the same answer for every reason the
    /// store would not read.
    /// </summary>
    KeyStoreUnreadable = 0,

    /// <summary>
    /// The pairing record store could not be read, so whether a window is open, and what state
    /// any pairing is in, is unknown. What makes that store unreadable is decided where the
    /// store is: a file that is there and is not a record store is refused as damaged, and a
    /// file declaring a format this build has no rung for is refused separately. This member is
    /// only the answer an administrator gets instead of a fault, and it is the same answer for
    /// every reason the store would not read.
    /// </summary>
    RecordStoreUnreadable = 1,

    /// <summary>
    /// The mapping store could not be read, so what is held about a user is unknown. What makes
    /// that store unreadable is decided where the store is, in the same two ways as the record
    /// store: a file that is there and is not a mapping store is refused as damaged, and a file
    /// declaring a format this build has no rung for is refused separately. This member is only
    /// the answer an administrator gets instead of a fault, and it is the same answer for every
    /// reason the store would not read.
    /// </summary>
    MappingStoreUnreadable = 2,

    /// <summary>
    /// The principal the host authenticated carries no identifier this plugin can write an
    /// audit entry under, so an action that has to name who asked refuses rather than record
    /// the act under nobody. On a host behaving as <c>docs/endpoints.md</c> reads it, every
    /// request that passes the elevation policy carries the identifier, so what this names is
    /// the host having changed under the plugin rather than a case an operator will meet. It is
    /// the one problem on this plane that is not about a store, and it exists so that a report
    /// of what is held about a person is never made without a record of who asked.
    /// </summary>
    AdministratorUnidentified = 3,
}
