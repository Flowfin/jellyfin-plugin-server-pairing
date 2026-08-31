using System;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Protocol;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The one shape every refusal on the pairing plane takes, and the one exception to it.
/// </summary>
/// <remarks>
/// <c>docs/protocol.md</c> fixes it: HTTP 403 with a body of exactly one JSON object carrying
/// exactly one member. The bytes are built here rather than serialised from a type, because a
/// serialiser is free to reorder members, to write a different escape for the same character
/// and to be configured differently by a host, and the shape never varying is the whole
/// property.
/// <para>
/// <see cref="RefusalCode.Version"/> IS THE ONE EXCEPTION AND IT IS STATED RATHER THAN
/// SMUGGLED. Its body carries the range this build speaks beside the code, because a refusal
/// that says only "no version in common" leaves the operator on the other side with nothing to
/// act on and turns every mismatch into a support conversation. What it discloses is nothing a
/// probe could not have: the range is what a <c>hello</c> response advertises anyway, and the
/// document states it. Putting it in the <c>hello</c> response instead fails the case that
/// matters, which is a peer outside the overlap, because such a peer never gets one. That
/// decision is issue #25's and the taxonomy carries it as a named exception rather than making
/// every refusal body variable-length.
/// </para>
/// <para>
/// <see cref="Body(RefusalCode)"/> reads the range from <see cref="SupportedVersions"/> rather
/// than taking it, so every route that refuses produces the one correct version refusal for
/// this build and the refusal, the negotiation and the document read one list. The shape
/// function beside it takes a range, and it takes one for a reason worth stating: this build's
/// lowest and highest version are the same number, so a case driving only the constants cannot
/// tell the two members apart and would pass with them swapped.
/// </para>
/// </remarks>
public static class Refusal
{
    /// <summary>
    /// The HTTP status every refusal on this plane carries.
    /// </summary>
    public const int Status = 403;

    /// <summary>
    /// The wire spelling of a refusal code.
    /// </summary>
    /// <param name="code">The code.</param>
    /// <returns>The value the <c>code</c> member carries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The code is not one of the defined values.</exception>
    public static string Wire(RefusalCode code) => code switch
    {
        RefusalCode.Refused => "refused",
        RefusalCode.Clock => "clock",
        RefusalCode.Version => "version",
        RefusalCode.State => "state",
        RefusalCode.Malformed => "malformed",
        RefusalCode.Replay => "replay",
        RefusalCode.Busy => "busy",
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    /// <summary>
    /// The whole body of a refusal, as the bytes that go on the wire.
    /// </summary>
    /// <param name="code">The code the body carries.</param>
    /// <returns>The body bytes.</returns>
    public static string Body(RefusalCode code) => code == RefusalCode.Version
        ? VersionBody(SupportedVersions.Range)
        : "{\"code\":\"" + Wire(code) + "\"}";

    /// <summary>
    /// The body of a version refusal naming a range, as the bytes that go on the wire.
    /// </summary>
    /// <param name="supported">The range to name.</param>
    /// <returns>The body bytes.</returns>
    /// <remarks>
    /// Every route that refuses goes through <see cref="Body(RefusalCode)"/>, which hands this
    /// the range out of <see cref="SupportedVersions"/>, so nothing composes a version refusal
    /// naming some other range. It takes the range rather than reading it so that the two
    /// members can be told apart while this build's lowest and highest version are the same
    /// number, which is the only way a case can show them in the right order rather than
    /// assuming it.
    /// <para>
    /// The member names are the ones a <c>hello</c> request uses for the same two numbers, so a
    /// peer reads one spelling of a version range rather than two.
    /// </para>
    /// </remarks>
    public static string VersionBody(VersionRange supported) =>
        "{\"code\":\"" + Wire(RefusalCode.Version)
        + "\",\"versionLow\":" + supported.Low.ToString(CultureInfo.InvariantCulture)
        + ",\"versionHigh\":" + supported.High.ToString(CultureInfo.InvariantCulture) + "}";
}
