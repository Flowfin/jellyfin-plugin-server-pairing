using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Supplies the keys that authenticate requests arriving from one pairing.
/// </summary>
/// <remarks>
/// The keys are selected by the pairing identifier in the request and by nothing else, so an
/// implementation answers about the identifier it was given and never about the caller.
/// <para>
/// Every read takes the instant it is judged at, because a superseded key stops verifying at a
/// moment and an implementation that answered without one could not express that. The instant
/// is the same one the rest of the plane is judged against: it is read once, at the edge that
/// serves the request, and handed down. Nothing here reads a clock, which is the rule
/// <c>ClockSourceTests</c> refuses a second site against.
/// </para>
/// </remarks>
public interface IPairingKeySource
{
    /// <summary>
    /// The keys that verify requests arriving from the named pairing.
    /// </summary>
    /// <param name="pairingId">The pairing identifier from the request.</param>
    /// <param name="at">The instant the answer is judged at.</param>
    /// <returns>
    /// The keys, or <see cref="AcceptedKeys.None"/> where no pairing with that identifier is
    /// known. An implementation answers in the same shape in both cases, or the caller's own
    /// work to hide the difference is undone by the lookup in front of it.
    /// </returns>
    AcceptedKeys ArrivingKeys(string pairingId, DateTimeOffset at);
}
