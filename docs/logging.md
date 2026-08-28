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
| The key store could not be read or written | Error | the operation, the reason, no path contents |
| A request on the pairing plane faulted | Error | which of the five messages it arrived on, and the fault the runtime raised, with no pairing identifier |
| The plugin started against a store that already holds a pairing | Information | pairing id, one entry per pairing found |
| The key store was carried up from an older format | Information | the format it was in, the format it is now, and the name of the copy left beside it |

Debug adds timing and state machine transitions for the same events. It adds no
field that is not in the table above.

Three rows carry no pairing identifier and each has its own reason. **This
paragraph said the fault row was the only one**, and the two store rows above it
have never carried one either, so the sentence was wrong about the table it sits
under from the day the store rows landed. It is corrected here rather than by
adding an identifier those rows have no way to know.

The fault row leaves it out deliberately. A fault is reachable before the request
has been read, so at that moment the only identifier available is the one the
caller put in a header, unverified, on a plane where an unverified caller is
assumed hostile. Writing it would let a stranger choose which pairing an
operator's error line appears to be about. What the entry names instead is the
path the request arrived on, which is this server's own fact.

The two store rows leave it out because neither is about a pairing. A store that
cannot be read holds no identifier anybody can name, and a store being carried up
from an older format is one event about one file however many pairings are in it.
The migration row names the file rather than its contents on purpose: what is
inside is key material, and a row naming contents would put every key the store
holds into the log.

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

## The audit trail these lists have to support

From the log alone, and with nothing else to hand, an operator can answer:

- when a pairing was created, and by which administrator
- which peer it was created against
- when a key was rotated, and which side started it
- when a pairing was revoked, and which side revoked it
- how many requests were refused, over what window, and in which category

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
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/KeyOverlap.cs:152:    public RotationOutcome Rotate(ReadOnlySpan<byte> replacement, DateTimeOffset at, DateTimeOffset supersededStopsAt)

    git grep -n "AdministratorRevoked = " origin/master -- Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs
    origin/master:Jellyfin.Plugin.ServerPairing/Protocol/LocalEvent.cs:31:    AdministratorRevoked = 3,

What is missing is the logging itself, and it is less missing than it was. This
paragraph said nothing in the plugin took a logger and that a capturing logger
would be handed a run that writes nothing, and then it said two types take one.
Four do. Which rows of the table each of them writes, and which row two of them
write nothing for, is under the reading rather than counted here.

    git grep -nE "ILogger|_logger" -- Jellyfin.Plugin.ServerPairing
    Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:39:    private readonly ILogger<PeerPlaneController> _logger;
    Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:69:    public PeerPlaneController(PeerPlane plane, TimeProvider time, ILogger<PeerPlaneController> logger)
    Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:73:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    Jellyfin.Plugin.ServerPairing/Api/PeerPlaneController.cs:253:            _logger.LogError(fault, "A request on the pairing plane faulted and was answered with the refusal every caller gets. Message: {PairingMessage}", message);
    Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:34:    private readonly ILogger<ConfigurationAtStartup> _logger;
    Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:42:    public ConfigurationAtStartup(ConfigurationReading reading, ILogger<ConfigurationAtStartup> logger)
    Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:45:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:58:            _logger.LogError(
    Jellyfin.Plugin.ServerPairing/Configuration/ConfigurationAtStartup.cs:65:            _logger.LogWarning(
    Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:76:    private readonly ILogger<FilePairingKeyStore>? _logger;
    Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:130:    public FilePairingKeyStore(string file, Action<string, string>? moveIntoPlace, ILogger<FilePairingKeyStore>? logger)
    Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:134:        _logger = logger;
    Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:302:        if (_logger is not null && _logger.IsEnabled(LogLevel.Information))
    Jellyfin.Plugin.ServerPairing/KeyStore/FilePairingKeyStore.cs:304:            _logger.LogInformation(
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:45:    private readonly ILogger<StoreAtStartup> _logger;
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:52:    public StoreAtStartup(IPairingKeyStore store, ILogger<StoreAtStartup> logger)
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:55:        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:81:            if (_logger.IsEnabled(LogLevel.Information))
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:85:                    _logger.LogInformation(
    Jellyfin.Plugin.ServerPairing/KeyStore/StoreAtStartup.cs:95:            _logger.LogError(fault, "The key store could not be read at startup, so what it holds is unknown and no pairing will work. The server is left running.");
    Jellyfin.Plugin.ServerPairing/PluginServiceRegistrator.cs:103:                services.GetRequiredService<ILogger<FilePairingKeyStore>>()));

THIS BLOCK PASTED TWO TYPES AND THE COMMAND NOW RETURNS FOUR, and the sentence
under it counted the rows against the two. What is written above this reading
still holds - two rows of the table have a logger under them in the suite - but
the reading no longer says only that, and the difference is where a row is
missing rather than where one is unchecked.

`PeerPlaneController` writes the fault row and `StoreAtStartup` writes the
startup row, and a capturing logger sits under each of them in the suite: under
the fault row it asserts what the entry holds and that none of it reaches the
caller, and under the startup row it asserts one entry per pairing found, each
naming its identifier, and that none of the key material the case generated
appears in any of them. So two rows are checked and every other row in the table
is not.

`FilePairingKeyStore` writes the migration row, which the table above carries and
this sentence did not name.

`ConfigurationAtStartup` writes two entries that the table above carries no row
for at all: a refused setting at Error, and an acknowledged cleartext peer
address at Warning. That is a defect in the table rather than in this paragraph -
what this plugin writes and what this document says it writes have to be the same
list - and the table is issue #13's rather than this pass's, so it is named here
and not repaired here.

The test this section asks for is a different and larger thing, and it does not
exist. It drives a full enrolment, a rotation and a revocation at Debug against a
capturing logger and looks for the secrets the run generated, and there is still
no enrolment to drive and nothing that writes any of the other rows. Until it
exists, both lists above are a design statement for every entry but those two,
and nothing refuses a call site that violates them. This paragraph is the whole
of that disclosure and no later edit of this file turns it into a statement that
the logging has been checked.
