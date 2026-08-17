namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The version selected for a pairing, or the refusal that no version was.
/// </summary>
/// <remarks>
/// <see cref="Version"/> carries a number only where <see cref="Outcome"/> is
/// <see cref="VersionOutcome.Selected"/>. It is zero otherwise rather than being the nearest
/// version to what was offered, because a caller that reads the number without reading the
/// outcome should get a version this protocol does not have rather than a plausible one.
/// </remarks>
public sealed class VersionSelection
{
    private VersionSelection(VersionOutcome outcome, int version)
    {
        Outcome = outcome;
        Version = version;
    }

    /// <summary>
    /// Gets what was decided.
    /// </summary>
    public VersionOutcome Outcome { get; }

    /// <summary>
    /// Gets the selected version, where one was selected, and zero otherwise.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// A selection that found no version in common.
    /// </summary>
    /// <returns>The refusal.</returns>
    public static VersionSelection None() =>
        new VersionSelection(VersionOutcome.NoVersionInCommon, 0);

    /// <summary>
    /// A selection that settled on a version.
    /// </summary>
    /// <param name="version">The version selected.</param>
    /// <returns>The selection.</returns>
    public static VersionSelection Of(int version) =>
        new VersionSelection(VersionOutcome.Selected, version);
}
