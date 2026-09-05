using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.ServerPairing.Configuration;
using Jellyfin.Plugin.ServerPairing.KeyStore;
using Jellyfin.Plugin.ServerPairing.Logging;
using Jellyfin.Plugin.ServerPairing.Mapping;
using Jellyfin.Plugin.ServerPairing.Protocol;
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
/// enrolment window is here and is issue #357, confirming a ceremony is #19, revoking is #24, the
/// pairing states the page renders are #49, and reporting what is held about one user is here
/// while removing it is the other half of #60. Listing a pairing's mapping table and removing
/// a row from it are here too, and are the half of issue #40 that needs nothing from the peer;
/// adding a row means choosing a peer user from a list fetched from the peer, and nothing in
/// this plugin fetches one. What this type owns is the plane, which is the elevation policy,
/// the answer shape and the rows in the endpoint table.
/// </para>
/// <para>
/// THE DIAGNOSTICS PAYLOAD WAS IN THAT LIST AND IS AN ACTION NOW, WHICH IS SMALLER THAN ISSUE
/// #51. What it carries is what the peer plane has refused and why, and the protocol versions
/// this build speaks. THAT SENTENCE COUNTED THE TWO PROTOCOL VERSIONS AMONG THE ABSENCES and
/// they are not one thing: what a peer speaks needs a peer that has answered, and what this
/// server speaks is <see cref="SupportedVersions"/> and needed only a reader. The members that
/// issue asks for besides - a state per pairing, the version a PEER speaks, a last error - have
/// no producer in this tree and no member here. <see cref="DiagnosticsAnswer"/> names each of them
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
    private readonly IPairingRecordStore _records;
    private readonly RefusalCounters _refusals;
    private readonly ArrivalLimit _arrivals;
    private readonly HeldAboutUser _held;
    private readonly UserMappings _mappings;
    private readonly ILocalUsers _localUsers;
    private readonly Enrolment _enrolment;
    private readonly ConfigurationReading _configuration;
    private readonly TimeProvider _time;
    private readonly ILogger<AdministrativePlaneController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AdministrativePlaneController"/> class.
    /// </summary>
    /// <param name="keys">Where this server keeps the keys it holds.</param>
    /// <param name="records">What state each pairing this server holds is in.</param>
    /// <param name="refusals">What the peer plane has refused, and why.</param>
    /// <param name="arrivals">How much of the peer plane each claimed identifier has used.</param>
    /// <param name="held">What this plugin holds about one user, and the audit entry saying it was asked.</param>
    /// <param name="mappings">The one way a mapping is read, made or removed, and the audit entry a change writes.</param>
    /// <param name="localUsers">The users this server has, read from the host.</param>
    /// <param name="enrolment">The join that opens a window and writes the record saying so.</param>
    /// <param name="configuration">This server's settings as read for this request: the peer a window opens against, and whether any setting was refused.</param>
    /// <param name="time">The one clock this plugin reads, for the instant a window opens at.</param>
    /// <param name="logger">Where the detail of an unreadable store goes, and the entry saying an enrolment was started.</param>
    public AdministrativePlaneController(
        IPairingKeyStore keys,
        IPairingRecordStore records,
        RefusalCounters refusals,
        ArrivalLimit arrivals,
        HeldAboutUser held,
        UserMappings mappings,
        ILocalUsers localUsers,
        Enrolment enrolment,
        ConfigurationReading configuration,
        TimeProvider time,
        ILogger<AdministrativePlaneController> logger)
    {
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _refusals = refusals ?? throw new ArgumentNullException(nameof(refusals));
        _arrivals = arrivals ?? throw new ArgumentNullException(nameof(arrivals));
        _held = held ?? throw new ArgumentNullException(nameof(held));
        _mappings = mappings ?? throw new ArgumentNullException(nameof(mappings));
        _localUsers = localUsers ?? throw new ArgumentNullException(nameof(localUsers));
        _enrolment = enrolment ?? throw new ArgumentNullException(nameof(enrolment));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _time = time ?? throw new ArgumentNullException(nameof(time));
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
    /// The enrolment windows this server has open.
    /// </summary>
    /// <returns>One entry per open window, or the named problem where the store could not be read.</returns>
    /// <remarks>
    /// This is the seventh property of issue #18: while a window is open the plugin says so,
    /// because a window an operator opened and forgot is the failure the rest of the enrolment
    /// bounds exist against. The bounds close a window on the first use, on a timer and after a
    /// small number of failures; none of them helps an operator who does not know one is open.
    /// <para>
    /// It reads the record store rather than <see cref="EnrolmentWindow"/>, which is the surface
    /// <c>docs/protocol.md</c> decided on and is argued at <see cref="OpenWindow"/>. A window is
    /// a pairing in <see cref="PairingState.Offered"/>, which is what the transition table says
    /// an administrator opening one produces, so a reader asking what pairings are half-built and
    /// a reader asking what windows are open are the same reader.
    /// </para>
    /// <para>
    /// THIS REMARK SAID WHAT WRITES THAT RECORD IS NOT BUILT AND THAT THIS ANSWERS AN EMPTY LIST
    /// ON EVERY RUNNING SERVER. <see cref="Enrolment"/> is the join between the window and the
    /// state machine and is registered, so a window opened through it is a record in
    /// <see cref="PairingState.Offered"/> and appears here.
    /// </para>
    /// <para>
    /// THIS REMARK THEN SAID NO ACTION CALLED THAT PRODUCER AND NAMED ISSUE #53 FOR THE ONE THAT
    /// WOULD. <see cref="OpenEnrolmentWindow"/> calls it, which is issue #357; #53 is about how a
    /// dashboard request is authenticated and never asked for an action. So what this answers on
    /// a server is what an administrator opened through that action and nothing has since closed,
    /// and no page calls the action yet, which is issue #49.
    /// </para>
    /// <para>
    /// It is a read and changes nothing, so it is not the state-changing endpoint issue #53's
    /// third condition asks for. The catch is the one <see cref="Pairings"/> carries and for the
    /// same reason: a store whose file does not parse throws out of the deserialiser, and an
    /// exception leaving an action reaches the host's own pipeline.
    /// </para>
    /// </remarks>
    [HttpGet("windows")]
    public IActionResult Windows()
    {
        try
        {
            // Materialised inside the try rather than returned as a query. A record store that
            // throws on a read is the failure the catch below answers, and a lazy sequence
            // handed to the serialiser would raise it outside this method instead.
            var open = _records.Pairings()
                .Select(_records.Read)
                .OfType<PairingRecord>()
                .Where(record => record.State == PairingState.Offered)
                .Select(record => new OpenWindow(record.PairingId, record.At))
                .ToList();

            return new ContentResult
            {
                StatusCode = 200,
                ContentType = "application/json",
                Content = JsonSerializer.Serialize(open),
            };
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so whether a window is open is unknown. The answer names the problem and carries nothing of the fault.");

            return new ContentResult
            {
                StatusCode = AdministrativeAnswer.ProblemStatus,
                ContentType = "application/json",
                Content = AdministrativeAnswer.Body(AdministrativeProblem.RecordStoreUnreadable),
            };
        }
    }

    /// <summary>
    /// Opens an enrolment window against the configured peer, and writes the record that says a
    /// pairing is being built with it.
    /// </summary>
    /// <returns>
    /// Created, carrying the identifier the record was written under and the instant it opened,
    /// in the shape <see cref="Windows"/> lists; the refusal where this server declines to open
    /// one; or the named problem where the caller could not be named or the store could not be
    /// read or written.
    /// </returns>
    /// <remarks>
    /// This is issue #357 and the second action on this plane that changes state. Every part of
    /// an opening existed before it - the window, the join that writes a record when one opens,
    /// the read that says one is open - and nothing on a server called the join, so the page
    /// rendered an empty list on every server and no pairing had ever been in
    /// <see cref="PairingState.Offered"/> on one.
    /// <para>
    /// THE ADDRESS IS THE ONE THE CONFIGURATION HOLDS AND THE REQUEST CARRIES NONE.
    /// <c>docs/configuration.md</c> fixes <c>PeerAddress</c> as the one address this server will
    /// send a pairing request to, and parses it there under the cleartext acknowledgement the
    /// same file governs, so an address arriving in a body would be a second address with a
    /// second parse. The reading is resolved per request rather than once at load, so the address
    /// an operator saved a moment ago is the one a window opens against.
    /// </para>
    /// <para>
    /// WHAT IS REFUSED, AND IN WHAT ORDER. A principal naming nobody is refused before anything
    /// is read, because the record names its actor and a change nobody is named for is the
    /// trail's failure and not only the entry's. A configuration a setting was refused on is
    /// refused next, which is where <c>docs/configuration.md</c> says <c>MayPair</c> is read: the
    /// window would open on the lifetime a server nobody configured runs on rather than the one
    /// the operator asked for, and a window of a length they did not choose is one they will not
    /// go looking for. A configuration holding no address has nothing to open against. Then the
    /// window's own two refusals, a pairing already held with that peer and a window already
    /// open against it, are carried to the wire under a word each rather than collapsed into one.
    /// </para>
    /// <para>
    /// The window answers first and the record follows it, which is <see cref="Enrolment"/>'s
    /// property rather than this action's, so a refused opening writes nothing and the audit
    /// entry is written only once a record is. The entry is the row <c>docs/logging.md</c> gives
    /// an enrolment that was started, and it carries the peer address, which that row names and
    /// which is an address the operator entered rather than anything a peer said.
    /// </para>
    /// <para>
    /// One catch, over the one store the join reads and writes, and its bound is worth stating.
    /// A record left in <c>Offered</c> by a window a restart lost is retired on the way in, and
    /// the state machine sweeps the mapping table when a pairing ends, so a mapping store that
    /// will not read on that path is answered under the record store's name. That path is
    /// reached only where a half-built record already exists for this address, and the log line
    /// carries the fault's own words for an operator who meets it.
    /// </para>
    /// </remarks>
    [HttpPost("windows")]
    public IActionResult OpenEnrolmentWindow()
    {
        var administrator = RequestingAdministrator.Of(User);

        if (administrator is null)
        {
            return Named(AdministrativeProblem.AdministratorUnidentified);
        }

        if (!_configuration.MayPair)
        {
            return Refused(OpeningRefusal.ConfigurationRefused);
        }

        var peer = _configuration.Peer;

        if (peer is null)
        {
            return Refused(OpeningRefusal.NoPeerAddress);
        }

        var at = _time.GetUtcNow();
        WindowOpened opened;

        try
        {
            opened = _enrolment.Open(peer, administrator, at);
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The pairing record store could not be read or written for an administrator, so whether a window was opened is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.RecordStoreUnreadable);
        }

        if (opened.Opening != WindowOpening.Opened)
        {
            return Refused(OpeningAnswer.RefusalFor(opened.Opening));
        }

        // The join names the record it wrote whenever the window opened, which is its own
        // contract rather than a hope, and EnrolmentTests holds it to that.
        var pairingId = opened.PairingId!;

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation(
                "An enrolment was started by an administrator. Pairing: {PairingId}, peer address: {PeerAddress}, administrator: {Administrator}",
                OneLine.Of(pairingId),
                OneLine.Of(peer.Value),
                OneLine.Of(administrator));
        }

        return new ContentResult
        {
            StatusCode = 201,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(new OpenWindow(pairingId, at)),
        };
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
            Content = JsonSerializer.Serialize(
                DiagnosticsAnswer.Of(_refusals, _arrivals, SupportedVersions.Range)),
        };
    }

    /// <summary>
    /// What this plugin holds about one local user, across every pairing.
    /// </summary>
    /// <param name="localUserId">The user on this server the report is about.</param>
    /// <returns>
    /// One entry per mapping held for that user, empty where they are mapped nowhere, or the
    /// named problem where a store could not be read or the caller could not be named.
    /// </returns>
    /// <remarks>
    /// This is the report half of issue #60. An operator running a server for a household may be
    /// asked by one of its users what is held about them, and this is what lets them answer
    /// without reading a file by hand. <c>docs/data.md</c> fixes what the report covers and
    /// <see cref="HeldMapping"/> is that scope as a shape.
    /// <para>
    /// IT WALKS THE RECORD STORE AND NOT THE KEY STORE, AND THAT IS THE DECISION THIS ACTION
    /// CARRIES. The mapping store is readable per pairing, so a report for a person is a walk
    /// over pairings, and <see cref="Pairings"/> hands back the key store's enumeration, which is
    /// every pairing holding key material. A pairing that has not finished enrolling holds no key
    /// and may hold a mapping, so a report walked over that enumeration answers nothing for a
    /// user mapped only under such a pairing and looks exactly like a report for a user mapped
    /// nowhere, which is the failure #60's own notes name. The record store enumerates every
    /// pairing a record exists for, which is every pairing a mapping may be made under, and a
    /// case in the suite refuses the narrower walk.
    /// </para>
    /// <para>
    /// Who asked is read from the principal the host authenticated, by
    /// <see cref="RequestingAdministrator"/>, and where that names nobody the report is refused
    /// rather than audited under nobody. It is a read and changes nothing, so it is not the
    /// state-changing endpoint issue #53's third condition asks for; the one thing it writes is
    /// the audit entry <see cref="HeldAboutUser"/> owns.
    /// </para>
    /// <para>
    /// THE REMOVAL HALF OF #60 IS NOT HERE. Nothing on this plane removes a user from every
    /// pairing, because that removal has to raise the consumer event once per pairing and there
    /// is no contract to raise it through until issue #43 lands. A removal that took the mapping
    /// and told no consumer would read as total while leaving the rows it exists to remove, so it
    /// is absent rather than half-built, and <c>docs/data.md</c> says so beside what it covers.
    /// <see cref="Unmap"/> is not that operation: it removes one mapping under one pairing at an
    /// administrator's decision, which is what a pairing's table offers, and it tells no consumer
    /// anything because nothing moved under a mapping yet and the wording an operator confirms
    /// says what already arrived stays where it arrived.
    /// </para>
    /// <para>
    /// Two catches rather than one, for the reason the two above carry two: the record store and
    /// the mapping store are two files, and a single catch over both would name whichever the
    /// code happened to reach first, sending an operator to the wrong one.
    /// </para>
    /// </remarks>
    [HttpGet("users/{localUserId}")]
    public IActionResult HeldAbout(string localUserId)
    {
        var administrator = RequestingAdministrator.Of(User);

        if (administrator is null)
        {
            return Named(AdministrativeProblem.AdministratorUnidentified);
        }

        IReadOnlyList<string> pairings;

        try
        {
            pairings = _records.Pairings();
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so what is held about a user is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.RecordStoreUnreadable);
        }

        try
        {
            var held = new List<HeldMapping>();

            foreach (var mapping in _held.Report(pairings, localUserId, administrator))
            {
                held.Add(new HeldMapping(mapping.PairingId, mapping.LocalUserId, mapping.PeerUserId, mapping.PeerDisplayName));
            }

            return new ContentResult
            {
                StatusCode = 200,
                ContentType = "application/json",
                Content = JsonSerializer.Serialize(held),
            };
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The mapping store could not be read for an administrator, so what is held about a user is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.MappingStoreUnreadable);
        }
    }

    /// <summary>
    /// The mapping table of one pairing: every mapping under it, and every local user who has
    /// none.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <returns>
    /// The table, not found where no record is held for that pairing, or the named problem where
    /// a store could not be read.
    /// </returns>
    /// <remarks>
    /// This is the listing half of issue #40, and the shape is <see cref="MappingTable"/>: the
    /// local user and the cached peer display name per mapping, an unset cache shown as the
    /// identifier rather than as an empty cell, and the local users who are unmapped, so an
    /// operator wondering why somebody is not syncing does not work it out by subtraction.
    /// <para>
    /// A PAIRING NOTHING IS HELD FOR IS NOT FOUND RATHER THAN AN EMPTY TABLE WITH EVERY USER
    /// UNMAPPED. <see cref="PairingState.Absent"/> is what an identifier nothing is held for reads
    /// as, and a table for it would invite mapping under a pairing that does not exist, which
    /// <see cref="UserMappings.Map"/> refuses anyway; the refusal is moved to the moment of the
    /// listing so the page never offers it. A pairing in <see cref="PairingState.Revoked"/> keeps
    /// its record and is listed, with an empty table, because the state machine swept its
    /// mappings when it ended, and that is the answer rather than a fault.
    /// </para>
    /// <para>
    /// It is a read and changes nothing, and it writes no audit entry. The report of what is
    /// held about a person writes one because it is an act about a person; this is the dashboard
    /// reading the table, which <c>docs/logging.md</c> names as where an operator entitled to the
    /// answer reads who is mapped to whom.
    /// </para>
    /// <para>
    /// The users this server has are read from the host through <see cref="ILocalUsers"/> and
    /// are not behind a catch: what may throw there is the host's own user manager, and a fault
    /// in the host's own service is the host's to answer for rather than something a sentence
    /// naming one of this plugin's stores could describe.
    /// </para>
    /// </remarks>
    [HttpGet("pairings/{pairingId}/mappings")]
    public IActionResult Mappings(string pairingId)
    {
        PairingRecord? record;

        try
        {
            record = _records.Read(pairingId);
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so whether a pairing holds a mapping table is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.RecordStoreUnreadable);
        }

        if (record is null)
        {
            return NotFound();
        }

        IReadOnlyList<UserMapping> held;

        try
        {
            held = _mappings.For(pairingId);
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The mapping store could not be read or written for an administrator, so what a pairing's table holds is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.MappingStoreUnreadable);
        }

        return new ContentResult
        {
            StatusCode = 200,
            ContentType = "application/json",
            Content = JsonSerializer.Serialize(MappingTable.Of(pairingId, held, _localUsers.Users())),
        };
    }

    /// <summary>
    /// Removes the mapping held for one local user under one pairing, at an administrator's
    /// decision.
    /// </summary>
    /// <param name="pairingId">The pairing.</param>
    /// <param name="localUserId">The user on this server whose mapping goes.</param>
    /// <returns>
    /// No content where a mapping was there and is now gone, not found where there was none, or
    /// the named problem where the store could not be read or written or the caller could not
    /// be named.
    /// </returns>
    /// <remarks>
    /// This is the removal half of issue #40, and it is the first action on this plane that
    /// changes state. It goes through <see cref="UserMappings.Unmap"/> and through nothing else,
    /// so the audit entry naming who removed it is written by the one type every change passes
    /// and cannot be skipped here. Who removed it is the principal the host authenticated, read
    /// by <see cref="RequestingAdministrator"/>, and where that names nobody the removal is
    /// refused before the store is touched, because a change nobody is named for is the trail's
    /// failure and not only the entry's.
    /// <para>
    /// WHAT THE OPERATOR IS TOLD BEFORE THEY DO THIS IS NOT HERE. Issue #40 asks that the
    /// consequence for already-transferred data be stated at the moment of removal, and the
    /// sentence is <c>DestructiveWording.RemoveMapping</c>, which the page shows and this action
    /// does not carry: an answer carrying a sentence is a second copy of it, and the suite refuses
    /// a sentence anywhere but the one place it lives. What this action does is the act the
    /// sentence describes, and nothing more: the mapping and its display cache go, what already
    /// arrived under it stays on the user it arrived on, and nothing here reaches it.
    /// </para>
    /// <para>
    /// Removing a mapping that is not there is not found and writes nothing, which is the
    /// property <see cref="UserMappings.Unmap"/> holds and this answer passes on: an entry per
    /// call rather than per change would let anything reaching this path grow the log without a
    /// mapping ever moving.
    /// </para>
    /// <para>
    /// One catch, over a read and a write of one file, and it names the mapping store: the
    /// removal touches no other store, so there is no second file an operator could be sent to
    /// by mistake.
    /// </para>
    /// </remarks>
    [HttpDelete("pairings/{pairingId}/mappings/{localUserId}")]
    public IActionResult Unmap(string pairingId, string localUserId)
    {
        var administrator = RequestingAdministrator.Of(User);

        if (administrator is null)
        {
            return Named(AdministrativeProblem.AdministratorUnidentified);
        }

        bool removed;

        try
        {
            removed = _mappings.Unmap(pairingId, localUserId, administrator);
        }
#pragma warning disable CA1031 // Every escaping exception is the failure this catch exists for, so the type cannot be narrowed without reopening it.
        catch (Exception fault)
#pragma warning restore CA1031
        {
            _logger.LogError(fault, "The mapping store could not be read or written for an administrator, so whether a mapping was removed is unknown. The answer names the problem and carries nothing of the fault.");

            return Named(AdministrativeProblem.MappingStoreUnreadable);
        }

        return removed ? NoContent() : NotFound();
    }

    /// <summary>
    /// The sentences an operator reads on the page, served out of the registers that hold them.
    /// </summary>
    /// <returns>Both registers, each sentence under the name of its constant.</returns>
    /// <remarks>
    /// The page shows <c>DestructiveWording.RemoveMapping</c> in the same view as the action
    /// that removes a mapping, and it may not carry the sentence itself: the suite refuses
    /// markup that carries one, because a pasted copy drifts from the register the operator
    /// guide is held equal to. So the page asks here on every show, and what it renders is the
    /// constant as it stands at that moment. <see cref="WordingAnswer"/> builds the answer by
    /// reflection over the registers, so nothing here is a second copy either.
    /// <para>
    /// It reads no store and changes nothing, so there is no file to fail to read, no named
    /// problem to answer with and no catch. It is not the state-changing endpoint issue #53's
    /// third condition asks for.
    /// </para>
    /// </remarks>
    [HttpGet("wording")]
    public IActionResult Wording()
        => new ContentResult
        {
            StatusCode = 200,
            ContentType = "application/json",
            Content = WordingAnswer.Body(),
        };

    /// <summary>
    /// The answer a named problem is carried in.
    /// </summary>
    /// <param name="problem">The problem.</param>
    /// <returns>The answer, at the status every named problem on this plane carries.</returns>
    private static ContentResult Named(AdministrativeProblem problem)
        => new ContentResult
        {
            StatusCode = AdministrativeAnswer.ProblemStatus,
            ContentType = "application/json",
            Content = AdministrativeAnswer.Body(problem),
        };

    /// <summary>
    /// The answer a refused opening is carried in.
    /// </summary>
    /// <param name="refusal">The refusal.</param>
    /// <returns>The answer, at the status every refusal to open a window carries.</returns>
    private static ContentResult Refused(OpeningRefusal refusal)
        => new ContentResult
        {
            StatusCode = OpeningAnswer.RefusedStatus,
            ContentType = "application/json",
            Content = OpeningAnswer.Body(refusal),
        };
}
