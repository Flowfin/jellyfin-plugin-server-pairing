namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// Selecting the one protocol version a pairing runs at.
/// </summary>
/// <remarks>
/// The rule is <c>docs/protocol.md</c>: a <c>hello</c> carries the sender's range, the receiver
/// selects the highest version inside both ranges, and that version is fixed for the life of
/// the pairing. Where the ranges do not overlap there is no fallback, because a message a
/// server does not understand is one it cannot make a security decision about.
/// <para>
/// The highest rather than the lowest, and it is worth saying why in the place that does it. A
/// pairing that settled on the lowest version both sides speak would hold two upgraded servers
/// at the oldest wire either of them had ever supported, for as long as the pairing lasted,
/// and nothing about that would be visible to either operator.
/// </para>
/// <para>
/// Selecting is a decision about the whole pairing rather than about one message, so it
/// happens once, on the <c>hello</c>. A request arriving on an <c>Active</c> pairing carrying
/// a version other than the selected one is not renegotiated: the transition table refuses it
/// and the taxonomy answers <c>state</c>. Nothing here does that, because nothing here holds a
/// pairing record.
/// </para>
/// <para>
/// Neither overload reads a clock, a configuration file or a peer. What this server speaks is
/// <see cref="SupportedVersions"/> and what the peer speaks is an argument, which is what
/// makes the rule testable over ranges no build ships while the set has one member in it.
/// </para>
/// </remarks>
public static class VersionNegotiation
{
    /// <summary>
    /// Selects the version for a pairing between this build and a peer offering a range.
    /// </summary>
    /// <remarks>
    /// THIS WAS THE ONE READER OF <see cref="SupportedVersions.Range"/> IN THE PLUGIN AND IS
    /// ONE OF THREE. The others are <see cref="Api.Refusal.Body(Api.RefusalCode)"/> and the
    /// diagnostics payload an administrator reads, which is what issue #25's fourth condition
    /// asks for; the suite holds the three against one another rather than each against the
    /// field. What that is worth as proof is bounded and the bound is stated where the set is
    /// declared: this
    /// build's range has one member, so a selection through it cannot be told apart from a
    /// constant. The overload below is where the rule itself is exercised.
    /// </remarks>
    /// <param name="offered">The range the peer's <c>hello</c> carried.</param>
    /// <returns>The version selected, or the refusal that none was.</returns>
    public static VersionSelection Select(VersionRange offered) =>
        Select(SupportedVersions.Range, offered);

    /// <summary>
    /// Selects the version for a pairing between two ranges.
    /// </summary>
    /// <param name="local">The range this side speaks.</param>
    /// <param name="offered">The range the other side offered.</param>
    /// <returns>The version selected, or the refusal that none was.</returns>
    public static VersionSelection Select(VersionRange local, VersionRange offered)
    {
        var highest = local.High < offered.High ? local.High : offered.High;

        if (!local.Includes(highest) || !offered.Includes(highest))
        {
            return VersionSelection.None();
        }

        return VersionSelection.Of(highest);
    }
}
