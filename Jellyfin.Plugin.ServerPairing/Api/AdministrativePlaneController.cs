using System;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.ServerPairing.Api;

/// <summary>
/// The plane an administrator reaches, behind the host's elevation policy.
/// </summary>
/// <remarks>
/// This and <see cref="PeerPlaneController"/> are the two planes <c>docs/endpoints.md</c>
/// defines, and every difference between them follows from who is on the other end. A peer
/// holds this plugin's own key and none of the host's, so that plane is anonymous to the
/// server and every cause collapses into one refusal. An administrator has already passed the
/// host's elevation policy before an action here runs, so this plane asks the host for that
/// policy and names what went wrong.
/// <para>
/// The policy is declared once, on the class, rather than per action. An action added without
/// an attribute inherits it, which is the direction that fails safe;
/// <c>EndpointAuthorizationTableTests</c> is what refuses one that does not carry it, out of
/// the host's own action discovery rather than out of the attribute.
/// </para>
/// <para>
/// WHETHER THE HOST THEN ENFORCES THAT POLICY IS NOT MEASURED BY ANYTHING IN THIS REPOSITORY.
/// The refusal is the server's authorization middleware, so reaching it means standing that
/// pipeline up, and a case that did would be judging the framework rather than this plugin.
/// <c>docs/testing.md</c> refuses the neighbouring apparatus - two real servers, and a browser -
/// and does NOT name this case, so this paragraph is the argument rather than a citation of
/// one. What the suite holds is that the declaration is present and is the right one; what the
/// enforcement is rests on the reading of the server's source in <c>docs/endpoints.md</c>,
/// which is somebody else's tree read at two tags.
/// </para>
/// <para>
/// The actions of this plan land here rather than each bringing a controller: opening an
/// enrolment window and confirming a ceremony are issues #18 and #19, revoking is #24, editing
/// mappings is #40 and the pairing states the page renders are #49. What this type owns is the
/// plane, which is the elevation policy, the answer shape and the rows in the endpoint table.
/// </para>
/// <para>
/// THE DIAGNOSTICS PAYLOAD WAS IN THAT LIST AND IS AN ACTION NOW, WHICH IS SMALLER THAN ISSUE
/// #51. What it carries is what the peer plane has refused and why, and the members that issue
/// asks for besides - a state per pairing, the two protocol versions, a last error - have no
/// producer in this tree and no member here. <see cref="DiagnosticsAnswer"/> names each of them
/// with what has to exist first, so the absence is read there rather than inferred from this
/// list.
/// </para>
/// </remarks>
[ApiController]
[Authorize(Policy = Policies.RequiresElevation)]
[Route("ServerPairing/Administration")]
public sealed class AdministrativePlaneController : ControllerBase
{
    private readonly IPairingKeyStore _keys;
    private readonly RefusalCounters _refusals;
    private readonly ArrivalLimit _arrivals;
    private readonly ILogger<AdministrativePlaneController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdministrativePlaneController"/> class.
    /// </summary>
    /// <param name="keys">Where this server keeps the keys it holds.</param>
    /// <param name="refusals">What the peer plane has refused, and why.</param>
    /// <param name="arrivals">How much of the peer plane each claimed identifier has used.</param>
    /// <param name="logger">Where the detail of an unreadable store goes.</param>
    public AdministrativePlaneController(
        IPairingKeyStore keys,
        RefusalCounters refusals,
        ArrivalLimit arrivals,
        ILogger<AdministrativePlaneController> logger)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _refusals = refusals ?? throw new ArgumentNullException(nameof(refusals));
        _arrivals = arrivals ?? throw new ArgumentNullException(nameof(arrivals));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The pairings this server holds a key for.
    /// </summary>
    /// <returns>The identifiers, or the named problem where the store could not be read.</returns>
    /// <remarks>
    /// Identifiers and nothing else, which is <see cref="IPairingKeyStore.Pairings"/>'s own
    /// contract rather than a choice made here: the store has no accessor that hands key
    /// material to code that only wants to display something, and this is that code.
    /// <para>
    /// What an operator can otherwise learn this from is a line written once at startup, so a
    /// server that has been running for a week answers the question only in a log file nobody
    /// kept. That is the whole of what this action is for. It is a read and changes nothing,
    /// so it is not the state-changing endpoint issue #53's third condition asks for.
    /// </para>
    /// <para>
    /// The catch is not tidiness. A store whose file does not parse throws out of the
    /// deserialiser, and an exception leaving an action reaches the host's own pipeline, which
    /// on a server with the developer page turned on answers an administrator with a stack
    /// trace naming this plugin's types instead of with the one sentence they can act on.
    /// </para>
    /// </remarks>
    [HttpGet("pairings")]
    public IActionResult Pairings()
    {
        try
        {
            return new ContentResult
            {
                StatusCode = 200,
                ContentType = "application/json",
                Content = JsonSerializer.Serialize(_keys.Pairings()),
            };
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The key store could not be read for an administrator, so what this server holds is unknown. The answer names the problem and carries nothing of the fault.");

            return new ContentResult
            {
                StatusCode = AdministrativeAnswer.ProblemStatus,
                ContentType = "application/json",
                Content = AdministrativeAnswer.Body(AdministrativeProblem.KeyStoreUnreadable),
            };
        }
    }

    /// <summary>
    /// What this server has refused on the pairing plane, and why.
    /// </summary>
    /// <returns>The diagnostics payload.</returns>
    /// <remarks>
    /// This is the surface issue #51 is about, and it is the thing an operator pastes into a
    /// support thread, so what it may hold is decided by what it must never hold.
    /// <see cref="DiagnosticsAnswer"/> is where that argument lives and is also where the members
    /// this payload does not have yet are named, each with what has to exist before it can.
    /// <para>
    /// It is a read and changes nothing, so it is not the state-changing endpoint issue #53's
    /// third condition asks for. It has no catch of its own, and that is the difference from
    /// the action above rather than an omission: what it reads is two objects this process
    /// holds in memory, not a file that may not parse, so there is no fault a catch here could
    /// turn into a sentence an administrator can act on.
    /// </para>
    /// <para>
    /// WHETHER THE PAYLOAD IS FREE OF WHAT A LIFECYCLE CREATES IS NOT ASSERTED BY ANYTHING.
    /// That is issue #51's second condition, it needs a full enrolment, rotation and revocation
    /// driven through the harness in issue #29, and neither exists. A case written today would
    /// assert an absence over a payload that no secret had ever been near, pass, and go on
    /// passing after the first one was. What the suite holds instead is that every member of
    /// this payload is a number over an enumeration, which is a narrower statement.
    /// </para>
    /// </remarks>
    [HttpGet("diagnostics")]
    public IActionResult Diagnostics()
    {
        return new ContentResult
        {
            StatusCode = 200,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(DiagnosticsAnswer.Of(_refusals, _arrivals)),
        };
    }
}
