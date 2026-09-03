using System.Collections.Generic;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// The users this server has, read from the host that owns them.
/// </summary>
/// <remarks>
/// This exists so the administrative plane can say which local users are unmapped under a
/// pairing, which is the fourth thing issue #40 asks the surface to do: an operator wondering
/// why somebody is not syncing should not have to work it out by subtraction. Answering it is
/// a read of the host's user set, and this is the one place the plugin reads it.
/// <para>
/// IT IS READ AND NEVER WRITTEN, AND NOTHING DECIDES A MAPPING FROM IT. The rule that a mapping
/// is an administrator's decision rather than an inference is <see cref="UserMappings"/>'s and
/// is asserted over the plugin source, and a list of local users beside a list of peer users is
/// exactly the input a name-matching route would take. So this interface hands back names for
/// a dashboard to show and identifiers for a listing to match on, and the suite goes on
/// refusing any route from two lists to a correspondence.
/// </para>
/// <para>
/// It is an interface rather than the host's user manager taken directly, so the plane can be
/// proved without the host's entity types in the suite, and so the one place that reads the
/// host's type is the one place the host's type can change under this plugin.
/// </para>
/// </remarks>
public interface ILocalUsers
{
    /// <summary>
    /// Every user this server has, at the moment of the call.
    /// </summary>
    /// <returns>The users, in no particular order, empty where the host has none.</returns>
    IReadOnlyList<LocalUser> Users();
}
