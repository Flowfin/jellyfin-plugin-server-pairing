#!/bin/sh
# Installs a built package into a Jellyfin server of the floor its manifest claims, and asks the
# server what it made of it.
#
# usage: sh .github/floor-install.sh <archive.zip> <manifest.yaml> [port]
#
# WHY A SERVER AND NOT A COMPILE. .github/abi-floor.sh already builds against the oldest package
# each manifest claims, which catches a call to a host API that does not exist there. It cannot
# catch what shipped in 0.1.0.0: the build compiled against the newest package on the 10.11 line,
# so every reference to a server assembly was stamped 10.11.9.0 while build.yaml promised
# targetAbi 10.11.0.0. The runtime binds a reference at the version the assembly names and takes
# no server assembly below it, so every 10.11 server under 10.11.9 offered the plugin on the
# strength of the declared floor and then refused every type in it. Nothing in this repository
# could say so, because nothing here installed the package it built.
#
# WHAT THE ANSWER IS. `Active` from GET /Plugins and nothing else. A plugin the server has
# admitted and constructed says Active; one whose assembly would not load says NotSupported, and
# one the server never saw at all is absent from the list. This refuses the last two by name,
# because absent and refused are different failures and collapsing them hides an install that
# silently put the files in the wrong place.
#
# WHAT IT DOES NOT PROVE. That the plugin does anything. Active is the server accepting the
# assembly and constructing the plugin, and no endpoint of the plugin is called here.
#
# NOR DOES IT JUDGE THE DECLARED FLOOR AGAINST THE SERVER, and that is measured rather than
# assumed. The same archive with targetAbi rewritten to 10.99.0.0 was installed on 10.11.0 by hand
# and answered Active, so a server does not read that field off a plugin somebody dropped into
# /config/plugins: it is what a catalogue filters on, not what the loader enforces. What this
# check reads is the assembly actually binding on that server, which is the defect 0.1.0.0
# shipped; a manifest promising a floor no catalogue would offer it at is a different question and
# .github/manifest-check.sh is where the manifest is judged against the build.
#
# It needs no display, no elevation and no trusted certificate: a container, plain HTTP on the
# loopback interface, and the container is removed when the run ends.

set -eu

archive=${1:?the package to install}
manifest=${2:?the manifest declaring the floor}
port=${3:-18096}

test -f "$archive" || { echo "FAIL  no such archive: $archive"; exit 1; }

# THE FLOOR IS READ FROM THE MANIFEST RATHER THAN WRITTEN HERE, so a floor that moves moves this
# with it and the check can never be judging a server the manifest does not promise. The reading
# itself is .github/floor-image.sh, which refuses a manifest that is not there and one whose
# targetAbi is not four parts, and which the leg that says why a floor cannot be installed on yet
# calls as well, so this directory holds one reading of that field rather than two that can
# disagree about which server a promise is about.
image_tag=$(sh .github/floor-image.sh "$manifest")
image="jellyfin/jellyfin:$image_tag"

name="floor-install-$image_tag-$$"
work=$(mktemp -d)
base="http://127.0.0.1:$port"

# The runner this is written for is Linux and both of the following are no-ops there. They are here
# so the command a person runs on Windows and the command the job runs are the same bytes, which is
# the arrangement .github/package-audit.sh is in and the reason this is a script rather than steps
# in a workflow file. Git Bash rewrites an argument that looks like an absolute path, which turns a
# container path into a Windows one, and hands docker a POSIX path it cannot resolve.
dk() {
    MSYS_NO_PATHCONV=1 docker "$@"
}

host_path() {
    cygpath --windows "$1" 2>/dev/null || printf '%s' "$1"
}

cleanup() {
    dk rm --force "$name" >/dev/null 2>&1 || true
    rm -rf "$work"
}
trap cleanup EXIT

echo "== install $archive on $image, the floor $manifest declares"

unzip -q "$archive" -d "$work/plugin"
ls -1 "$work/plugin"

dk rm --force "$name" >/dev/null 2>&1 || true
dk run --detach --name "$name" --publish "127.0.0.1:$port:8096" "$image" >/dev/null

# The server is started first because /config is a volume: it is populated when the container runs,
# and a copy made before that lands under a directory the running server never reads.
waited=0
while [ "$waited" -lt 60 ]; do
    if dk exec "$name" test -d /config/plugins 2>/dev/null; then
        break
    fi
    waited=$((waited + 1))
    sleep 1
done
dk exec "$name" mkdir -p /config/plugins/ServerPairing
dk cp "$(host_path "$work/plugin")/." "$name:/config/plugins/ServerPairing"

# Plugins are read at start, so the server has to come up again with the package already in place.
dk restart "$name" >/dev/null

# THREE ANSWERS IN A ROW RATHER THAN ONE. The port accepts while the server is still coming up, so
# one answer is not the server being ready and the next call is refused.
settled=0
waited=0
while [ "$waited" -lt 120 ]; do
    if curl --silent --fail --max-time 5 "$base/System/Info/Public" >"$work/info" 2>/dev/null; then
        settled=$((settled + 1))
        if [ "$settled" -ge 3 ]; then
            break
        fi
        sleep 1
    else
        settled=0
        sleep 2
    fi
    waited=$((waited + 1))
done
if [ "$settled" -lt 3 ]; then
    echo "FAIL  the server never settled; nothing was read"
    dk logs "$name" 2>&1 | tail -20
    exit 1
fi

echo "server version: $(sed -E 's/.*"Version":"([^"]*)".*/\1/' "$work/info")"

# The wizard has to be completed before anything can be asked of the server. The account exists for
# the length of this container and is reachable only from this loopback port.
password=$(head -c 24 /dev/urandom | od -An -tx1 | tr -d ' \n')
auth='MediaBrowser Client="floor-install", Device="floor-install", DeviceId="floor-install", Version="1.0.0.0"'

curl --silent --fail --max-time 30 -X POST "$base/Startup/Configuration" \
    -H 'Content-Type: application/json' \
    -d '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
curl --silent --fail --max-time 30 "$base/Startup/User" >/dev/null
curl --silent --fail --max-time 30 -X POST "$base/Startup/User" \
    -H 'Content-Type: application/json' \
    -d "{\"Name\":\"floorcheck\",\"Password\":\"$password\"}" >/dev/null
curl --silent --fail --max-time 30 -X POST "$base/Startup/RemoteAccess" \
    -H 'Content-Type: application/json' \
    -d '{"EnableRemoteAccess":true,"EnableAutomaticPortMapping":false}' >/dev/null
curl --silent --fail --max-time 30 -X POST "$base/Startup/Complete" -H 'Content-Length: 0' >/dev/null

curl --silent --fail --max-time 30 -X POST "$base/Users/AuthenticateByName" \
    -H 'Content-Type: application/json' \
    -H "Authorization: $auth" \
    -d "{\"Username\":\"floorcheck\",\"Pw\":\"$password\"}" >"$work/session"
token=$(sed -E 's/.*"AccessToken":"([^"]*)".*/\1/' "$work/session")
test -n "$token" || { echo "FAIL  the server issued no token; nothing was read"; exit 1; }

curl --silent --fail --max-time 30 "$base/Plugins" \
    -H "Authorization: $auth, Token=\"$token\"" >"$work/plugins"

# The name is the one the manifest declares, so a rename moves this with it rather than leaving the
# check reading a plugin that is no longer there and calling its absence a pass.
plugin_name=$(grep -E '^name:' "$manifest" | head -1 | sed -E 's/^name:[[:space:]]*"?([^"]*)"?[[:space:]]*$/\1/')

# One object per plugin on its own line, so the one this manifest names can be picked out without a
# JSON parser this image is not guaranteed to have.
tr '{' '\n' <"$work/plugins" | grep -F "\"Name\":\"$plugin_name\"" >"$work/entry" || true

if [ ! -s "$work/entry" ]; then
    echo "FAIL  the server lists no plugin named $plugin_name; the package was not seen at all"
    echo "      what the server does list:"
    tr '{' '\n' <"$work/plugins" | sed -nE 's/.*"Name":"([^"]*)".*"Status":"([^"]*)".*/        \1 \2/p'
    dk logs "$name" 2>&1 | grep -iE 'ServerPairing|Could not load file or assembly' | tail -10
    exit 1
fi

status=$(sed -E 's/.*"Status":"([^"]*)".*/\1/' "$work/entry")
version=$(sed -E 's/.*"Version":"([^"]*)".*/\1/' "$work/entry")

if [ "$status" != "Active" ]; then
    echo "FAIL  $plugin_name $version is $status on $image, which $manifest promises"
    dk logs "$name" 2>&1 | grep -iE 'ServerPairing|Could not load file or assembly|Failed to load assembly' | tail -10
    exit 1
fi

echo "ok    $plugin_name $version is Active on $image"
