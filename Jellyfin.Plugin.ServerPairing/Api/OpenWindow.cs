using System;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// One enrolment window this server currently has open, as an administrator is told about it.
/// </summary>
/// <remarks>
/// A window an operator opened and forgot is the failure the enrolment bounds are really about,
/// so the plugin says while one is open rather than leaving it to a line written once in a log
/// at the moment it opened. That is the seventh property of issue #18.
/// <para>
/// WHICH SURFACE THIS IS READ FROM IS A DECISION THE TREE ALREADY TOOK, and it is not
/// <see cref="Protocol.EnrolmentWindow"/>. <c>docs/protocol.md</c> decides that a pairing in
/// <see cref="Protocol.PairingState.Offered"/> is written as a record under a provisional
/// identifier, and the argument it gives for taking that answer over the other one is this
/// surface: under the answer it refused, an open window would be visible only inside the window
/// type, which holds an address and no state, so a dashboard would have to read one surface for
/// a pairing an operator has started and another for every pairing that finished. So this is
/// read from the record store, which is the one surface, and the window type is not resolved
/// here.
/// </para>
/// <para>
/// No peer address is on this shape because no peer address is on the record. Issue #18 claims
/// that field and it is not built, so what an operator is told is that a window is open and
/// when it opened, and not which address it was opened against. That is a narrower answer than
/// the property asks for and it is the whole of what the record can answer today.
/// </para>
/// </remarks>
/// <param name="PairingId">
/// The provisional identifier the half-built pairing is held under. Nothing about it is secret:
/// <c>docs/protocol.md</c> says it is unique so that two windows opened in the same second are
/// two records, and unreachable because of its shape rather than because of its entropy.
/// </param>
/// <param name="OpenedAt">
/// When the transition that opened it was recorded. It is the record's own instant, so an
/// operator reading two open windows can tell which one they have forgotten about.
/// </param>
public sealed record OpenWindow(
    [property: JsonPropertyName("pairingId")] string PairingId,
    [property: JsonPropertyName("openedAt")] DateTimeOffset OpenedAt);
