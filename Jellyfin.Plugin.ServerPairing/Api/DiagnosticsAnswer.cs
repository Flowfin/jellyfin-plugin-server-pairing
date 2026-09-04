using System.Collections.Generic;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.ServerPairing.Protocol;

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
/// Nothing here is per pairing, so nothing here names one, and the payload has the same shape
/// on a server that has never been paired as on one that has. The counters are aggregates over
/// the whole server; the version range is a constant of the build rather than a measurement of
/// anything, and it is here because an operator comparing two servers has to be able to read
/// each one's without a token and a terminal.
/// </para>
/// <para>
/// WHAT IS NOT HERE IS THE LARGER HALF OF WHAT ISSUE #51 ASKS FOR, and it is absent because it
/// has no producer rather than because it was left out. The state of each pairing and when it
/// last succeeded needs a record store. THIS SENTENCE SAID THIS TREE HAS NO IMPLEMENTATION OF
/// ONE, and <see cref="Protocol.FilePairingRecordStore"/> ships and is registered; what has no
/// implementation is the reading, because nothing on either plane resolves that store. The
/// member is still absent for want of a producer, and the producer it wants is a join rather
/// than a store somebody has yet to write, which is the half a reader of the old sentence would
/// have gone looking for. THE TWO PROTOCOL VERSIONS WERE ONE ABSENCE IN THIS SENTENCE AND ARE
/// TWO DIFFERENT ONES. What a peer speaks needs a peer that has ever answered, and there is
/// none. What THIS server speaks needs nothing to have happened at all: it is
/// <see cref="SupportedVersions"/>, it is decided at build time, and it is the member below
/// rather than an absence. Issue #25 asks for that set to be defined once and read by the
/// negotiation, by the refusal and by the dashboard, and this is the third of those readers.
/// The last error per pairing needs a pairing. The matching counters issue #39 asks for need a
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
/// <param name="VersionLow">
/// The lowest version of the pairing protocol this build speaks.
/// </param>
/// <param name="VersionHigh">
/// The highest version of the pairing protocol this build speaks. It is the same number as the
/// low endpoint on a build shipping one version, which is why the pair is handed in as a range
/// rather than read here: two members that cannot be told apart by their values are two members
/// a case cannot show in the right order.
/// </param>
public sealed record DiagnosticsAnswer(
    [property: JsonPropertyName("refusalsByCode")] IReadOnlyDictionary<string, long> RefusalsByCode,
    [property: JsonPropertyName("refusalsByCause")] IReadOnlyDictionary<string, long> RefusalsByCause,
    [property: JsonPropertyName("identifiersBeingCounted")] int IdentifiersBeingCounted,
    [property: JsonPropertyName("versionLow")] int VersionLow,
    [property: JsonPropertyName("versionHigh")] int VersionHigh)
{
    /// <summary>
    /// Reads one payload out of what this server is holding.
    /// </summary>
    /// <param name="refusals">The refusal counters.</param>
    /// <param name="arrivals">The arrival limit.</param>
    /// <param name="supported">The protocol versions this build speaks.</param>
    /// <returns>The payload.</returns>
    /// <remarks>
    /// The two maps are built by walking the enumerations rather than by naming members here,
    /// so a code or a cause added to either one appears in the payload without this method
    /// moving. That is what makes issue #51's third condition a property of the type rather
    /// than of a list somebody remembered to extend.
    /// <para>
    /// The range is TAKEN rather than read, for the reason
    /// <see cref="Refusal.VersionBody(VersionRange)"/> takes one: this build's lowest and
    /// highest version are the same number, so a payload that wrote the high endpoint into both
    /// members would be byte-identical to a correct one and every case driving the constants
    /// would pass with the two swapped. The one caller that answers a request hands it
    /// <see cref="SupportedVersions.Range"/>, so what an operator reads is the declared set and
    /// not a second copy of it.
    /// </para>
    /// </remarks>
    public static DiagnosticsAnswer Of(RefusalCounters refusals, ArrivalLimit arrivals, VersionRange supported)
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

        return new DiagnosticsAnswer(byCode, byCause, arrivals.Counting(), supported.Low, supported.High);
    }
}
