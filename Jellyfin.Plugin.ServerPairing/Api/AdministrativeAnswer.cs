using System;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The shape an action on the administrative plane answers a problem with.
/// </summary>
/// <remarks>
/// Built as bytes here rather than serialised from a type, for the reason
/// <see cref="Refusal"/> gives on the other plane: a serialiser is free to reorder members and
/// to be configured differently by a host, and there is one member and one value, so there is
/// nothing a serialiser would buy.
/// <para>
/// The status is 503 rather than 500. What this answers is a server that cannot read what it
/// holds, which is a state an operator repairs and then retries, and a 500 says the request
/// was wrong to make. Nothing here retries on the operator's behalf.
/// </para>
/// </remarks>
public static class AdministrativeAnswer
{
    /// <summary>
    /// The HTTP status a named problem carries.
    /// </summary>
    public const int ProblemStatus = 503;

    /// <summary>
    /// The wire spelling of a problem.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The value the <c>problem</c> member carries.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The problem is not one of the defined values.</exception>
    public static string Wire(AdministrativeProblem problem) => problem switch
    {
        AdministrativeProblem.KeyStoreUnreadable => "key-store-unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(problem)),
    };

    /// <summary>
    /// The whole body of a named problem.
    /// </summary>
    /// <param name="problem">The problem the body names.</param>
    /// <returns>The body.</returns>
    public static string Body(AdministrativeProblem problem) => "{\"problem\":\"" + Wire(problem) + "\"}";
}
