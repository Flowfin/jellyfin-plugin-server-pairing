# What this plugin logs, and what it may never log

A Jellyfin log file is something an operator pastes into a public forum thread
when asking for help. Whatever this plugin writes there is readable by everyone
who reads that thread, months later, out of context. So the contents of the log
are part of the threat model rather than a detail of the implementation.

Two lists follow. The second one is the one that matters.

## What is logged

Every entry below carries the pairing identifier, so entries about one pairing
can be pulled out of a log holding several. The fault row is the exception and
says why underneath the table.

| Event | Level | Fields |
| --- | --- | --- |
| An enrolment was started | Information | pairing id, peer address, the administrator who started it |
| An enrolment completed | Information | pairing id, peer address, the administrator who confirmed it, the protocol version agreed |
| An enrolment expired or was abandoned | Information | pairing id, reason |
| A key was rotated | Information | pairing id, which side initiated, the overlap window that is now open |
| A rotation overlap closed | Information | pairing id |
| A pairing was revoked | Warning | pairing id, which side revoked, whether the peer was reachable at the time |
| A request was refused | Warning | pairing id where one is known, the refusal category, the peer address |
| A refusal rate crossed its threshold | Warning | pairing id, the count and the window it was counted over |
| A mapping was added, changed or removed | Information | pairing id, the administrator who did it, which direction |
| What is held about one user was reported | Information | the administrator who asked, how many pairings were looked under, how many mappings were found |
| The key store could not be read or written | Error | the operation, the reason, no path contents |
| The pairing record store could not be read | Error | the operation, the reason, no path contents, no pairing identifier |
| The mapping store could not be read or written | Error | the operation, the reason, no path contents, no pairing identifier |
| A request on the pairing plane faulted | Error | which of the six messages it arrived on, and the fault the runtime raised, with no pairing identifier |
| The plugin started against a store that already holds a pairing | Information | pairing id, one entry per pairing found |
| The key store was carried up from an older format | Information | the format it was in, the format it is now, and the name of the copy left beside it |
| A setting was refused at startup | Error | the setting, the rule it broke, and that nothing was corrected |
| A cleartext peer address was acknowledged | Warning | the setting that acknowledges it |

Debug adds timing and state machine transitions for the same events. It adds no
field that is not in the table above.

Eight rows carry no pairing identifier and each has its own reason. **This
paragraph said six**, and before that five, and three, and before that it said
the fault row was the only one, which was wrong about the table it sits under
from the day the store rows landed. Every count is corrected here rather than by
adding an identifier those rows have no way to know.

The fault row leaves it out deliberately. A fault is reachable before the request
has been read, so at that moment the only identifier available is the one the
caller put in a header, unverified, on a plane where an unverified caller is
assumed hostile. Writing it would let a stranger choose which pairing an
operator's error line appears to be about. What the entry names instead is the
path the request arrived on, which is this server's own fact.

The four store rows leave it out because none of them is about a pairing. A
store that cannot be read holds no identifier anybody can name, and that is true
of all three stores: the record store is the one that would carry an identifier,
the mapping store is keyed by one, and a walk that threw is a walk that reached
none. A store being carried up from an older format is one event about one file
however many pairings are in it.
The migration row names the file rather than its contents on purpose: what is
inside is key material, and a row naming contents would put every key the store
holds into the log.

The two configuration rows leave it out because the configuration is read once,
at startup, before any pairing has been looked at. A refused setting is a fact
about this server rather than about a relationship, and an acknowledged cleartext
address weakens every pairing this server will ever have rather than one of them.
Both name the setting instead, which is what an operator has to open to change
it.

The report row leaves it out because a report of what is held about a person
crosses every pairing at once and is about the person rather than about any one
of them. It names the administrator who asked and how far the report looked, and
neither user: the peer identity is on the list below, and the local identifier
is not a field the mapping-change row above names either. So the trail says that
the question was asked, by whom and when, and the mapping table is where somebody
entitled to the answer reads it.

The fault text is the runtime's and not this plugin's, which is where the list
below meets its one soft edge. Nothing on this plane parses a body today, so no
exception it can raise is built out of one; the day something does, the message
of a parse failure is a place a fragment of a body can reach a log without anyone
writing it there. That is the case to check when a body parser lands rather than
a defect in the tree now.

## What may never be logged, at any level, including Debug

- key material of any kind, in any encoding, whole or partial
- the enrolment secret, before or after it is consumed
- the preimage of a fingerprint
- an authorization header, whole or partial
- a request signature
- a request or response body from the pairing plane
- the mapped user identities on the peer, in any form, including a username

A peer address is fine. A pairing identifier is fine. A refusal category is
fine, and it is deliberately a category rather than a detail, because the log
and the refusal path have the same oracle problem.

A truncated key fingerprint is fine only where this document says how many bits
it exposes and why that is acceptable for the thing it identifies.

One truncation is defined in the tree. [`crypto.md`](crypto.md) fixes the
leading 128 bits of a SHA-256 digest as the value two operators compare, and
argues that length in both directions. The number is cited rather than restated
here, because a second copy of a cryptographic parameter is the copy that goes
stale, and that document is the authority for every one of them.

What is missing is the second half of the rule rather than the first. This
document has not said whether that value may be logged, or what logging it would
expose for the pairing it identifies, so none may be logged. The prohibition
rests on the sentence above being unwritten rather than on there being no
truncation to prohibit, which is what it used to rest on.

## One entry is one line

The two lists above say what an entry may hold. This says what an entry may be,
and it is a separate rule because a value that satisfies both lists can still
write entries this plugin never composed.

An entry that carries a line break is read as several. Every line after the
first is one an operator attributes to this plugin, at whatever level and about
whatever pairing the value chose, so a caller who can put a break into a value
this plugin logs can write the log rather than appear in it. A pairing
identifier arrives as a path segment of the request that asked for the change
and nothing between the route and the entry reads its shape; an administrator
arrives as a claim the host set, which is from outside this plugin whatever the
host does with it today.

So every value from outside is put on one line before it reaches an entry, by
`OneLine` in `Jellyfin.Plugin.ServerPairing/Logging/OneLine.cs`. What it
replaces is wider than the break: the escape character drives the terminal
rendering the file, and the bidirectional overrides of CVE-2021-42574 reorder
what a reader sees without changing what was stored, which is the same attack
`.github/workflows/unicode-guard.yml` refuses in tracked source, arriving at
runtime through a value instead of at review time through a file.

The words the caller wrote stay in the entry. Removing them is a larger
decision about what an audit entry may say, and it is not taken here; what is
guaranteed is that they are one line of one entry, so an operator reads them as
a value somebody sent rather than as a sentence this plugin wrote.

WHAT THIS IS NOT IS VALIDATION AT THE EDGE. Refusing a malformed identifier
where it arrives is a different rule with a different answer per endpoint, and
nothing here asks for it or stands in for it. What holds at the call site holds
whatever the edge does.

TWO CALL SITES CARRY A VALUE FROM OUTSIDE TODAY AND BOTH ARE GUARDED. The
mapping-change row and the report row are the two, and the guard is proved at
each of them rather than only on the type: `MappingAuditTests` and
`HeldAboutUserTests` each drive a forged value through and assert the entry has
no break in it, and deleting the call turns exactly those two cases red. The
other rows name this server's own facts - a setting, a store format, a message
this plugin enumerates - and are not put through it. A row that starts carrying
a value from outside is added to that set in the change that makes it do so,
and nothing reads this paragraph to check that it was.

## The audit trail these lists have to support

From the log alone, and with nothing else to hand, an operator can answer:

- when a pairing was created, and by which administrator
- which peer it was created against
- when a key was rotated, and which side started it
- when a pairing was revoked, and which side revoked it
- how many requests were refused, over what window, and in which category
- when an administrator asked what is held about a user, and which administrator

Each of those is answerable from the table above. That is the reason the table
is shaped the way it is, rather than being a list of whatever happened to be
convenient at the call site.

## The rule that makes this hold

A promise in a document is not a guarantee about a log. What makes this hold is
a test that drives a full enrolment, a rotation and a revocation against a
capturing logger, at Debug, and asserts that the captured text contains none of
the secrets the run generated. The test generates the secrets, so it knows every
byte it is looking for, and it fails on any of them appearing anywhere in the
captured output.

That test does not exist. When this was written there was also no test project
to put it in; one landed afterwards, in `87cee9c`.

The reason given here after that was that there is no enrolment, no rotation and
no revocation to drive. That is no longer the reason. A rotation and a revocation
are both in the tree:

    git grep -n "public RotationOutcome Rotate" origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs:150:    public RotationOutcome Rotate(ReadOnlySpan<byte> replacement, DateTimeOffset at, DateTimeOffset supersededStopsAt)

    git grep -n "AdministratorRevoked = " origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs:31:    AdministratorRevoked = 3,

What is missing is the logging itself, and it is less missing than it was. This
paragraph said nothing in the plugin took a logger and that a capturing logger
would be handed a run that writes nothing, then that two types take one, then
that four do, then that five do. Six do. Which rows of the table each of them
writes is under the reading rather than counted here.

    git grep -nE "ILogger|_logger" origin/master -- Jellyfin.Plugin.ServerPairing
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:74:    private readonly ILogger<AdministrativePlaneController> _logger;
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:95:        ILogger<AdministrativePlaneController> logger)
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:104:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:144:            _logger.LogError(fault, "The key store could not be read for an administrator, so what this server holds is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:214:            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so whether a window is open is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:330:            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so what is held about a user is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:355:            _logger.LogError(fault, "The mapping store could not be read for an administrator, so what is held about a user is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:410:            _logger.LogError(fault, "The pairing record store could not be read for an administrator, so whether a pairing holds a mapping table is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:430:            _logger.LogError(fault, "The mapping store could not be read or written for an administrator, so what a pairing's table holds is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/AdministrativePlaneController.cs:503:            _logger.LogError(fault, "The mapping store could not be read or written for an administrator, so whether a mapping was removed is unknown. The answer names the problem and carries nothing of the fault.");
    origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:39:    private readonly ILogger<PeerPlaneController> _logger;
    origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:69:    public PeerPlaneController(PeerPlane plane, TimeProvider time, ILogger<PeerPlaneController> logger)
    origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:73:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    origin/master:Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:260:            _logger.LogError(fault, "A request on the pairing plane faulted and was answered with the refusal every caller gets. Message: {PairingMessage}", message);
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:34:    private readonly ILogger<ConfigurationAtStartup> _logger;
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:42:    public ConfigurationAtStartup(ConfigurationReading reading, ILogger<ConfigurationAtStartup> logger)
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:45:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:58:            _logger.LogError(
    origin/master:Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:65:            _logger.LogWarning(
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:87:    private readonly ILogger<FilePairingKeyStore>? _logger;
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:141:    public FilePairingKeyStore(string file, Action<string, string>? moveIntoPlace, ILogger<FilePairingKeyStore>? logger)
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:145:        _logger = logger;
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:373:        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:375:            _logger.LogInformation(
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:46:    private readonly ILogger<StoreAtStartup> _logger;
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:53:    public StoreAtStartup(IPairingKeyStore store, ILogger<StoreAtStartup> logger)
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:56:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:82:            if (_logger.IsEnabled(LogLevel.Information))
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:86:                    _logger.LogInformation(
    origin/master:Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:96:            _logger.LogError(fault, "The key store could not be read at startup, so what it holds is unknown and no pairing will work. The server is left running.");
    origin/master:Jellyfin.Plugin.ServerPairing/Mapping/HeldAboutUser.cs:39:    private readonly ILogger<HeldAboutUser> _log;
    origin/master:Jellyfin.Plugin.ServerPairing/Mapping/HeldAboutUser.cs:52:    public HeldAboutUser(IUserMappingStore mappings, ILogger<HeldAboutUser> log)
    origin/master:Jellyfin.Plugin.ServerPairing/Mapping/UserMappings.cs:37:    private readonly ILogger<UserMappings> _log;
    origin/master:Jellyfin.Plugin.ServerPairing/Mapping/UserMappings.cs:53:    public UserMappings(IUserMappingStore mappings, PairingStateMachine pairings, ILogger<UserMappings> log)
    origin/master:Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:126:                services.GetRequiredService<ILogger<FilePairingKeyStore>>()));
    origin/master:Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:186:            services.GetRequiredService<ILogger<HeldAboutUser>>()));
    origin/master:Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:205:            services.GetRequiredService<ILogger<UserMappings>>()));

THIS BLOCK WENT STALE TWICE AND NO RUN ON THIS REPOSITORY SAW EITHER TIME. It
pasted two types, then four, then five, and the command returns 7 that hold a logger; the registration line
moved from 103 to 113 underneath it as well. What let that happen is the reading
rather than the check. `sh .github/reading-check.sh` walks a command naming
`origin/master`, and this one named a working tree, so it sat outside the walk on
every run that reported the tree clean - which is the class that file's own
header names, three of which were repaired by hand in `9c8dedd`. It is pinned at
`origin/master` above for that reason rather than for tidiness. It is inside the
walk now, so the next arrival that stales it reddens a run instead of waiting for
somebody to re-read the paragraph under it.

`PeerPlaneController` writes the fault row and `StoreAtStartup` writes the
startup row, and a capturing logger sits under each of them in the suite: under
the fault row it asserts what the entry holds and that none of it reaches the
caller, and under the startup row it asserts one entry per pairing found, each
naming its identifier, and that none of the key material the case generated
appears in any of them. So two rows have their contents checked and every other
row in the table does not.

`FilePairingKeyStore` writes the migration row. `AdministrativePlaneController`
writes both unreadable-store rows from the other side, where what meets an
administrator's request is a store that will not open. Two rows rather than one,
because the plane reads two files: the key store, for what this server holds, and
the pairing record store, for whether an enrolment window is open. An answer
naming one when the other is the broken one is a sentence an operator can act on,
pointed at the wrong disk.

`ConfigurationAtStartup` writes the two entries the table above carried no row
for at all, a refused setting at Error and an acknowledged cleartext peer address
at Warning. **That was a defect in the table** - what this plugin writes and what
this document says it writes have to be the same list, and for those two they had
not been since the call sites landed. The rows are in the table above now. Both
were found by reading the call sites in the tree against the table by hand, which
is the whole argument for the guard below.

`LoggedEventTableTests` walks this plugin's own source for every `ILogger` call
site, holds each one against the row it writes, and fails where a call site has
no row, where a row a call site names is absent from the table, and where a row
is claimed for a call site the source no longer has. A call site added without a
row now reddens the suite rather than waiting to be found the way these two were.

**What that guard does not judge is larger than what it does.** It compares a
call site against the NAME of a row and never against the row's level or its
fields, so an entry written at the wrong level, or carrying a field the row does
not name, passes it. It reads the message a call site opens with in the source
rather than what reaches a log on a running server. And it says nothing whatever
about the second list below: no check in this tree refuses a call site that
writes key material, a signature, or a peer's user identity.

ONE ENTRY IS JUDGED FURTHER THAN THAT, AND ONE IS NOT ALL OF THEM. The mapping
row's level, and the absence from it of the local and the peer user identity, are
asserted by `MappingAuditTests` over the text the entry actually produces. That
is one row out of the table, asserted by driving the call rather than by reading
the source, and it leaves every sentence above exactly as true of the others as
it was. It is written here so that a reader does not take the paragraph above for
a statement that nothing anywhere is checked, and it is not a step towards the
run the section below asks for.

The test this section asks for is a different and larger thing, and it does not
exist. It drives a full enrolment, a rotation and a revocation at Debug against a
capturing logger and looks for the secrets the run generated, and there is still
no enrolment to drive and nothing that writes any row the call sites above do not
already cover. Until it exists, the second list is a design statement and nothing
refuses a call site that violates it. This paragraph is the whole of that
disclosure and no later edit of this file turns it into a statement that the
logging has been checked.
