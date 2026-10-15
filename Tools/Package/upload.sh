#!/bin/bash
# Uploads the mod installed in Mods to its Steam Workshop item, with the
# newest changelog entry as the change note:
#
#   Tools/Package/upload.sh [--dry-run]
#
# Goes through the Mod Uploader mod (~/Developer/TimberbornModUploader, it has
# to be in Mods): it starts Timberborn if it isn't running, waits for the
# upload at the main menu and closes the game again if it was the one to start
# it. Steam has to be running and signed in. The same request uploads the
# release zip to mod.io when there's a token for it. It refuses when the
# installed version isn't the one the changelog's newest entry is for: run
# release.sh first.
set -euo pipefail

# ---- This repo ---------------------------------------------------------------
id="BeaverBuddies"
modio_game=3659
modio_mod=6446808
changelog="BeaverBuddies/changelog.txt"
# -----------------------------------------------------------------------------

repo="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$repo"
timberborn="$HOME/Documents/Timberborn"
# The newest version-x.y folder holds the manifest
installed=$(ls -d "$timberborn/Mods/$id"/version-*/manifest.json 2>/dev/null | sort -V | tail -1)
flag="$timberborn/ModUploader-request"
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
zip=$(ls -t "$HOME"/Downloads/$id-[0-9]*.zip 2>/dev/null | head -1 || true)
{
    echo "mod: $id"
    if [ -n "$zip" ] && [ -s "$HOME/.config/timberempires/modio-token" ]; then
        echo "modio-game: $modio_game"; echo "modio-mod: $modio_mod"; echo "modio-zip: $zip"; echo "version: $version"
    fi
    echo
    printf '%s\n' "$note"
} > "$flag"
started=0
cleanup() {
    rm -f "$flag"
    [ "$started" = 0 ] || pkill -x Timberborn 2>/dev/null || true
}
trap cleanup EXIT
if ! pgrep -x Timberborn >/dev/null; then
    pgrep -x steam_osx >/dev/null || die "Steam isn't running"
    echo "starting Timberborn..."
    open -a "$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app" --env SteamAppId=1062090 --env SteamGameId=1062090 --args -skipModManager
    started=1
fi
waited=0
until [ -f "$result" ]; do
    [ "$waited" -lt 300 ] || die "no answer from Timberborn after 5 minutes: is Mod Uploader enabled in it?"
    sleep 1
    waited=$((waited + 1))
done
outcome=$(cat "$result")
rm -f "$result"
failed=0
while read -r target status rest; do
    case "$target $status" in
        "steam ok") echo "uploaded: https://steamcommunity.com/sharedfiles/filedetails/?id=$rest" ;;
        "modio ok") echo "uploaded to mod.io: file $rest" ;;
        *) echo "$target $status $rest" >&2; failed=1 ;;
    esac
done <<< "$outcome"
[ "$failed" = 0 ] || exit 1
