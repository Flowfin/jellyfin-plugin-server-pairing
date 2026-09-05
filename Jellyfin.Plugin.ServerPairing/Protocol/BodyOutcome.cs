namespace Jellyfin.Plugin.ServerPairing.Protocol;

/// <summary>
/// What reading the body of an arriving request produced.
/// </summary>
public enum BodyOutcome
{
    /// <summary>
    /// The body is the one the member table fixes for this message. An empty body is this where
    /// the table says the message carries none.
    /// </summary>
    Read = 0,

    /// <summary>
    /// The body is not. It is not one object, it carries a member the table does not name, it is
    /// missing one the table does name, a value is outside its limit, or the message carries a
    /// body where the table says it carries none.
    /// </summary>
    DidNotParse = 1,

    /// <summary>
    /// The body was not read, which is two messages and two different reasons. An
    /// <c>exchange</c> body is opaque to this layer and <c>docs/protocol.md</c> names none of
    /// what is inside it, so reading one here would be this layer deciding the consumer contract
    /// that M6 owns. A <c>rotate</c> body has a member table and no reader, so it is refused by
    /// nothing here today and the shape is owed.
    /// </summary>
    NotReadHere = 2,
}
