using System;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The answer the administrative plane gives when it refuses to open an enrolment window.
/// </summary>
/// <remarks>
/// The shape follows <see cref="AdministrativeAnswer"/>: one word under one member, so an
/// operator can search a support thread for it, and a status that is not the one a named
/// problem carries, so a page reading the status alone can tell a request this server declined
/// from a store it could not read.
/// </remarks>
public static class OpeningAnswer
{
    /// <summary>
    /// The status every refusal to open a window carries.
    /// </summary>
    /// <remarks>
    /// A conflict, because every refusal here is the request meeting a state this server
    /// already holds: a pairing, an open window, a refused setting or an empty address. Nothing
    /// about the request was malformed and nothing on this server failed, which is what keeps it
    /// apart from <see cref="AdministrativeAnswer.ProblemStatus"/>.
    /// </remarks>
    public const int RefusedStatus = 409;

    /// <summary>
    /// The word a refusal is carried under.
    /// </summary>
    /// <param name="refusal">The refusal.</param>
    /// <returns>Its spelling on the wire.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The refusal has no spelling.</exception>
    public static string Wire(OpeningRefusal refusal) => refusal switch
    {
        OpeningRefusal.AlreadyPaired => "already-paired",
        OpeningRefusal.AlreadyOpen => "already-open",
        OpeningRefusal.ConfigurationRefused => "configuration-refused",
        OpeningRefusal.NoPeerAddress => "no-peer-address",
        _ => throw new ArgumentOutOfRangeException(nameof(refusal)),
    };

    /// <summary>
    /// The body a refusal is answered with.
    /// </summary>
    /// <param name="refusal">The refusal.</param>
    /// <returns>A JSON object with one member naming it.</returns>
    public static string Body(OpeningRefusal refusal) => "{\"refused\":\"" + Wire(refusal) + "\"}";

    /// <summary>
    /// The refusal a window's own answer is carried to the wire as.
    /// </summary>
    /// <param name="opening">What the window answered.</param>
    /// <returns>The refusal.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The window opened, which is no refusal.</exception>
    /// <remarks>
    /// Total over the window's refusals and nothing else. An arm mapping an opening the window
    /// did not refuse to a refusal would answer a window that is open as one that is not, which
    /// is why <see cref="WindowOpening.Opened"/> throws here rather than mapping to a default.
    /// </remarks>
    public static OpeningRefusal RefusalFor(WindowOpening opening) => opening switch
    {
        WindowOpening.AlreadyPaired => OpeningRefusal.AlreadyPaired,
        WindowOpening.AlreadyOpen => OpeningRefusal.AlreadyOpen,
        _ => throw new ArgumentOutOfRangeException(nameof(opening), opening, "A window that opened was not refused."),
    };
}
