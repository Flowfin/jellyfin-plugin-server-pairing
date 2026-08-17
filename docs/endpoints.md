# The endpoints this plugin adds, and what authorises a request to one

Two things belong in this file. The table of endpoints and the authorization
each one requires is issue #27, and it is not here yet, because this plugin
adds no endpoint. What is here is the question underneath that table: how the
host authenticates a request that arrives from the dashboard, and what an
attacker on another origin can make a browser do with it. That is issue #53,
and it is written first deliberately, because the answer decides what the
endpoints are allowed to look like instead of describing them once they exist.

## How the claims about the host were produced

Every statement about Jellyfin below is read from the server's source at the
two supported tags. There is no Jellyfin checkout on the machine this was
written on, so the reading was done through the GitHub API rather than with
`git grep`. Both forms are given for each claim. The `git grep` form is the one
a reader with a checkout runs and it was not run here; the `gh api` form is
what produced the output that is pasted.

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

Identical at `v12.0-rc3`.

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

It does not hold the endpoint table. That is issue #27, and it needs endpoints
to exist first.

Nothing here is asserted by a test. Issue #53's third condition asks that a
test assert every state-changing endpoint refuses a request lacking whatever
this document names, and there is no controller in this repository to enumerate
or to drive. Until that test exists, everything above is a reading of somebody
else's source and this file is prose.

It says nothing about how the dashboard page treats a string that came from a
peer. That is a separate question with a separate failure, and it is issue #52.
