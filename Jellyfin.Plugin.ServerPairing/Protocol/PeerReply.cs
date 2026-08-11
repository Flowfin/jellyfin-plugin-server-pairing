using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// One answer from a peer, as much of it as the caller was willing to read.
/// </summary>
public sealed class PeerReply
{
    private PeerReply(PeerReplyOutcome outcome, int statusCode, ReadOnlyMemory<byte> body)
    {
        Outcome = outcome;
        StatusCode = statusCode;
        Body = body;
    }

    /// <summary>
    /// Gets what happened.
    /// </summary>
    public PeerReplyOutcome Outcome { get; }

    /// <summary>
    /// Gets the status the peer answered with, or zero where there was no answer.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets the answer bytes, empty for every outcome but <see cref="PeerReplyOutcome.Answered"/>.
    /// </summary>
    /// <remarks>
    /// A refused answer carries no bytes on purpose. A caller that got a partial body back
    /// would have somewhere to put a truncated message, and a truncated message that parses
    /// is worse than one that does not arrive.
    /// </remarks>
    public ReadOnlyMemory<byte> Body { get; }

    /// <summary>
    /// An answer that arrived inside its bound.
    /// </summary>
    /// <param name="statusCode">The status the peer answered with.</param>
    /// <param name="body">The answer bytes.</param>
    /// <returns>The reply.</returns>
    public static PeerReply Answered(int statusCode, ReadOnlyMemory<byte> body)
        => new PeerReply(PeerReplyOutcome.Answered, statusCode, body);

    /// <summary>
    /// An answer that was refused for what it was rather than for what it said.
    /// </summary>
    /// <param name="outcome">Which rule refused it.</param>
    /// <param name="statusCode">The status the peer answered with, or zero.</param>
    /// <returns>The reply.</returns>
    public static PeerReply Refused(PeerReplyOutcome outcome, int statusCode)
        => new PeerReply(outcome, statusCode, ReadOnlyMemory<byte>.Empty);
}
