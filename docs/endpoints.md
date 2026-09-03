# The endpoints this plugin adds, and what authorises a request to one

Two things belong in this file. The table of endpoints and the authorization
each one requires is issue #27, and it is the next section. Underneath that
table sits the question of how the host authenticates a request that arrives
from the dashboard, and what an attacker on another origin can make a browser
do with it. That is issue #53, and it was written first deliberately, because
the answer decides what the endpoints are allowed to look like instead of
describing them once they exist.

## The table

| Action | Method | Path | Plane | Host authorization | What decides the request |
| --- | --- | --- | --- | --- | --- |
| `PeerPlaneController.Hello` | `POST` | `/ServerPairing/hello` | peer | `anonymous` | the pairing signature |
| `PeerPlaneController.Confirm` | `POST` | `/ServerPairing/confirm` | peer | `anonymous` | the pairing signature |
| `PeerPlaneController.Rotate` | `POST` | `/ServerPairing/rotate` | peer | `anonymous` | the pairing signature |
| `PeerPlaneController.Revoke` | `POST` | `/ServerPairing/revoke` | peer | `anonymous` | the pairing signature |
| `PeerPlaneController.Exchange` | `POST` | `/ServerPairing/exchange` | peer | `anonymous` | the pairing signature |
| `PeerPlaneController.Unpair` | `POST` | `/ServerPairing/unpair` | peer | `anonymous` | the pairing signature |
| `AdministrativePlaneController.Pairings` | `GET` | `/ServerPairing/Administration/pairings` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.Diagnostics` | `GET` | `/ServerPairing/Administration/diagnostics` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.Windows` | `GET` | `/ServerPairing/Administration/windows` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.HeldAbout` | `GET` | `/ServerPairing/Administration/users/{localUserId}` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.Mappings` | `GET` | `/ServerPairing/Administration/pairings/{pairingId}/mappings` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.Unmap` | `DELETE` | `/ServerPairing/Administration/pairings/{pairingId}/mappings/{localUserId}` | administrative | `elevation` | the host's elevation policy |
| `AdministrativePlaneController.Wording` | `GET` | `/ServerPairing/Administration/wording` | administrative | `elevation` | the host's elevation policy |

Thirteen rows. Six are the peer plane, which is the paths the specification fixes.
THIS PARAGRAPH SAID THE ADMINISTRATIVE PLANE HAS NO ENDPOINT AND THEREFORE NO
ROW; it has seven. The first is issue #289, and the plane is what that issue is
about rather than the action on it: what it answers is the identifiers of the
pairings this server holds a key for, and nothing else. The second is the
diagnostics read, which is issue #51, and what it answers is how many requests
the peer plane has refused since this server started and why, as numbers over two
enumerations. It names no pairing, no address and no person, and the payload has
the same shape on a server that has never been paired as on one that has.

The third is the open enrolment windows, which is the seventh property of issue
#18: while a window is open the plugin says so, because a window an operator
opened and forgot is the failure the other six properties exist against. It reads
the pairing record store and not `EnrolmentWindow`, which is the surface the
decision under the local events table in [`protocol.md`](protocol.md) settled on:
a pairing in `Offered` is written as a record, so what is half-built and what is
finished are one surface rather than two. WHAT WRITES THAT RECORD IS NOT BUILT.
Nothing joins the window to the state machine, and the state machine is not
registered on a server at all, so this action answers an empty list on every
server today. That is stated here and at the action rather than left for an
operator to read an empty answer as a server with no window open.

The fourth is what this plugin holds about one local user, which is the report
half of issue #60: every mapping held for that user across every pairing, each
with the pairing it belongs to, the opaque peer identifier and the cached peer
display name, said in the output to be a cache by the name of the member that
carries it. It walks the pairing record store and not the key store, because a
pairing that has not finished enrolling holds no key and may hold a mapping, and
a report walked over the key store would answer nothing for such a user and look
exactly like a report for a user mapped nowhere; the case named after that walk
in the suite refuses the narrower one. It writes one audit entry naming who asked
and neither user, which is the row [`logging.md`](logging.md) gives it, and it
changes nothing. THE REMOVAL HALF OF #60 IS NOT BUILT. Nothing on this plane
removes a user from every pairing, because that removal has to raise the consumer
event once per pairing and there is no contract to raise it through until issue
#43 lands; [`data.md`](data.md) says what it will cover and that it does not
exist.

The fifth and sixth are a pairing's mapping table, which is the half of issue
#40 that needs nothing from the peer. The listing answers every mapping under
one pairing with the local user as the host names them, the opaque peer
identifier and the cached peer display name, and beside each name the value a
page shows: the name where there is one and the identifier where there is not,
so an unset cache is never an empty cell. A mapping whose local user this server
no longer has is listed rather than dropped and is marked as such by a member
that is false for it and for nothing else, which is the third rule of issue #37
reported at the listing rather than left to a page to infer from an empty name.
It answers the local users who are
unmapped under that pairing too, read from the host's user manager at the
moment of the call and kept nowhere, so an operator wondering why somebody is
not syncing does not work it out by subtraction. A pairing nothing is held for
is not found rather than listed empty, because an empty table with every user
unmapped invites mapping under a pairing that does not exist. The listing writes
no audit entry: it is the dashboard reading the table, which
[`logging.md`](logging.md) names as where an operator entitled to the answer
reads who is mapped to whom. The removal takes one mapping under one pairing
out of the table at an administrator's decision, goes through the one type every
mapping change passes so the audit entry naming who removed it cannot be
skipped, and refuses a principal naming nobody before the store is touched. IT
IS THE FIRST ACTION ON THIS PLANE THAT CHANGES STATE, and it is not the removal
half of #60: that one takes a person out of every pairing and tells every
consumer, and this one takes one row out of one table and tells nobody, because
nothing has moved under a mapping yet and the wording an operator confirms says
what already arrived stays where it arrived. NOTHING ADDS A MAPPING. Adding one
means choosing a peer user from a list fetched from the peer, which is a
protocol operation this plugin does not make yet, so the table these two actions
read and shrink is empty on every server until that lands.

The seventh is the wording an operator reads on the page, served out of the two
registers that hold every such sentence. The page shows the sentence about
removing a mapping in the same view as the action that removes one, and it may
not carry the sentence itself: `CeremonyWordingTests` refuses markup carrying
one, because a pasted copy drifts from the register the operator guide is held
equal to. So the page asks this action on every show and renders what it
answered, and the answer is built by reflection over the registers rather than
by a list, so a sentence added to a register reaches the page without the
action moving. It reads no store and changes nothing.

That second row is narrower than the issue it comes from. A state per pairing,
the protocol version each side speaks and a last error per pairing are all things
#51 asks the payload to carry, and none of them has anything in this tree that
produces one. `DiagnosticsAnswer` names each of them with what has to exist
first, so the payload's silence is written down where somebody reading the
payload will find it rather than being inferred from what is missing.

The rest of the administrative actions are still issues rather than actions, and
a row for one of them would describe something no request can reach: OPENING an
enrolment window is #18, which the row above does not do - it says whether one is
open and provides no way to open one - confirming a ceremony is #19, revoking is
#24, adding a mapping is the rest of #40 behind a peer user list nothing fetches
yet, removing what is held about one user is the other half of #60 and the
pairing states the page renders are #49. Each of them lands
on the plane above rather than bringing a controller of its own.

### What the two authorization words mean

`anonymous` is the host being asked for nothing. The action resolves to
`AllowAnonymous` and carries no `Authorize`. It is not the absence of a
credential requirement, it is the absence of a *host* credential requirement: a
peer holds this plugin's own pairing key and none of the host's, so a request
here carries no token the server's authentication would recognise, and
requiring one would refuse every peer before this plugin saw it. What decides
such a request is the pairing signature, checked in `PeerPlane` and nowhere
else.

`elevation` is the action carrying `Authorize` naming the host's
`RequiresElevation` policy, which is the constant read out of the server source
in the section below. SIX ROWS CARRY IT AND THIS SENTENCE SAID FOUR DID, having
said three, two and one before that; the count moves with the plane and is derived
by the suite rather than trusted from here. What
decides such a request is the host's elevation policy together with the
endpoint's own repeat of the check, which is the third bullet under `What this
plugin does about it`.

The policy is declared on the controller rather than per action, so an action
added to that class without an attribute inherits it. That is the direction that
fails safe, and it is not what makes the row true: what the suite compares
against is the host's own action discovery, so an action that ended up with the
server's default rather than with this policy fails whatever the class says.

WHETHER THE HOST THEN ENFORCES THE POLICY IS NOT MEASURED BY ANYTHING HERE. The
refusal is the server's authorization middleware rather than this plugin's code,
so reaching it means standing that pipeline up, and a case that did would be
judging the framework. `docs/testing.md` refuses the neighbouring apparatus, two
real servers and a browser, and does not name this case, so this paragraph is
the argument rather than a citation of one. The declaration is asserted and the
enforcement is the reading of the server's source below, which is somebody
else's tree read at two tags and not a measurement of this one.

The pairing between the columns is fixed rather than chosen per row: `peer` goes
with `anonymous` and with the pairing signature, `administrative` goes with
`elevation` and with the host's elevation policy. A row that spells one half and
not the other is refused by the suite, because a row is the only place the two
could be made to disagree.

### What binds the table to the tree

`EndpointAuthorizationTableTests` in the test project reads the rows out of this
file and compares them against what the host would serve. An endpoint the host
serves and this table does not name fails the suite, and so does a row naming an
endpoint the host does not serve, a row naming the wrong method or path, and a
row naming an authorization the action does not carry. This table is therefore
not a description that can drift; it is the declaration a change is judged
against.

What produces the served side is the host's own action discovery rather than a
reading of the attributes, and the difference is not academic. **A public
instance method declared on a controller is an action whether or not it carries
an HTTP attribute.** Counting `HttpPost` attributes counts the endpoints
somebody meant to write; asking the discovery counts the endpoints a request can
reach. The two disagreed in this tree. `PeerPlaneController.Arriving` is public
because the suite drives it directly, and until it was marked `NonAction` the
discovery returned it as a sixth action: routed at `/ServerPairing`, under no
HTTP method constraint, on a class carrying `AllowAnonymous`. It is not `Serve`,
so it is not the refusal the five named paths give. That is the failure issue
#27 names, it was found by asking the discovery rather than by reading the
source, and this table is what stops the next one.

What that reading does not carry is what a request to it would have received.
The discovery was asked in the test process, not on a running server, so the
status and the body a real request would have got are not measured here and no
claim is made about them. What is measured is that the host would have routed
one there at all, with nothing of the host's asked for.

## How the claims about the host were produced

Every statement about Jellyfin below is read from the server's source at the
two supported tags. There is no Jellyfin checkout on the machine this was
written on, so the reading was done through the GitHub API rather than with
`git grep`. Both forms are given for each claim. The `git grep` form is the one
a reader with a checkout runs and it was not run here; the `gh api` form is
what produced the output that is pasted.

THE `git grep` FORMS HAVE SINCE BEEN RUN AND THE PARAGRAPH ABOVE IS UNCHANGED,
because it says how the pasted output was produced and that is still how it was
produced. Every reading in this file that names a tag was run again in a checkout
of `jellyfin/jellyfin` holding both of them, and each agrees with the `gh api`
form beside it. That population is the one `.github/reading-check.sh` declares
out of its own walk, so nothing runs these on a pull request and this paragraph
is a reading somebody took rather than a guard. It was taken on issue #80, where
what it found in a neighbouring document is written down.

The two tags resolve to these commits, so a reader can tell which blobs the
quotations are correct about:

```
gh api repos/jellyfin/jellyfin/git/ref/tags/v10.11.9 --jq '.object.sha'
e83a7e62f26443f7dd98f126d6955ac1af090125
gh api repos/jellyfin/jellyfin/git/ref/tags/v12.0-rc3 --jq '.object.sha'
fc43f151a2418cc112e116050a99dd6318917ab0
```

Nothing below was observed on a running server. Every claim is a claim about
source at those two commits.

## The mechanism

A request is authenticated by a token. `CustomAuthenticationHandler` asks the
authentication service for the request's authorization information, and where
that carries no token it returns no result rather than an identity. Where it
carries one, the handler builds a claims principal and marks it as an
administrator if the token is an API key or if the user behind it holds the
administrator permission.

The file is byte for byte the same at both tags:

```
git diff v10.11.9 v12.0-rc3 -- Jellyfin.Api/Auth/CustomAuthenticationHandler.cs

for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Api/Auth/CustomAuthenticationHandler.cs?ref=$t" | md5sum; done
8be5684d28045f841027fd9c61be26cf *-
8be5684d28045f841027fd9c61be26cf *-
```

What an API key reaches once it has been accepted is not restated here. It is
in [`threat-model.md`](threat-model.md), under the constraint that the pairing
credential is this plugin's own, with the line of server source that says so.

The policy name for an administrative endpoint comes from the host rather than
from a string written in this repository:

```
git grep -n "RequiresElevation = " v10.11.9 v12.0-rc3 -- MediaBrowser.Common/Api/Policies.cs

gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/MediaBrowser.Common/Api/Policies.cs?ref=v10.11.9" \
  | grep -n 'RequiresElevation = '
16:    public const string RequiresElevation = "RequiresElevation";
41:    public const string LocalAccessOrRequiresElevation = "LocalAccessOrRequiresElevation";
```

Identical at `v12.0-rc3`, which the `git grep` form above answers for both tags
rather than leaving to a sentence:

```
git grep -n "RequiresElevation = " v10.11.9 v12.0-rc3 -- MediaBrowser.Common/Api/Policies.cs
v10.11.9:MediaBrowser.Common/Api/Policies.cs:16:    public const string RequiresElevation = "RequiresElevation";
v10.11.9:MediaBrowser.Common/Api/Policies.cs:41:    public const string LocalAccessOrRequiresElevation = "LocalAccessOrRequiresElevation";
v12.0-rc3:MediaBrowser.Common/Api/Policies.cs:16:    public const string RequiresElevation = "RequiresElevation";
v12.0-rc3:MediaBrowser.Common/Api/Policies.cs:41:    public const string LocalAccessOrRequiresElevation = "LocalAccessOrRequiresElevation";
```

## Where the token may come from, and where the two lines differ

`AuthorizationContext` is the one place that turns a request into a token. It
reads six places, four of them behind a server configuration flag:

```
git grep -nE 'headers\["|queryString\["|Headers\[HeaderNames.Authorization\]|Headers\["X-Emby-Authorization"\]' \
  v10.11.9 -- Jellyfin.Server.Implementations/Security/AuthorizationContext.cs

gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs?ref=v10.11.9" \
  | grep -nE 'headers\["|queryString\["|Headers\[HeaderNames.Authorization\]|Headers\["X-Emby-Authorization"\]|EnableLegacyAuthorization'
93:            if (_configurationManager.Configuration.EnableLegacyAuthorization && string.IsNullOrEmpty(token))
95:                token = headers["X-Emby-Token"];
98:            if (_configurationManager.Configuration.EnableLegacyAuthorization && string.IsNullOrEmpty(token))
100:                token = headers["X-MediaBrowser-Token"];
105:                token = queryString["ApiKey"];
108:            if (_configurationManager.Configuration.EnableLegacyAuthorization && string.IsNullOrEmpty(token))
110:                token = queryString["api_key"];
231:            var auth = httpReq.Headers[HeaderNames.Authorization];
233:            if (_configurationManager.Configuration.EnableLegacyAuthorization && string.IsNullOrEmpty(auth))
235:                auth = httpReq.Headers["X-Emby-Authorization"];
259:            validName = validName || (_configurationManager.Configuration.EnableLegacyAuthorization && name.Equals("Emby", StringComparison.OrdinalIgnoreCase));
```

The same command at `v12.0-rc3` prints the same eleven lines at the same line
numbers. The two files differ by one character, which is whitespace inside a
range expression at line 305 and has nothing to do with any of this:

```
for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs?ref=$t" > "ac-$t.cs"; done
diff "ac-v10.11.9.cs" "ac-v12.0-rc3.cs"
305c305
<                     key = authorizationHeader[start.. i].Trim().ToString();
---
>                     key = authorizationHeader[start..i].Trim().ToString();
```

So the code is the same on both lines. What differs is the default value of the
flag that four of the six routes are behind:

```
git grep -n "EnableLegacyAuthorization" v10.11.9 v12.0-rc3 -- MediaBrowser.Model/Configuration/ServerConfiguration.cs

for t in v10.11.9 v12.0-rc3; do echo "-- $t --"; gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Configuration/ServerConfiguration.cs?ref=$t" \
  | grep -n 'EnableLegacyAuthorization'; done
-- v10.11.9 --
290:    public bool EnableLegacyAuthorization { get; set; } = true;
-- v12.0-rc3 --
290:    public bool EnableLegacyAuthorization { get; set; }
```

At `v10.11.9` the property is initialised to true. At `v12.0-rc3` it has no
initialiser, so it is false unless an operator turns it on. That is the answer
to why this file states the mechanism per supported server line rather than
once.

On a 10.11 server with the shipped default, a token is accepted from any of:

| Route | Accepted on 10.11 default | Accepted on 12.0 default |
| --- | --- | --- |
| `Authorization: MediaBrowser Token="..."` | yes | yes |
| `?ApiKey=` in the query string | yes | yes |
| `Authorization: Emby Token="..."` | yes | no |
| `X-Emby-Authorization` header | yes | no |
| `X-Emby-Token` header | yes | no |
| `X-MediaBrowser-Token` header | yes | no |
| `?api_key=` in the query string | yes | no |

Seven rows out of six places. The `Authorization` header is one place and is
listed twice, because the scheme name is checked separately from the header:
`MediaBrowser` is accepted whatever the flag says and `Emby` only when it is on,
which is line 259 above.

The row worth noticing is the second, and it is one of two rather than one.
`?ApiKey=` at line 105 and the `Authorization` header at line 231 are the two
places the flag does not guard, which is the same thing the two `yes | yes` rows
say. So a credential in a query string is accepted on both lines and under both
settings, and that is the one of the two worth a rule here. A query string is
written to access logs, to proxy logs, to browser history and, on an outbound
link, to a referrer header, so this is a route this plugin does not use for a
credential of its own and does not offer as a convenience.

## Cross-origin forgery, stated as a claim with what it rests on

The claim: a page on another origin cannot cause an authenticated request to a
Jellyfin endpoint, because the host attaches no credential to a request by
itself. There is no cookie, no HTTP authentication realm and no client
certificate in the path above. Every one of the seven routes requires the
caller to put the token into the request, and a page on another origin does not
have the token.

That is the whole of why classic cross-site request forgery does not apply
here, and it is a property of the host rather than of anything this plugin
does.

What the claim rests on, and its bound. Nothing in the file that resolves a
token reads a cookie:

```
git grep -in "cookie" v10.11.9 v12.0-rc3 -- Jellyfin.Server.Implementations/Security/AuthorizationContext.cs

for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Server.Implementations/Security/AuthorizationContext.cs?ref=$t" \
  | grep -in "cookie"; echo "exit=$?"; done
exit=1
exit=1
```

and nothing in the request pipeline that composes the server's middleware reads
one either:

```
for t in v10.11.9 v12.0-rc3; do gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Startup.cs?ref=$t" \
  | grep -in "cookie\|antiforgery"; echo "exit=$?"; done
exit=1
exit=1
```

Two greps over two files each is what was run, and it is not a proof that no
part of the server anywhere authenticates by cookie. A wider search agrees and
carries its own caveat, because the code search index is built from the default
branch rather than from a tag:

```
gh api "search/code?q=repo:jellyfin/jellyfin+Cookies+path:Jellyfin.Api" --jq '.total_count'
0
gh api "search/code?q=repo:jellyfin/jellyfin+Cookie+path:Jellyfin.Server.Implementations/Security" --jq '.total_count'
0
```

So: verified for the token resolution path and the middleware pipeline at both
tags, and not evaluated for the rest of the server.

## Preflight is not a barrier on this host

Issue #53 asks that no state-changing operation be reachable by a request shape
a browser will send across origins without preflight. On this host that framing
protects nothing, and it is better to say so than to write a rule that reads as
a defence and is not one.

The server answers every preflight affirmatively by default:

```
git grep -n "AllowAnyOrigin\|AllowAnyHeader\|AllowAnyMethod\|AllowCredentials" \
  v10.11.9 v12.0-rc3 -- Jellyfin.Server/Configuration/CorsPolicyProvider.cs

gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/Jellyfin.Server/Configuration/CorsPolicyProvider.cs?ref=v10.11.9" \
  | sed -n '28,44p'
            var corsHosts = _serverConfigurationManager.Configuration.CorsHosts;
            var builder = new CorsPolicyBuilder()
                .AllowAnyMethod()
                .AllowAnyHeader();

            // No hosts configured or only default configured.
            if (corsHosts.Length == 0
                || (corsHosts.Length == 1
                    && string.Equals(corsHosts[0], CorsConstants.AnyOrigin, StringComparison.Ordinal)))
            {
                builder.AllowAnyOrigin();
            }
            else
            {
                builder.WithOrigins(corsHosts)
                    .AllowCredentials();
            }
```

The file is identical at `v12.0-rc3`, and the shipped configuration takes the
first branch:

```
for t in v10.11.9 v12.0-rc3; do echo "-- $t --"; gh api -H "Accept: application/vnd.github.raw" \
  "repos/jellyfin/jellyfin/contents/MediaBrowser.Model/Configuration/ServerConfiguration.cs?ref=$t" \
  | grep -n 'CorsHosts'; done
-- v10.11.9 --
236:    public string[] CorsHosts { get; set; } = new[] { "*" };
-- v12.0-rc3 --
236:    public string[] CorsHosts { get; set; } = new[] { "*" };
```

`AllowAnyMethod` and `AllowAnyHeader` are set before the branch, so they apply
whichever way it goes. With the default hosts the origin is any origin and
credentials are not allowed. So a preflight from an attacker's page asking to
send `PUT` with an `Authorization` header is answered yes, and requiring a
preflight is not a thing an endpoint here can rely on. What stops the attacker
is the previous section: the request goes out with no token on it.

Two consequences follow and they belong to the endpoints when they are written.

A response from an endpoint that needs no authentication is readable by any
origin, because `AllowAnyOrigin` is the default and a browser will hand the
body to the calling script. The enrolment responder is the one endpoint in the
plan that answers a stranger, so whatever it returns has to be safe to publish,
not merely safe to send to the peer who asked.

Where an operator has set `CorsHosts` to real hostnames the policy changes
shape: those origins get `AllowCredentials` and every other origin gets
nothing. That is a configuration this plugin neither reads nor controls, and no
endpoint here may behave differently depending on which branch is live.

## What this plugin does about it

- No credential of this plugin's own is ever accepted from a query string, on
  either server line, for the reason under the table above.
- This plugin adds no cookie and no other ambient credential. Adding one would
  create the forgery surface that the section above says the host does not
  have.
- Every administrative endpoint carries the host's elevation policy, and the
  endpoint repeats the check rather than trusting the dashboard to have made
  it.
- The enrolment responder is written on the assumption that its response is
  world-readable.

## What this document does not yet do

The table is asserted by a test. The rest of this file is not, and that half
does not become smaller for the table arriving: every statement about Jellyfin
above is a reading of somebody else's source at two tags, made through the
GitHub API and not on a running server, and no test drives any of it. That half
is prose and stays prose.

Issue #53's third condition asks that a test assert every state-changing
endpoint refuses a request lacking whatever this document names. It is not met.
THIS PARAGRAPH SAID THE SET IT QUANTIFIES OVER WAS EMPTY BECAUSE EVERY
ADMINISTRATIVE ACTION WAS A READ, AND ONE IS NOT NOW: the removal of a mapping
changes state, so the set has one member. What the suite asserts about it is
that a principal naming no administrator is refused before the store is touched,
and that is this plugin's own check rather than the host's credential. Whether
the host refuses a request lacking its token is the elevation policy, which
nothing here measures for the reason under the table, so the condition has a
subject now and is still not met.
`PeerPlaneTests` does assert that a request without a verifying signature is
refused and that its body is not handed on, but every answer this plane gives
today is the same refusal whatever arrives, so that assertion cannot be
distinguished from an assertion that everything is refused. Until a request can
succeed there is nothing for the condition to bite on, and #53 carries it.

It says nothing about how the dashboard page treats a string that came from a
peer. That is a separate question with a separate failure, and it is issue #52.
