# Threat model

This is the document the rest of the plan argues against. It comes before the
protocol and before any of the code, so almost everything it describes is a
design position rather than a measured property of something that runs. Where a
sentence describes a mechanism, the mechanism is owed by a milestone and the
milestone is named. Where a sentence describes the Jellyfin host, it carries the
command that produced it.

The single question this document exists to answer has its own section near the
end: what a stolen pairing secret reaches, and what it does not.

## How the claims about the host were produced

Every claim about how Jellyfin behaves was run against a checkout of the server,
at a tag, and the command is quoted where the claim is made. To reproduce any of
them:

    git clone https://github.com/jellyfin/jellyfin.git
    cd jellyfin

Two tags are used throughout, one from each of the two server lines this plugin
ships for:

    git rev-parse v10.11.9 v12.0-rc3
    e83a7e62f26443f7dd98f126d6955ac1af090125
    fc43f151a2418cc112e116050a99dd6318917ab0

Both lines are shipped for, which is issue #9. There is one manifest per line
and each carries its own floor and its own framework:

    grep -h '^targetAbi:\|^framework:' build.yaml build.net10.0.yaml
    targetAbi: "10.11.0.0"
    framework: "net9.0"
    targetAbi: "12.0.0.0"
    framework: "net10.0"

Those two tags are still only where the commands below were run, which is a
narrower thing than what the plugin supports. A reading taken at v10.11.9 says
nothing about a later release on the same line.

They were also not the newest tag on either line when this was written:

    gh api "repos/jellyfin/jellyfin/tags?per_page=100" --jq 'first(.[] | select(.name | startswith("v10.11"))) | .name'
    v10.11.11
    gh api "repos/jellyfin/jellyfin/tags?per_page=100" --jq 'first(.[] | select(.name | startswith("v12."))) | .name'
    v12.0-rc4

That takes nothing away from a measurement below, because each one names the tag
it was run at and those bytes stay fetchable. It does fix what each one is about:
v10.11.9 and v12.0-rc3, rather than whatever the two lines hold today. Re-running
one at a newer tag produces a new reading rather than confirming the old one.

A claim with no command beside it is a claim about this plugin's own design, not
about the host. Where something about the host is believed and was not measured,
the sentence says so in those words.

## What exists today

The tree holds a plugin skeleton and a test project. There is no pairing, no key
store, no endpoint and no dashboard page. So no adversary described below is
currently refused by anything, because there is nothing yet for them to reach.

That is not a caveat on one section, it is the state of the whole document, and
no later edit of this file turns it into a statement that any of this has been
checked. Each mechanism named below says which milestone owes it. When a
mechanism lands, the sentence naming it should be rewritten to carry the test
that proves it bites.

## What the answers settled

Issue #1 opened nine forks in the plan. The answers that reach this document are
recorded here rather than argued again, and every section below is written under
them. No section holds a fork open.

| Answer | What it settles here |
| --- | --- |
| Enrolment is static key pairs with a fingerprint the two operators compare | There is no transcribed secret at any point, so nothing that could be read over a shoulder or left in a clipboard exists to be stolen. The asset the answer creates instead is a long term private key that never leaves the server that generated it. Authenticity rests on somebody actually comparing two fingerprints, which is a property of the dashboard and not of the cryptography |
| The pairing is symmetric | Both servers hold the same rights and expose the same surface, so a hostile peer's reach is bounded by the mapping table rather than by which side started the pairing. Which side pulls or pushes is a setting of the sync plugin above this one |
| The transport is TLS with the peer certificate pinned at enrolment, and the exception is a setting | Bodies are not readable on the path on an installation nobody has changed. An operator can turn the requirement off, and the setting says what that costs at the point of ticking it. That case is named below as the exception it is, not as one of two equal branches |
| The mapping table holds opaque identifiers and a display cache | No peer username is at rest as truth on either server, so a reader of the table gets identifiers and a cache that may be discarded at any time |
| Revocation deletes what came from the peer | Revocation is an undo on the side that performs it rather than a stop, which is what bounds the limit against a hostile peer, and it is why the consumer contract requires provenance on every synced row |
| There is no unattended pairing mode | The human comparison is the only path to a pairing, so the trust root above has no weaker sibling to be modelled beside it. An operator who wants to pair from a script cannot |

Where a sentence below is true only because of one of these, it names the answer
rather than a decision number.

## The assets

| Asset | Where it lives | Who can read it on a normally configured server | Adversaries |
| --- | --- | --- | --- |
| The long term private key of this server | The key store, generated here and never transmitted anywhere in any form. M4 fixes the path and the format | The server process, and anything that can read that file as the user the server runs as | A5, A6 |
| Per pairing key material | The key store, a file this plugin owns, deliberately outside the plugin configuration directory. M4 fixes the path and the format | The server process, and anything that can read that file as the user the server runs as | A2, A5, A6 |
| The peer's public key, while an enrolment window is open | In flight between the two servers, and on both dashboards as the fingerprint the two operators compare. It is not secret and never was, and what it needs is integrity rather than confidentiality | Anyone on the path, and both administrators | A2, A7 |
| The user mapping table | Plugin state on each server. It holds opaque identifiers and a display cache, and no peer username as truth | Administrators through the dashboard, and anything that can read the file it is stored in | A1, A3, A4, A5, A6 |
| The peer address and identity | Plugin state on each server, entered by an administrator | Administrators through the dashboard | A1, A3, A6 |
| The audit trail | The server log directory. The fields are fixed by [what this plugin logs](logging.md) rather than restated here | Anyone who can read the server log, which in practice includes everyone who reads a forum thread an operator pasted it into | A5, A6 |

Every asset in that table has at least one adversary named against it, and every
adversary named appears in the list below with its reach and its limit.

### Why the key store is not the plugin configuration

The host writes a plugin's configuration object to disk with the XML serialiser:

    git grep -n "SerializeToFile" v10.11.9 v12.0-rc3 -- MediaBrowser.Common/Plugins/BasePluginOfT.cs
    v10.11.9:MediaBrowser.Common/Plugins/BasePluginOfT.cs:150:                XmlSerializer.SerializeToFile(config, ConfigurationFilePath);
    v12.0-rc3:MediaBrowser.Common/Plugins/BasePluginOfT.cs:150:                XmlSerializer.SerializeToFile(config, ConfigurationFilePath);

That path is fixed by the host, in the plugin configuration directory:

    git grep -n "ConfigurationFilePath =>" v10.11.9 v12.0-rc3 -- MediaBrowser.Common/Plugins/BasePluginOfT.cs
    v10.11.9:MediaBrowser.Common/Plugins/BasePluginOfT.cs:131:        public string ConfigurationFilePath => Path.Combine(ApplicationPaths.PluginConfigurationsPath, ConfigurationFileName);
    v12.0-rc3:MediaBrowser.Common/Plugins/BasePluginOfT.cs:131:        public string ConfigurationFilePath => Path.Combine(ApplicationPaths.PluginConfigurationsPath, ConfigurationFileName);

    git grep -n "PluginConfigurationsPath =>" v10.11.9 v12.0-rc3 -- Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs
    v10.11.9:Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs:61:        public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");
    v12.0-rc3:Emby.Server.Implementations/AppBase/BaseApplicationPaths.cs:61:        public string PluginConfigurationsPath => Path.Combine(PluginsPath, "configurations");

The same object is served back over the API. The controller that serves it
carries an authorization policy at the class:

    git grep -n "Policies.RequiresElevation" v10.11.9 v12.0-rc3 -- Jellyfin.Api/Controllers/PluginsController.cs
    v10.11.9:Jellyfin.Api/Controllers/PluginsController.cs:25:[Authorize(Policy = Policies.RequiresElevation)]
    v12.0-rc3:Jellyfin.Api/Controllers/PluginsController.cs:25:[Authorize(Policy = Policies.RequiresElevation)]

and that policy is the administrator role:

    git grep -n -A3 "Policies.RequiresElevation," v10.11.9 v12.0-rc3 -- Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs
    v10.11.9:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:90:                    Policies.RequiresElevation,
    v10.11.9:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs-91-                    policy => policy.AddAuthenticationSchemes(AuthenticationSchemes.CustomAuthentication)
    v10.11.9:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs-92-                        .RequireClaim(ClaimTypes.Role, UserRoles.Administrator));
    v12.0-rc3:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs:87:                    Policies.RequiresElevation,
    v12.0-rc3:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs-88-                    policy => policy.AddAuthenticationSchemes(AuthenticationSchemes.CustomAuthentication)
    v12.0-rc3:Jellyfin.Server/Extensions/ApiServiceCollectionExtensions.cs-89-                        .RequireClaim(ClaimTypes.Role, UserRoles.Administrator));

So a key on the configuration object would be plaintext XML on the filesystem
and would be readable by any administrator through an endpoint this plugin does
not control and cannot refuse. Keeping key material off that object is what M4
owes, and the assertion that makes it hold is the reflection test in issue #30.

### What the server's own backup does and does not take

The server can produce a full system backup. Its service does not mention
plugins at either tag:

    git grep -il "plugin" v10.11.9 v12.0-rc3 -- Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs ; echo "exit=$?"
    exit=1

What it does copy is the configuration directory, the root folder and named
subdirectories of the data path:

    git grep -n "CopyDirectory(Path.Combine\|EnumerateFiles(_applicationPaths" v10.11.9 -- Jellyfin.Server.Implementations/FullSystemBackup/BackupService.cs

The plugin configuration directory sits under the plugins path rather than the
configuration directory, shown by the two commands in the previous section, so
neither the plugin configuration nor a key store beside it is inside the
server's own backup archive.

Read that as a fact about one mechanism and not as reassurance. An operator
backing up the whole data directory with a filesystem tool takes everything,
which is what most operators do, and that is the case adversary A6 covers.

## The adversaries

Seven, each with a reach and a limit. The reach is what the adversary can do
without any further access. The limit is what they cannot do, and every limit
names either the mechanism that holds it or the milestone that owes one.

### A1, someone on the network path, passive

Reach on an installation nobody has changed is that a pairing exists between
these two addresses, and when it is used. TLS is required and the peer
certificate is pinned at enrolment, so the bodies are not readable on the path
and what is left over is traffic timing, which is out of scope.

Reach where an operator has turned the transport requirement off is the whole
pairing plane in the clear: the mapping table as it is transferred, the agreed
protocol version, and every other field the protocol defines. That is a setting
with a safe default rather than a second design, it has to name what it costs
where the operator ticks it, and the shape it takes is issue #50. An operator who
has not ticked it is in the paragraph above.

The limit in both cases is that no request this adversary observes can be
replayed to useful effect once the replay defences of M3 land, and that observing
a request does not yield the key that authenticated it, because the
authentication is a signature over a canonical form rather than a bearer token in
a header. Both halves are owed by M3 and neither is enforced today. The transport
requirement itself is owed by M3 as well and is not enforced today either, so the
first paragraph above describes a design position rather than a measured
property.

### A2, someone on the network path, active

Reach is everything A1 has, plus the ability to drop, delay, reorder and forge
requests in both directions. The forged request is the interesting one: this
adversary can present any pairing identifier and any body.

The limit is that a forged request has to carry a signature over the canonical
form, computed with a key this adversary does not have, and that a request
failing verification never reaches the deserialiser. Both are owed by issue #20.
Against enrolment specifically, the limit is the fingerprint comparison. This
adversary can substitute its own public key in either direction and there is no
transcribed code it has to guess to do it, so what catches it is two
administrators reading the fingerprints their own dashboards show and finding
them different. That is the one place in this design where the mechanism is a
person, and no test can prove a person read a screen. What the page can do is
make the comparison hard to skip and hard to get wrong, which is the ceremony in
issue #19 and the wording in issue #54.

### A3, the peer server, once compromised or once its operator turns hostile

Reach is everything a legitimate peer has, because that is what it is. It holds
a valid pairing key, so every request it sends verifies. It can ask for
everything the consumer contract exposes, as often as it likes, and it can lie
about everything it sends back.

The limit is the pairing boundary: a valid pairing authorises what the mapping
table says and nothing else, so a hostile peer reaches only the users an
administrator on this side mapped, and only the operations the contract defines.
It cannot reach an unmapped user, cannot create a mapping, and cannot read key
material, because no endpoint returns any. Those are owed by issue #36 for the
mapping being an administrator decision, and by issue #32 for key material never
leaving the process.

One part of that reach does not look like reach and is worth naming on its own. A
peer chooses every string it sends, its own display name and the names of its
users included, and those strings are rendered on an administrator's dashboard,
inside the web client, on this server's own origin, in a session that can do
anything. So a hostile peer has a path from a field in its own configuration into
script running with administrator privilege on this side, and it needs no defect
in the protocol to take it. Pairing with a server one does not fully trust is a
thing operators will do, which makes this ordinary rather than exotic. What
refuses it is how the page renders those strings, and that is issue #52. Nothing
in the tree refuses it today.

Revocation is the other half of the limit, and it is worth being exact about
which way it runs. On the side that performs it, revocation deletes what came
from the peer rather than stopping the transfer and leaving it in place. That
deletion is not something this plugin performs itself: it happens in the sync
plugin that stored the rows, which is why the consumer contract requires every
synced row to carry its provenance, and that requirement is issue #57.

It is not an undo in the other direction, and no wording should suggest that it
is. Whatever this server sent to a hostile peer before it was caught is on that
peer's disk, and nothing this plugin can send gets it back. What revocation
bounds is the future, plus this side's copy of the past.

### A4, a signed in user on either server who is not an administrator

Reach is the ordinary user API of their own server. If any pairing endpoint or
any dashboard endpoint of this plugin is reachable without the administrator
role, this adversary reaches it.

The limit is that every administrative surface this plugin adds requires the
administrator role, and that the pairing plane endpoints authenticate with the
pairing key rather than with a user session at all, so a user session is not a
credential on that plane. Issue #27 owes the authorization table and the
reflection assertion over it, and issue #53 owes the dashboard half. Neither
exists today, so today this limit is a design statement.

### A5, someone who has stolen a pairing secret from a config file, a backup or a log

Reach is whatever the secret authorises, and that is the subject of its own
section below.

The limit is that the secret is per pairing, so it authorises one pairing and
not the server, and that the log is not a place any of these secrets appears.
The logging half is fixed by [what this plugin logs](logging.md), including the
test that is owed to make the list hold rather than assert it. The config file
half is issue #30. The backup half is measured above for the server's own backup
service and is not true for a filesystem backup of the whole data directory.

### A6, someone who can read the server filesystem, including a backup archive

Reach is the key store file, the mapping table, the peer address and the log, in
whatever form they are at rest. On a normally configured server this is anything
running as the user the server runs as, plus anyone holding a copy of a
filesystem backup.

The limit is thin and it is worth being blunt about it. File permissions set by
issue #35 keep the store away from other users on the same host, and nothing
this plugin does keeps it away from something already running as the server
user. What protects the store at rest, and what does not, is issue #31, and this
document does not preempt its answer. Against a stolen backup, the honest
position today is that the store is as readable as the backup is, and the
mitigation is revocation after the fact rather than confidentiality of the file.

### A7, someone who reaches the pairing endpoints when there is nothing to reach

Two cases and they differ.

Reach with no pairing in existence is a set of endpoints that have no key to
verify against. The limit is that they answer nothing and refuse identically
whatever they are sent, so probing them tells an unauthenticated caller neither
whether this plugin is installed nor whether it has ever been paired. That is
owed by issue #28.

Reach while an enrolment window is open is larger, because the window is the one
moment the plugin accepts something from a party it has not yet authenticated.
There is no transcribed code to guess, because what the window accepts is a
public key. So the limit is not the entropy of a secret: it is that the window is
small, single use and fail closed, which is issue #18, and that a key arriving
inside it still has to survive a comparison performed by two people. Guessing is
replaced by grinding for a fingerprint collision, and the length that makes that
useless is pinned in issue #16 along with the reason it is enough. A number
written in this document instead would be the second copy that goes stale.

## The trust boundaries

Four, and each one is a place where something crosses from a party that is
trusted to one that is not.

Between the two servers. This is the boundary the protocol exists to defend.
Everything crossing it is authenticated with the pairing key, and nothing
crossing it is trusted before that verification succeeds. A peer is trusted for
exactly what the mapping table says and for nothing else, which makes A3's reach
a function of an administrator's decision rather than of the peer's assertion.

Between the plugin and the host. The plugin runs inside the server process with
the server's full trust, so this boundary protects the host from nothing. It
runs the other way: the host decides where the plugin's configuration lives, who
may read it and when it is written, as the commands above show, and the plugin
has to plan around those decisions rather than override them. The key store
exists because of this boundary.

Between the plugin and the sync plugins that consume it. A consumer is another
plugin in the same process, so it is not separated from this one by anything the
runtime enforces. The boundary is therefore a contract rather than a wall, and
what makes it worth anything is that the contract cannot express key material at
all. Issue #45 owes the proof that it cannot. Until then this boundary is an
intention.

Between the dashboard and the endpoints behind it. The dashboard is markup the
server serves to a browser, so nothing it does is trusted and every check it
appears to make is a convenience. The endpoint behind it repeats every check.
Peer controlled strings arriving at that page are hostile input, which is issue
#52. How the dashboard's own requests are authenticated, and what forgery is
possible against them, is issue #53.

## What a stolen pairing secret reaches, and what it does not

This is the question the document exists for, and the answer has to be narrow
enough to be worth having.

A pairing secret authorises requests on the pairing plane for one pairing. So
whoever holds it can act as that peer: they can make the requests the consumer
contract defines, for the users that the mapping table on the far side maps, and
they can do so for as long as the key is live.

What it does not reach:

It does not reach the Jellyfin server. A pairing secret is this plugin's own
credential and is never a Jellyfin API key, which is the subject of issue #11.
Nothing on the pairing plane can be presented to the server's own API, and
nothing on the server's own API accepts it.

That constraint exists because of what the alternative is worth to whoever takes
it. A Jellyfin API key is not scoped. The server's default authorization handler
succeeds the default requirement for any request carrying one, before any user,
permission or remote-access check runs, and it says so in a comment on line 53
at both supported lines:

```
git grep -n "Api keys are unrestricted" v10.11.9 v12.0-rc3 -- Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs
v10.11.9:Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:53:                // Api keys are unrestricted.
v12.0-rc3:Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs:53:                // Api keys are unrestricted.
```

Read without a Jellyfin checkout, the same bytes come through the API, and the
tags resolve to the commits a reader will get:

```
gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs?ref=v10.11.9" --jq '.content' | tr -d '\n' | base64 -d | grep -n "Api keys are unrestricted"
53:                // Api keys are unrestricted.
gh api "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Auth/DefaultAuthorizationPolicy/DefaultAuthorizationHandler.cs?ref=v12.0-rc3" --jq '.content' | tr -d '\n' | base64 -d | grep -n "Api keys are unrestricted"
53:                // Api keys are unrestricted.
gh api repos/jellyfin/jellyfin/git/ref/tags/v10.11.9 --jq '.object.sha'
e83a7e62f26443f7dd98f126d6955ac1af090125
gh api repos/jellyfin/jellyfin/git/ref/tags/v12.0-rc3 --jq '.object.sha'
fc43f151a2418cc112e116050a99dd6318917ab0
```

So a credential of that kind, taken from a configuration file, a backup or a
support log, is full administrative control of the server that issued it. The
prior art uses exactly that as the inter-server credential, which is why the
first constraint on this protocol was fixed before any of it was designed. The
second half of the constraint runs the other way: this plugin never creates,
reads, stores or asks an operator for a Jellyfin API key either, because a
plugin holding one has widened the blast radius of its own key store.

The `git grep` form is what somebody with a Jellyfin checkout runs. It was not
run on the machine that added this paragraph, which has no such checkout, and
the API form under it is what was actually executed there. The two read the same
blobs and the tag shas are printed so a reader can tell.

It does not reach a second pairing. Keys are per pairing, so a server paired
with three peers holds three keys and a stolen one is worth one peer's access.
This is a property of the key store's shape, owed by issue #30.

It does not reach an unmapped user. The mapping table is the authorisation, and
it is an administrator's decision rather than anything the peer asserts, which
is issue #36.

It does not reach the key store. No endpoint returns key material in any
encoding to anybody, which is issue #32, so holding one pairing's key does not
lead to another's.

It does not survive revocation. Revocation is unilateral, immediate and
terminal, which is issue #24, and on the side that performs it what came from the
peer is deleted rather than left in place. What no revocation reaches is what
this server already sent the other way, which is on the peer's disk.

It does not reach the past. A captured request cannot be replayed outside a
small timestamp window and cannot be replayed twice inside one, which is issue
#21.

Every one of those six is owed by a milestone and none of them is enforced
today. That is the state of the tree rather than a weakness of the model, and
the model is written this way so that each sentence has somewhere to be proved
when the code arrives.

## The refusal path and the oracle question

A refusal that says why it refused tells an unauthenticated caller something.
The general position is issue #28: refusals on the pairing plane are one shape,
one timing class and one category vocabulary, and a caller naming an unknown
pairing learns the same as one naming a known pairing with a bad signature.

One distinction is deliberately kept, and it is worth arguing rather than
assuming. A refusal caused by clock skew is reported as a clock refusal and not
as a signature failure. That does hand a caller one bit, which is whether their
timestamp was inside this server's window, and the bit is worth giving away for
two reasons. The window is a documented constant rather than a secret, so a
caller can learn the same bit by reading the specification. And the alternative
costs an operator an evening of debugging a signature error that is really a
clock error, on two home servers where one of them has no time source. Issue #26
owns the skew policy and the test for that distinction.

An unauthenticated caller does not get that distinction, because a request that
fails signature verification is refused before its timestamp is considered. So
the clock refusal is only ever reported to a caller that already holds the key.

## Out of scope

Three things, said plainly rather than left silent.

A compromised host operating system. If the machine is owned, the server process
is owned, and everything the plugin holds is readable. Nothing in a plugin
defends against this and pretending otherwise would be worse than saying so.

A malicious administrator on either side. The administrator is the root of trust
in this design. They enter the peer address, they perform the comparison that
makes enrolment mean anything, and they decide the mapping table. An
administrator who wants to send their users' data to a server they control does
not need this plugin's help, and no mechanism here would stop them.

Traffic analysis. That two servers talk to each other, how often, and how much
they send stays visible to the network path with the required TLS in place. It is
what is left when the bodies are not readable, and this design does not attempt
to hide it.

## What this document does not yet do

It does not cover the migration path, because there is nothing to migrate from
yet. M8 is where the model gets its section on what unpairing and uninstalling
leave behind.

It does not state a single cryptographic parameter. Every one of them belongs in
one place, which is issue #16, and a copy here would be the copy that goes stale.

It does not carry a personal data statement. What fields cross the wire, where
they come to rest and how an operator removes them is issue #14, which needs the
protocol document first.
