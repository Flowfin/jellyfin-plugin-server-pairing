using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// What the diagnostics action answers an administrator with.
/// </summary>
/// <remarks>
/// This is the payload an operator pastes into a support thread, so what it may hold is decided
/// by what it must never hold. Issue #51 names those: key material, a fingerprint preimage, an
/// enrolment secret, and peer user identities. None of the four has a member here, and none can
/// acquire one without a member being added to this type in a diff somebody reads.
/// <para>
/// Every number below is an aggregate over the whole server. Nothing is per pairing, so nothing
/// here names one, and the payload has the same shape on a server that has never been paired as
/// on one that has.
/// </para>
/// <para>
/// WHAT IS NOT HERE IS THE LARGER HALF OF WHAT ISSUE #51 ASKS FOR, and it is absent because it
/// has no producer rather than because it was left out. The state of each pairing and when it
/// last succeeded needs a record store, of which this tree has no implementation. The version
/// this server speaks and the version a peer speaks needs a peer that has ever answered. The
/// last error per pairing needs a pairing. The matching counters issue #39 asks for need a
/// matcher that counts, and it is a pure function that counts nothing. Each of those is a
/// member added here when something produces it, and a member reporting zero for want of a
/// producer would read as a measurement and be none.
/// </para>
/// </remarks>
/// <param name="RefusalsByCode">
/// One number per member of the refusal taxonomy, under the same name the wire spells it with,
/// so a code an operator read in a log or in a peer's answer is the code they find here. The
/// whole taxonomy appears, including the codes no site in this tree produces, which report zero.
/// </param>
/// <param name="RefusalsByCause">
/// One number per cause this server can distinguish, which is what tells a peer sending too
/// fast from a scanner on the wrong path from a peer whose signature does not verify. Every
/// cause maps to exactly one of the codes above, and the codes above are the sums.
/// </param>
/// <param name="IdentifiersBeingCounted">
/// How many claimed pairing identifiers the arrival limit is holding a window for. It names
/// none of them. It is the bound that type carries rather than a count of pairings: an
/// identifier a stranger invented is counted here and is not a pairing.
/// </param>
public sealed record DiagnosticsAnswer(
    [property: JsonPropertyName("refusalsByCode")] IReadOnlyDictionary<string, long> RefusalsByCode,
    [property: JsonPropertyName("refusalsByCause")] IReadOnlyDictionary<string, long> RefusalsByCause,
    [property: JsonPropertyName("identifiersBeingCounted")] int IdentifiersBeingCounted)
{
    /// <summary>
    /// Reads one payload out of what this server is holding.
    /// </summary>
    /// <param name="refusals">The refusal counters.</param>
    /// <param name="arrivals">The arrival limit.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// The two maps are built by walking the enumerations rather than by naming members here,
    /// so a code or a cause added to either one appears in the payload without this method
    /// moving. That is what makes issue #51's third condition a property of the type rather
    /// than of a list somebody remembered to extend.
    /// </remarks>
    public static DiagnosticsAnswer Of(RefusalCounters refusals, ArrivalLimit arrivals)
    {
        System.ArgumentNullException.ThrowIfNull(refusals);
        System.ArgumentNullException.ThrowIfNull(arrivals);

        var byCode = new Dictionary<string, long>(System.StringComparer.Ordinal);

        foreach (var code in RefusalCounters.Codes())
        {
            byCode[Refusal.Wire(code)] = refusals.CountedFor(code);
        }

        var byCause = new Dictionary<string, long>(System.StringComparer.Ordinal);

        foreach (var cause in RefusalCounters.Causes())
        {
            byCause[RefusalCounters.Name(cause)] = refusals.Counted(cause);
        }

        return new DiagnosticsAnswer(byCode, byCause, arrivals.Counting());
    }
}
