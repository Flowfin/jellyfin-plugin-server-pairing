using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The keys that verify a request arriving on one pairing, as they stand at one instant.
/// </summary>
/// <remarks>
/// Two rather than one, because a rotation leaves the peer signing with a key this side has
/// just replaced. A source that answered with the current key alone would make every rotation
/// break traffic for the length of the overlap, and break it only there, which is the window
/// nobody exercises by hand.
/// <para>
/// A slot nobody holds is an empty memory rather than a null, so a caller that judges both
/// slots does the same work whether or not a rotation is open. What stands in for an empty
/// slot is <see cref="RequestAuthenticator"/>'s business rather than this type's: a key drawn
/// once per receiver, never an empty span, because an empty key is one any caller can sign
/// under.
/// </para>
/// <para>
/// The bytes are a copy. <see cref="IPairingKeySource"/> has answered in
/// <see cref="ReadOnlyMemory{T}"/> since it was written and this type does not re-decide that,
/// so a store holding its key material in a type that hands out no copies produces one here.
/// That is a residual rather than a property: it is named in <c>docs/keystore.md</c> beside
/// the other things managed memory does not let this plugin promise.
/// </para>
/// </remarks>
public sealed class AcceptedKeys
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AcceptedKeys"/> class.
    /// </summary>
    /// <param name="current">The key this pairing signs with and accepts.</param>
    /// <param name="superseded">
    /// The key a rotation replaced while its overlap is open, or an empty memory where there
    /// is none.
    /// </param>
    public AcceptedKeys(ReadOnlyMemory<byte> current, ReadOnlyMemory<byte> superseded)
    {
        Current = current;
        Superseded = superseded;
    }

    /// <summary>
    /// Gets what a pairing this server does not hold answers with, which is neither key.
    /// </summary>
    /// <remarks>
    /// Built on each read rather than held in a static field. A cached instance would be the
    /// one static this assembly is allowed, which is the plugin instance the host sets, and
    /// <c>StaticStateTests</c> refuses a second by name rather than by judgement about whether
    /// it is safe.
    /// </remarks>
    public static AcceptedKeys None => new AcceptedKeys(default, default);

    /// <summary>
    /// Gets the key this pairing signs with and accepts.
    /// </summary>
    public ReadOnlyMemory<byte> Current { get; }

    /// <summary>
    /// Gets the key a rotation replaced, while its overlap is open.
    /// </summary>
    public ReadOnlyMemory<byte> Superseded { get; }
}
