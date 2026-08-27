using System;

namespace Jellyfin.Plugin.ServerPairing.Configuration;

/// <summary>
/// One setting whose value was refused, and the rule that refused it.
/// </summary>
/// <remarks>
/// The setting is carried separately from the sentence because the name is the part an
/// operator needs and the part a clamp destroys. A value silently corrected to something
/// inside its range leaves nothing to name, so the operator keeps the value they typed, the
/// server keeps a value nobody asked for, and neither of them ever meets the other.
/// </remarks>
public sealed class SettingRefusal
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingRefusal"/> class.
    /// </summary>
    /// <param name="setting">The name of the setting, spelled as it is on the configuration.</param>
    /// <param name="reason">Why the value was refused, in words an operator can act on.</param>
    /// <exception cref="ArgumentException">Either argument is null or empty.</exception>
    public SettingRefusal(string setting, string reason)
    {
        ArgumentException.ThrowIfNullOrEmpty(setting);
        ArgumentException.ThrowIfNullOrEmpty(reason);

        Setting = setting;
        Reason = reason;
    }

    /// <summary>
    /// Gets the name of the setting that was refused, as it is spelled on the configuration
    /// object and therefore in the configuration file and on the dashboard page.
    /// </summary>
    public string Setting { get; }

    /// <summary>
    /// Gets why the value was refused.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the whole refusal as one line, which is what goes to the log and to the operator.
    /// </summary>
    public string Message => "The setting '" + Setting + "' was refused. " + Reason;
}
