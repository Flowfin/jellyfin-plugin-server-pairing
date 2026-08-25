using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The key source of a server that holds no keys.
/// </summary>
/// <remarks>
/// This is a statement about this tree rather than a stand-in for one. There is no key store,
/// so no pairing has a key that verifies anything arriving, and this type answers exactly
/// that for every identifier it is asked about. Issue #30 is the store, and the registration
/// in <see cref="PluginServiceRegistrator"/> moves to it when it lands.
/// <para>
/// Answering the same way for every identifier is also the property
/// <see cref="IPairingKeySource"/> asks for: an implementation that answered faster for an
/// unknown pairing would undo the work <see cref="RequestAuthenticator"/> does to make the
/// two indistinguishable.
/// </para>
/// </remarks>
public sealed class NoPairingKeys : IPairingKeySource
{
    /// <inheritdoc />
    public ReadOnlyMemory<byte> ArrivingKey(string pairingId) => ReadOnlyMemory<byte>.Empty;
}
