#!/usr/bin/env bash
# Takes the ModDB screenshots: runs the Gallery tests on the test box and copies the
# PNGs back into screenshots/.
#
#   bash scripts/screenshots.sh [user@host] [--force]
#
# Shot.Take copies the newest new file from a screenshots folder every slot on the box
# shares, so a second client taking pictures at the same time can swap them. This refuses
# to run while another client is up, unless --force.
set -euo pipefail

host="dizzyd@vsclient.home"
force=0
for arg in "$@"; do
    case "$arg" in
        --force) force=1 ;;
        *) host="$arg" ;;
    esac
done

repo="$(cd "$(dirname "$0")/.." && pwd)"
vstestkit="${VSTESTKIT:-$repo/../vstestkit}"
out="$repo/screenshots"
export VINTAGE_STORY="${VINTAGE_STORY:-$(ls -d ~/.cairn/games/1.22* | sort -V | tail -1)}"

clients="$(ssh "$host" 'cd vstestkit-patternbook 2>/dev/null || cd vstestkit; bash scripts/slots' | sed -n 's/^clients \([0-9]*\)\/.*/\1/p')"
if [ "${clients:-0}" != "0" ] && [ "$force" = "0" ]; then
    echo "$clients other client(s) running on $host; their screenshots could swap with these." >&2
    echo "Wait for them, or pass --force." >&2
    exit 1
fi

(cd "$vstestkit" && bash scripts/sync-linux.sh "$host" --mod "$repo/patternbook")

# Keep whatever shots were taken even if one fails, then report the failure
status=0
ssh "$host" 'rm -f ~/.cairn/games/*/patternbook-gallery-*.png
    cd vstestkit-patternbook && bash scripts/run.sh mods/patternbook/tests --mod mods/patternbook/patternbook --client --filter Gallery' || status=$?

mkdir -p "$out"
rm -f "$out"/patternbook-gallery-*.png "$out"/modicon.png
scp -q "$host:.cairn/games/*/patternbook-gallery-*.png" "$out/" || true
ssh "$host" 'rm -f ~/.cairn/games/*/patternbook-gallery-*.png'

# The mod icon: a 480x480 square from the icon shot, centred across the picker and from the
# top of the frame, which keeps the hotbar out of it
icon="$out/patternbook-gallery-icon-full.png"
if [ -f "$icon" ]; then
    sips -c 480 480 --cropOffset 0 240 "$icon" --out "$out/modicon.png" >/dev/null
fi

ls -1 "$out"
exit "$status"
