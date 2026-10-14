#!/bin/bash
# Uploads the mod installed in Mods to its Steam Workshop item, with the
# newest changelog entry as the change note:
#
#   Tools/Package/upload.sh [--dry-run]
#
# Goes through the game's own uploader, which lives in the Timber Empires mod
# (DevTools/WorkshopUpload.cs there), so that has to be in Mods too. It
# starts Timberborn if it isn't running, waits for the upload at the main menu
# and closes the game again if it was the one to start it. Steam has to be
# running and signed in. Then it uploads to mod.io through upload-modio.sh,
# when there's a token for it. It refuses when the installed version isn't the one
# the changelog's newest entry is for: run release.sh first.
set -euo pipefail

# ---- This repo ---------------------------------------------------------------
id="BeaverBuddies"
changelog="BeaverBuddies/changelog.txt"
# -----------------------------------------------------------------------------

repo="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo"
timberborn="$HOME/Documents/Timberborn"
# The newest version-x.y folder holds the manifest
installed=$(ls -d "$timberborn/Mods/$id"/version-*/manifest.json 2>/dev/null | sort -V | tail -1)
flag="$timberborn/TimberEmpires-upload"
result="$flag.result"

dry=0
for a in "$@"; do
    case "$a" in
        --dry-run) dry=1 ;;
        *) echo "unknown option $a" >&2; exit 1 ;;
    esac
done

die() { echo "$*" >&2; exit 1; }
# The newest entry: from its v<version> line to the first empty line
version=$(sed -n '1s/^v//p' "$changelog")
note=$(sed -n '2,/^$/p' "$changelog" | sed '/^$/d')
[ -n "$version" ] && [ -n "$note" ] || die "no entry at the top of $changelog"
have=$(sed -nE 's/.*"Version": *"([^"]+)".*/\1/p' "$installed" | head -1)
[ "$have" = "$version" ] || die "Mods has $id ${have:-nothing}, the changelog is at $version: run release.sh first"

echo "uploading $id $version with:"
printf '%s\n' "$note"
[ "$dry" = 0 ] || { echo "(dry run: nothing uploaded)"; exit 0; }

rm -f "$result"
{ echo "$id"; printf '%s\n' "$note"; } > "$flag"
started=0
cleanup() {
    rm -f "$flag"
    [ "$started" = 0 ] || pkill -x Timberborn 2>/dev/null || true
}
trap cleanup EXIT
if ! pgrep -x Timberborn >/dev/null; then
    pgrep -x steam_osx >/dev/null || die "Steam isn't running"
    echo "starting Timberborn..."
    open -a "$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app" --args -skipModManager
    started=1
fi
waited=0
until [ -f "$result" ]; do
    [ "$waited" -lt 300 ] || die "no answer from Timberborn after 5 minutes: is the mod enabled in it?"
    sleep 1
    waited=$((waited + 1))
done
outcome=$(cat "$result")
rm -f "$result"
case "$outcome" in
    ok*) echo "uploaded: https://steamcommunity.com/sharedfiles/filedetails/?id=${outcome#ok }" ;;
    *) die "$outcome" ;;
esac
if [ -s "$HOME/.config/timberempires/modio-token" ]; then
    "$repo/Tools/Package/upload-modio.sh"
fi
