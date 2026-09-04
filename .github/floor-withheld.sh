#!/bin/sh
# Says why the floor a manifest claims is not installed on, names the server image the install will
# use on the day it can be, and refuses the day that reason stops being true.
#
# usage: sh .github/floor-withheld.sh <manifest.yaml>
#
# WHAT IT IS FOR. .github/floor-install.sh installs the package a run built on a server of the floor
# its manifest declares and reads GET /Plugins back, which is what 0.1.0.0 shipped without.
# build.yaml gets that. build.net10.0.yaml cannot: it declares a floor of 12.0.0.0, the 12.0 server
# line has had no release, and the registry publishes no tag that 12.0.0.0 derives into. Read on
# 2026-09-04, jellyfin/jellyfin carries 12.0-rc1 through 12.0-rc7 on that line and no 12.0.0. A leg
# that reached for a candidate instead would prove that candidate and promise the line, which is
# the same shape of promise the install check exists to stop.
#
# WHY IT PRINTS RATHER THAN BEING SKIPPED. A job a workflow skips prints nothing, and the reason is
# the whole of what this leg has to say. A reader of a run that says nothing about the 12.0 floor
# cannot tell a promise being withheld from a promise nobody thought about.
#
# WHY IT REFUSES. A withholding nobody re-reads is a green tick that means nothing within a month.
# The condition that ends this one is the image tag existing, so this asks the registry for it and
# reds when it answers, naming what is then owed. That moves the work to the day it becomes
# buildable instead of leaving it for somebody to notice.
#
# THREE ANSWERS AND NOT TWO. The tag is there, the tag is not there, or the registry did not answer.
# The third is not the second. A check that reds when a network call fails says nothing about its
# subject, and one that reports a failed call as an absence turns an unanswered question into
# evidence. This prints that the question was NOT EVALUATED and passes, so the disclosure survives
# the run rather than being rounded to the nearest verdict.
#
# WHAT IT DOES NOT DO. It installs nothing and asks no server anything, so nothing here says the
# package would load on that line if it existed. It is the absence of that reading, written down.

set -eu

manifest=${1:?the manifest declaring the floor}

image_tag=$(sh .github/floor-image.sh "$manifest")
image="jellyfin/jellyfin:$image_tag"

# The registry is asked for the one tag, rather than for a listing filtered by name, so a candidate
# tag sharing the line's prefix cannot be mistaken for the release this waits on.
url="https://hub.docker.com/v2/repositories/jellyfin/jellyfin/tags/$image_tag"

echo "== the floor $manifest declares is not installed on, and this says why"
echo "   image the install will use on the day the line has a release: $image"

code=$(curl --silent --location --max-time 30 --output /dev/null --write-out '%{http_code}' "$url" 2>/dev/null) || code="000"

case "$code" in
    200)
        echo "FAIL  the registry now publishes $image, so the reason this floor is not installed on has expired."
        echo "      What is owed: a package built for the framework $manifest declares, and"
        echo "      sh .github/floor-install.sh <that package> $manifest, as a step beside the one that"
        echo "      already does it for the other manifest. Until then this run is red rather than quiet."
        exit 1
        ;;
    404)
        echo "ok    withheld: the registry publishes no $image, so there is no server of this floor to install on"
        exit 0
        ;;
    *)
        echo "      NOT EVALUATED: the registry answered '$code' rather than 200 or 404, so whether $image"
        echo "      exists was not read on this run. Nothing here says the floor is still withheld."
        exit 0
        ;;
esac
