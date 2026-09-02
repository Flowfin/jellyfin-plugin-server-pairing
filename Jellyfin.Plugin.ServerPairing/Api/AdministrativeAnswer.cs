using System;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The shape an action on the administrative plane answers a problem with.
/// </summary>
/// <remarks>
/// Built as bytes here rather than serialised from a type, for the reason
/// <see cref="Refusal"/> gives on the other plane: a serialiser is free to reorder members and
/// to be configured differently by a host, and the body is one member holding one string, so
/// there is nothing a serialiser would buy. THIS SENTENCE SAID ONE MEMBER AND ONE VALUE: the
/// member is still one and the values it may hold are now two, because a second store can be
/// the unreadable one. Nothing about the shape moved with the second value, which is why the
/// argument for building it as bytes is unchanged.
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
        AdministrativeProblem.RecordStoreUnreadable => "record-store-unreadable",
        _ => throw new ArgumentOutOfRangeException(nameof(problem)),
    };

    /// <summary>
    /// The whole body of a named problem.
    /// </summary>
    /// <param name="problem">The problem the body names.</param>
    /// <returns>The body.</returns>
    public static string Body(AdministrativeProblem problem) => "{\"problem\":\"" + Wire(problem) + "\"}";
}
