using System;

namespace Jellyfin.Plugin.ServerPairing.Mapping;

/// <summary>
/// One user on this server, as the mapping table refers to them and as a dashboard shows them.
/// </summary>
/// <remarks>
/// Two strings and nothing else. The identifier is the one a mapping's
/// <see cref="UserMapping.LocalUserId"/> holds, formatted the way the host formats its own
/// claim for the same user, so a mapping and a user are matched by ordinal equality and never
/// by parsing either. The name is the host's current username for that user, read at the moment
/// of the listing and kept nowhere: it is a display value, and a user renamed on the host is
/// shown by the new name on the next read without anything here having to notice.
/// <para>
/// This is not the peer's display cache. That one is a copy of what another server said and
/// may be stale or empty; this one is read from the host that owns the user, so it is empty only
/// where the host no longer has the user at all, which is the case
/// <c>docs/mapping.md</c> says nothing refuses.
/// </para>
/// </remarks>
public sealed class LocalUser
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalUser"/> class.
    /// </summary>
    /// <param name="id">The identifier the host gives the user, formatted as thirty-two hex characters.</param>
    /// <param name="name">The username the host holds for them.</param>
    /// <exception cref="ArgumentException">The identifier is null, empty or blank.</exception>
    /// <exception cref="ArgumentNullException">The name is null.</exception>
    public LocalUser(string id, string name)
    {
        Id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("A local user needs an identifier.", nameof(id))
            : id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>
    /// Gets the identifier the host gives the user.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the username the host holds for them.
    /// </summary>
    public string Name { get; }
}
