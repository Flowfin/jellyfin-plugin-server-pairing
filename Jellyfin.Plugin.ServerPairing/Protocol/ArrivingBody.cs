using System;

namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// The body of one arriving request, read against the member table its message belongs to.
/// </summary>
/// <remarks>
/// The dispatch is here rather than at the plane so that which body a message has stays beside
/// the bodies themselves, and the plane keeps what it is for, which is the order the checks run
/// in. <c>docs/protocol.md</c> is the authority for the table and a difference between that
/// document and this file is a defect in this file.
/// <para>
/// EMPTY MEANS ZERO BYTES, AND THAT IS A REFUSAL RATHER THAN A TOLERANCE. Where the table says a
/// message carries no body, an arriving <c>{}</c>, a space or a line feed is refused, so a
/// member cannot be smuggled into a body the document says has none and then relied on.
/// </para>
/// <para>
/// Nothing here reads a key, a record, a clock or a configuration. A body is judged as bytes
/// against a table, which is what lets the whole of it be exercised without a server.
/// </para>
/// </remarks>
public sealed class ArrivingBody
{
    private ArrivingBody(BodyOutcome outcome, HelloRequestBody? hello, ConfirmRequestBody? confirm)
    {
        Outcome = outcome;
        Hello = hello;
        Confirm = confirm;
    }

    /// <summary>
    /// Gets what reading the body produced.
    /// </summary>
    public BodyOutcome Outcome { get; }

    /// <summary>
    /// Gets the <c>hello</c> the body carried, where it carried one.
    /// </summary>
    public HelloRequestBody? Hello { get; }

    /// <summary>
    /// Gets the <c>confirm</c> the body carried, where it carried one.
    /// </summary>
    public ConfirmRequestBody? Confirm { get; }

    /// <summary>
    /// Reads the body of a request that has already verified.
    /// </summary>
    /// <param name="message">The message the body belongs to.</param>
    /// <param name="body">The body bytes, exactly as they arrived.</param>
    /// <returns>What was read.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The message is not one of the defined values.</exception>
    /// <remarks>
    /// It is called after verification and never before it, which is the plane's ordering rather
    /// than this type's, and the reason is the taxonomy: a body that does not parse is answered
    /// <c>malformed</c>, and that code may be seen only by a caller that has proved it holds the
    /// key. A body parsed before a signature was checked would hand that answer to a stranger and
    /// would spend a parse on bytes nobody authenticated.
    /// </remarks>
    public static ArrivingBody Read(PairingMessage message, ReadOnlySpan<byte> body) => message switch
    {
        PairingMessage.Hello => HelloRequestBody.TryRead(body, out var hello)
            ? new ArrivingBody(BodyOutcome.Read, hello, null)
            : Unparsed(),
        PairingMessage.Confirm => ConfirmRequestBody.TryRead(body, out var confirm)
            ? new ArrivingBody(BodyOutcome.Read, null, confirm)
            : Unparsed(),
        PairingMessage.Revoke or PairingMessage.Unpair => body.IsEmpty
            ? new ArrivingBody(BodyOutcome.Read, null, null)
            : Unparsed(),
        PairingMessage.Rotate or PairingMessage.Exchange =>
            new ArrivingBody(BodyOutcome.NotReadHere, null, null),
        _ => throw new ArgumentOutOfRangeException(nameof(message)),
    };

    /// <summary>
    /// The one refusal. Every way a body fails the table produces the same answer, and this type
    /// holds nothing that could differ between two of them.
    /// </summary>
    /// <returns>A body that did not parse.</returns>
    /// <remarks>
    /// A method rather than a shared instance in a static field, because a static field holding
    /// state is a service a test cannot replace and <c>StaticStateTests</c> refuses one anywhere
    /// in this assembly. The value is immutable, so nothing would have gone wrong with a shared
    /// one; the guard is about the shape rather than about this instance, and buying an
    /// allocation is the cheaper side of that trade.
    /// </remarks>
    private static ArrivingBody Unparsed() =>
        new ArrivingBody(BodyOutcome.DidNotParse, null, null);
}
