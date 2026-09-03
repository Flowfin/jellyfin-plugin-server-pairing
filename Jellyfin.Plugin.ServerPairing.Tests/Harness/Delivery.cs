using System;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// One message reaching one side's endpoint, and what that side answered.
/// </summary>
/// <remarks>
/// A record of what ARRIVED, so a case can tell a message that never crossed from one that
/// crossed and was refused. Those two are the same thing to a sender, which is the property
/// the refusal shape exists for and the reason a case cannot read either from the answer.
/// </remarks>
/// <param name="Message">Which of the six messages arrived.</param>
/// <param name="ServedAt">The instant the receiving side judged it at.</param>
/// <param name="StatusCode">The status it answered with.</param>
/// <param name="Body">The answer body.</param>
internal readonly record struct Delivery(
    PairingMessage Message,
    DateTimeOffset ServedAt,
    int StatusCode,
    string Body);
