using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.ServerPairing.Api;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.Tests.Harness;

/// <summary>
/// What one side wrote to its log, kept so a case can assert on it.
/// </summary>
/// <remarks>
/// Enabled at every level including <see cref="LogLevel.Debug"/>, deliberately. The assertion
/// issue #13 asks for is an absence - that no secret a lifecycle created appears in the log -
/// and a capture that filtered by level would satisfy it by not looking.
/// <para>
/// WHAT THIS CAPTURES TODAY IS THE EDGE AND NOT A LIFECYCLE. The only site on the peer path
/// that writes anything is the controller's fault handler, so a run that faults nowhere leaves
/// this empty. That is why <c>PairedInstancesTests</c> asserts on a capture only where it also
/// arranges the fault: a case that cannot tell an empty log from a log with no secret in it is
/// the one #13 names as the shape to avoid, and it is not written here.
/// </para>
/// </remarks>
internal sealed class CapturedLog : ILogger<PeerPlaneController>
{
    private readonly List<string> _lines = new List<string>();

    /// <summary>
    /// Gets what was written, oldest first, each line carrying its level, its message and the
    /// message of any exception it was given.
    /// </summary>
    public IReadOnlyList<string> Lines => _lines;

    /// <inheritdoc />
    IDisposable? ILogger.BeginScope<TState>(TState state) => null;

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        _lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"{logLevel} {formatter(state, exception)} {exception?.Message}"));
    }
}
