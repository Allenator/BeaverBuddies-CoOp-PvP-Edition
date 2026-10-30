#!/bin/bash
# The checks that need no game, for BeaverBuddies: its translations have English's keys, and it builds (the Steam build, and the one without Steam
# networking and matchmaking, so code that should compile either way is caught), and its test harness with it, into a scratch folder
# so nothing in the Mods folder changes; and the release build installs no harness. Seconds. (See README.md.)
#
#   Tools/Tests/fast.sh
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
SCRATCH="${TMPDIR:-/tmp}/bb-fast-docs"
mkdir -p "$SCRATCH/Timberborn/Mods"
FAILED=0

step() { # name command...
    local name=$1 out
    shift
    if out=$("$@" 2>&1); then echo "  ok    $name"; else echo "  FAIL  $name"; echo "$out" | tail -15 | sed 's/^/        /'; FAILED=$((FAILED + 1)); fi
}

echo "BeaverBuddies, fast checks"
step "translations" python3 "$HERE/translations.py"
# Game Modes, which it builds on, into the same scratch folder
GM="${GAMEMODES_REPO:-$(cd "$REPO/../GameModes" 2>/dev/null && pwd)}"
step "builds Game Modes" bash -c "cd '$GM/GameModes' && dotnet build -c Release -nologo -v q -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
step "builds (Steam)" bash -c "cd '$REPO/BeaverBuddies' && dotnet build -c Release -nologo -v q -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
# The test harness, a DLL of its own that test runs install beside BeaverBuddies' (see README.md), each way too
step "builds the test harness (Steam)" bash -c "cd '$REPO/BeaverBuddies.TestHarness' && dotnet build -c Release -nologo -v q -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
step "builds without Steam networking and matchmaking" bash -c "cd '$REPO/BeaverBuddies' && dotnet build -c Release -nologo -v q -p:Steam=false -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
step "builds the test harness without Steam" bash -c "cd '$REPO/BeaverBuddies.TestHarness' && dotnet build -c Release -nologo -v q -p:Steam=false -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
# The Steam build again, so what's left in bin and obj is the one that ships
step "builds (Steam) again" bash -c "cd '$REPO/BeaverBuddies' && dotnet build -c Release -nologo -v q -p:DocumentsPath='$SCRATCH/' 2>&1 | tee /dev/stderr | grep -q '0 Error(s)'"
# What it installed is what a release ships: no test harness beside it
step "the release build installs no test harness" bash -c "[ ! -e '$SCRATCH/Timberborn/Mods/BeaverBuddies/version-1.1/BeaverBuddies.TestHarness.dll' ]"
echo
[ $FAILED -eq 0 ] && echo "All fast checks passed." || echo "$FAILED failed."
exit $((FAILED > 0))
