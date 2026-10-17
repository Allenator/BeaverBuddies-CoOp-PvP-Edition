#!/bin/bash
# Library for the game scenarios (see README.md): starts two windowed copies of
# Timberborn on this Mac, drives them through BeaverBuddies' test harness (a
# command file each, DevTools/TestHarness.cs), reads their logs and records
# what a scenario expects. Sourced by run.sh and the scenarios; works on the
# macOS bash (3.2).

BB_GAME=${BB_GAME:-"$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app"}
BB_TEST_DIR=${BB_TEST_DIR:-"${TMPDIR:-/tmp}/bb-tests"}
BB_RESULTS=${BB_RESULTS:-"$BB_TEST_DIR/results.tsv"}
BB_PREFS_SAVED="$BB_TEST_DIR/prefs.saved"
BB_PREFS_DOMAIN=com.mechanistry.timberborn
# The harness reads <this>-<instance> twice a second
BB_CMD_PREFIX="$HOME/Library/Caches/BeaverBuddies-test"
# How fast the copies play while a scenario waits on the game: 30x, as fast as two copies keep up on one Mac
BB_SPEED=${BB_SPEED:-30}
mkdir -p "$BB_TEST_DIR/shots"

# The game's display settings: windowed for the run, then the player's own back
# (and any other preference a mod's scenario changes: BB_PREF_KEYS_EXTRA, a space-free list separated by '|')
BB_PREF_KEYS=("FullScreen" "ResolutionWidth" "ResolutionHeight" "Screenmanager Fullscreen mode" \
    "Screenmanager Resolution Use Native" "Screenmanager Resolution Width" "Screenmanager Resolution Height" \
    "MasterVolume" "MusicVolume")
if [ -n "${BB_PREF_KEYS_EXTRA:-}" ]; then IFS='|' read -ra _extra <<< "$BB_PREF_KEYS_EXTRA"; BB_PREF_KEYS+=("${_extra[@]}"); fi

prefs_save() {
    # A run that didn't finish left the player's own in the file: keep them
    [ -f "$BB_PREFS_SAVED" ] && return
    local k v
    for k in "${BB_PREF_KEYS[@]}"; do
        v=$(defaults read $BB_PREFS_DOMAIN "$k" 2>/dev/null) && echo "$k|$v" || echo "$k|"
    done > "$BB_PREFS_SAVED"
}

prefs_windowed() {
    defaults write $BB_PREFS_DOMAIN FullScreen -int 0
    defaults write $BB_PREFS_DOMAIN ResolutionWidth -int 1400
    defaults write $BB_PREFS_DOMAIN ResolutionHeight -int 880
    defaults write $BB_PREFS_DOMAIN "Screenmanager Fullscreen mode" -int 3
    defaults write $BB_PREFS_DOMAIN "Screenmanager Resolution Use Native" -int 0
    defaults write $BB_PREFS_DOMAIN "Screenmanager Resolution Width" -int 1400
    defaults write $BB_PREFS_DOMAIN "Screenmanager Resolution Height" -int 880
    # The copies run silent
    defaults write $BB_PREFS_DOMAIN MasterVolume -float 0
    defaults write $BB_PREFS_DOMAIN MusicVolume -float 0
}

prefs_restore() {
    [ -f "$BB_PREFS_SAVED" ] || return 0
    local k v
    while IFS='|' read -r k v; do
        if [ -z "$v" ]; then defaults delete $BB_PREFS_DOMAIN "$k" 2>/dev/null
        elif [[ "$v" =~ ^-?[0-9]+$ ]]; then defaults write $BB_PREFS_DOMAIN "$k" -int "$v"
        else defaults write $BB_PREFS_DOMAIN "$k" -string "$v"; fi
    done < "$BB_PREFS_SAVED"
    rm -f "$BB_PREFS_SAVED"
}

# pref_set <key> <int>: a game preference for this scenario only (a mod's setting, say), the player's
# own put back when the scenario is over. BeaverBuddies' "always trace" is the one that matters:
# a scenario that is about logging being off, or on, sets it, whatever this Mac has it at.
BB_PREFS_OVERRIDES="$BB_TEST_DIR/prefs.overrides"
pref_set() {
    local k=$1 v=$2 old
    old=$(defaults read $BB_PREFS_DOMAIN "$k" 2>/dev/null) || old=""
    # Only the first change of a key remembers the player's own value
    grep -q "^$k|" "$BB_PREFS_OVERRIDES" 2>/dev/null || echo "$k|$old" >> "$BB_PREFS_OVERRIDES"
    defaults write $BB_PREFS_DOMAIN "$k" -int "$v"
}

prefs_restore_overrides() {
    [ -f "$BB_PREFS_OVERRIDES" ] || return 0
    local k v
    while IFS='|' read -r k v; do
        if [ -z "$v" ]; then defaults delete $BB_PREFS_DOMAIN "$k" 2>/dev/null
        elif [[ "$v" =~ ^-?[0-9]+$ ]]; then defaults write $BB_PREFS_DOMAIN "$k" -int "$v"
        else defaults write $BB_PREFS_DOMAIN "$k" -string "$v"; fi
    done < "$BB_PREFS_OVERRIDES"
    rm -f "$BB_PREFS_OVERRIDES"
}

# ---- Mods: switching one off for a scenario, and always back on --------------

bb_mods_dir() { (cd "$HOME/Documents/Timberborn/Mods" && pwd -P); }

mod_disable() {
    local mods; mods=$(bb_mods_dir)
    [ -d "$mods/$1" ] || return 0
    mv "$mods/$1" "$mods/../$1.disabled-by-tests" && echo "$1" >> "$BB_TEST_DIR/disabled-mods"
}

mod_enable_all() {
    local mods name; mods=$(bb_mods_dir)
    [ -f "$BB_TEST_DIR/disabled-mods" ] || return 0
    while read -r name; do
        [ -d "$mods/../$name.disabled-by-tests" ] && [ ! -e "$mods/$name" ] && mv "$mods/../$name.disabled-by-tests" "$mods/$name"
    done < "$BB_TEST_DIR/disabled-mods"
    rm -f "$BB_TEST_DIR/disabled-mods"
}

mod_installed() { [ -d "$(bb_mods_dir)/$1" ]; }

# ---- Game copies ----------------------------------------------------------------

bb_log() { echo "$BB_TEST_DIR/$1.log"; }

# bb_keep_log <instance>: that copy's log moved into this run's logs folder, named after the
# scenario and numbered, before anything starts a new one over it. A scenario run on its own
# keeps its logs in runs/single
bb_keep_log() {
    local log dir n=1
    log=$(bb_log "$1")
    [ -s "$log" ] || { rm -f "$log"; return 0; }
    dir="${BB_RUN_DIR:-$BB_TEST_DIR/runs/single}/logs"
    mkdir -p "$dir"
    while [ -e "$dir/${BB_SCENARIO:-x}-$1-$n.log" ]; do n=$((n + 1)); done
    mv "$log" "$dir/${BB_SCENARIO:-x}-$1-$n.log"
}

# send <instance> <command...>: one harness command, once the last one's been taken
send() {
    local inst=$1 file n=0
    shift
    file="$BB_CMD_PREFIX-$inst"
    while [ -e "$file" ] && [ $n -lt 200 ]; do sleep 0.05; n=$((n + 1)); done
    # A copy that hasn't taken the last one in 10 s is stuck: its command would be lost under this one
    [ -e "$file" ] && echo "  (warning: $inst hadn't taken \"$(head -1 "$file")\", sending \"$*\" after it)" >&2
    # Written whole and then moved in: the game reads and deletes the file as soon as it sees it,
    # and could otherwise read one half written, empty, and drop the command
    if [ -e "$file" ]; then echo "$*" >> "$file"; else echo "$*" > "$file.part" && mv "$file.part" "$file"; fi
}

# wait_for <instance> <regex> [seconds]: gives up early if that copy has closed
wait_for() {
    local inst=$1 re=$2 t=${3:-60} i
    for ((i = 0; i < t * 2; i++)); do
        grep -aEq -- "$re" "$(bb_log "$inst")" 2>/dev/null && return 0
        [ $((i % 10)) == 9 ] && [ -z "$(bb_pid "$inst")" ] && return 1
        sleep 0.5
    done
    return 1
}

# The first exception either copy's log shows: a mod that fails to load (a binding for a type the
# game binds differently, say) is there at once, and waiting on the game only hides it
bb_load_error() {
    local e
    e=$(grep -aE "[A-Za-z]Exception\b" "$(bb_log host)" "$(bb_log client)" 2>/dev/null | grep -v "gpath.c" | head -1 | cut -c1-300)
    [ -n "$e" ] && echo "$e"
}

# wait_for that gives up at once on an exception in either log
wait_for_watch() {
    local inst=$1 re=$2 t=${3:-60} i e
    for ((i = 0; i < t * 2; i++)); do
        grep -aEq -- "$re" "$(bb_log "$inst")" 2>/dev/null && return 0
        e=$(bb_load_error) && { echo "  load error: $e"; return 1; }
        sleep 0.5
    done
    return 1
}

bb_launch() {
    bb_open "$1"
    wait_for_watch "$1" "Registering Main Menu Services" 120
}

# The pid of a test copy
bb_pid() {
    local p
    for p in $(pgrep -x Timberborn); do
        ps eww -p "$p" 2>/dev/null | grep -q "BB_INSTANCE=$1\b" && { echo "$p"; return; }
    done
}

# The guest's window put right above the host's, which stays wherever it is unless there's no
# room above it: then the host's goes down to make some, under the menu bar. Through macOS,
# which knows where the windows are (the game reports 0,0)
bb_stack_windows() {
    local host client at i
    host=$(bb_pid host); client=$(bb_pid client)
    [ -n "$host" ] && [ -n "$client" ] || return 0
    for i in 1 2 3 4 5 6 7 8 9 10; do
        at=$(osascript -e "tell application \"System Events\" to tell (first process whose unix id is $host) to get (position of window 1) & (size of window 1)" 2>/dev/null)
        [ -n "$at" ] && break
        sleep 0.5
    done
    [ -n "$at" ] || return 0
    IFS=', ' read -r x y _ h <<< "$at"
    local top=25
    if [ $((y - h)) -lt $top ]; then
        y=$((top + h))
        osascript -e "tell application \"System Events\" to tell (first process whose unix id is $host) to set position of window 1 to {$x, $y}" >/dev/null 2>&1 || true
    fi
    osascript -e "tell application \"System Events\" to tell (first process whose unix id is $client) to set position of window 1 to {$x, $((y - h))}" >/dev/null 2>&1 || true
}

# bb_open <instance>: starts a copy without waiting for it, so two can load side by side
bb_open() {
    local inst=$1
    rm -f "$BB_CMD_PREFIX-$inst"
    bb_keep_log "$inst"
    prefs_windowed
    open -g -n -a "$BB_GAME" --env SteamAppId=1062090 --env SteamGameId=1062090 --env BB_INSTANCE="$inst" \
        ${BB_RTC_CODE:+--env BB_RTC_CODE="$BB_RTC_CODE"} \
        ${BB_HITCH_MS:+--env BB_HITCH_MS="$BB_HITCH_MS"} \
        ${BB_LEAK_ROOTS:+--env BB_LEAK_ROOTS="$BB_LEAK_ROOTS"} ${BB_LEAK_SIZES:+--env BB_LEAK_SIZES=1} ${BB_LEAK_WALK:+--env BB_LEAK_WALK=1} \
        --args -skipModManager -logFile "$(bb_log "$inst")" -screen-fullscreen 0 -screen-width 1400 -screen-height 880
}

# Whether the player's own Timberborn is running (not one of the test copies)
player_game_running() {
    local p
    for p in $(pgrep -x Timberborn); do
        ps eww -p "$p" 2>/dev/null | grep -q "BB_INSTANCE=" || return 0
    done
    return 1
}

# Closes the test copies, and only them: the player's own game isn't touched
bb_close() {
    local p
    for p in $(pgrep -x Timberborn); do
        ps eww -p "$p" 2>/dev/null | grep -q "BB_INSTANCE=" && kill -9 "$p" 2>/dev/null
    done
    rm -f "$BB_CMD_PREFIX-host" "$BB_CMD_PREFIX-client"
    local n=0
    while lsof -nP -iTCP -sTCP:LISTEN 2>/dev/null | grep -qi timberborn && [ $n -lt 40 ]; do sleep 1; n=$((n + 1)); done
}

# bb_kill <instance>: closes one test copy
bb_kill() {
    local p
    for p in $(pgrep -x Timberborn); do
        ps eww -p "$p" 2>/dev/null | grep -q "BB_INSTANCE=$1 " && kill -9 "$p" 2>/dev/null
    done
    sleep 3
}

# start_match <host faction> <client faction> [game mode id]: both copies in one
# match game, dialogs dismissed. Returns 1 if it never got there. BB_HOST_SETTINGS and
# BB_CLIENT_SETTINGS ("id=value ...") are each copy's picks for new games' settings
start_match() {
    local hostf=${1:-Folktails} clientf=${2:-IronTeeth} mode=${3:-} pick e
    bb_close
    # One after the other: copies starting together race on the player's data file
    bb_launch host || return 1
    bb_launch client || return 1
    bb_stack_windows
    for pick in ${BB_HOST_SETTINGS:-}; do send host "gamesetting ${pick%%=*} ${pick#*=}"; done
    for pick in ${BB_CLIENT_SETTINGS:-}; do send client "gamesetting ${pick%%=*} ${pick#*=}"; done
    send host "hostmatch ${BB_MAP:-Waterfalls} $hostf $mode"
    wait_for_watch host "Server started listening" 120 || return 1
    # BB_JOIN_ADDRESS joins some other way than 127.0.0.1, rtc:CODE over WebRTC say
    send client "joinmatch $clientf ${BB_JOIN_ADDRESS:-}"
    local i
    for ((i = 0; i < 180; i++)); do
        grep -aq "Registering Co-op services" "$(bb_log host)" \
            && sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
            && sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -aq "Load time" && break
        e=$(bb_load_error) && { echo "  load error: $e"; return 1; }
        sleep 1
    done
    [ $i -lt 180 ] || return 1
    sleep 3
    send host dismiss; send client dismiss
    sleep 2
    return 0
}

# rematch <host faction> <client faction>: both copies exit to the main menu, the host with the exit save
# and the client without, so state a save leaves behind differs between them,
# and start a new match without restarting, so whatever the last one left behind is still there.
# Returns 1 if it never got there
rematch() {
    local hostf=${1:-Folktails} clientf=${2:-IronTeeth} c i n_host n_client
    n_host=$(grep -ac "Registering Main Menu Services" "$(bb_log host)")
    n_client=$(grep -ac "Registering Main Menu Services" "$(bb_log client)")
    send client "exitmenu nosave"; send host "exitmenu"
    for ((i = 0; i < 240; i++)); do
        [ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$n_host" ] \
            && [ "$(grep -ac "Registering Main Menu Services" "$(bb_log client)")" -gt "$n_client" ] && break
        sleep 0.5
    done
    [ $i -lt 240 ] || return 1
    sleep 3
    n_host=$(grep -ac "Load time" "$(bb_log host)"); n_client=$(grep -ac "Load time" "$(bb_log client)")
    c=$(grep -ac "Server started listening" "$(bb_log host)")
    send host "hostmatch ${BB_MAP:-Waterfalls} $hostf"
    for ((i = 0; i < 240; i++)); do [ "$(grep -ac "Server started listening" "$(bb_log host)")" -gt "$c" ] && break; sleep 0.5; done
    [ $i -lt 240 ] || return 1
    send client "joinmatch $clientf"
    for ((i = 0; i < 180; i++)); do
        [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$n_host" ] && [ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$n_client" ] && break
        sleep 1
    done
    [ $i -lt 180 ] || return 1
    sleep 3
    send host dismiss; send client dismiss
    sleep 2
}

# place_starts <host faction> <client faction>: each player's free District Center, where the map's open
place_starts() {
    send host "place DistrictCenter.$1 ${BB_HOST_START:-79 46 5}"
    sleep 3
    send client "place DistrictCenter.$2 ${BB_CLIENT_START:-89 68 5}"
    sleep 6
    send host dismiss; send client dismiss
    sleep 3
}

# ---- Asking the game ------------------------------------------------------------

# probe <instance> <key...>: what Timber Empires' harness probe says for "te <key>" on that copy
# (DevTools/HarnessProbe.cs), with player IDs shortened to P
probe() {
    local inst=$1 key=$2 log before after i
    log=$(bb_log "$inst")
    before=$(grep -aFc "[Test] te $key:" "$log")
    send "$inst" "te $key"
    for i in $(seq 1 300); do
        after=$(grep -aFc "[Test] te $key:" "$log")
        [ "$after" -gt "$before" ] && break
        sleep 0.05
    done
    grep -aF "[Test] te $key:" "$log" | tail -1 | sed "s/.*\[Test\] te $key: *//" | sed -E 's/[0-9a-f]{8}-[0-9a-f-]{27}/P/g'
}

# The last tick both copies logged, as "<tick> <host line> | <client line>" hashes
sync_state() {
    local h c n
    h=$(grep -aEo "Tick [0-9]+ IO done" "$(bb_log host)" | tail -1 | grep -oE "[0-9]+")
    c=$(grep -aEo "Tick [0-9]+ IO done" "$(bb_log client)" | tail -1 | grep -oE "[0-9]+")
    [ -n "$h" ] && [ -n "$c" ] || { echo "no ticks"; return 1; }
    n=$((10#$h < 10#$c ? 10#$h : 10#$c))
    local hh ch
    hh=$(grep -aE "Tick 0*$n IO done" "$(bb_log host)" | tail -1 | sed 's/.*IO done//')
    ch=$(grep -aE "Tick 0*$n IO done" "$(bb_log client)" | tail -1 | sed 's/.*IO done//')
    [ "$hh" == "$ch" ] && echo "tick $n in sync" || echo "tick $n DIFFERS: host$hh client$ch"
}

# shot <name> [instance]: a screenshot of that copy, kept with the report
shot() {
    send "${2:-host}" "snap $BB_TEST_DIR/shots/${BB_SCENARIO:-x}-$1.png"
    sleep 2
}

# ---- What a scenario expects -----------------------------------------------------

_record() { printf '%s\t%s\t%s\t%s\n' "$1" "${BB_SCENARIO:-?}" "$2" "$3" >> "$BB_RESULTS"; }

pass() { echo "  ok    $1"; _record PASS "$1" ""; }
# A mod's own tests can define bb_on_fail <check> to keep more of the copies' state at a failure
fail() { echo "  FAIL  $1: $2"; _record FAIL "$1" "$2"; declare -F bb_on_fail > /dev/null && bb_on_fail "$1"; return 0; }

# expect_eq <name> <actual> <wanted>
expect_eq() { if [ "$2" == "$3" ]; then pass "$1"; else fail "$1" "got '$2', wanted '$3'"; fi; }

# expect_match <name> <actual> <regex>
expect_match() { if echo "$2" | grep -aEq -- "$3"; then pass "$1"; else fail "$1" "got '$2', wanted it to match '$3'"; fi; }

# expect_no_match <name> <actual> <regex>
expect_no_match() { if echo "$2" | grep -aEq -- "$3"; then fail "$1" "got '$2', wanted it not to match '$3'"; else pass "$1"; fi; }

# expect_nonempty <name> <value>: guards a comparison against passing because both sides are empty
expect_nonempty() { if [ -n "$2" ]; then pass "$1"; else fail "$1" "nothing came back"; fi; }

# expect_log <name> <instance> <regex>: the copy's log has such a line
expect_log() { if grep -aEq -- "$3" "$(bb_log "$2")"; then pass "$1"; else fail "$1" "no line like '$3' in the $2 log"; fi; }

# expect_no_log <name> <instance> <regex>
expect_no_log() {
    local line; line=$(grep -aE -- "$3" "$(bb_log "$2")" | head -1 | cut -c1-200)
    if [ -z "$line" ]; then pass "$1"; else fail "$1" "$2 log has: $line"; fi
}

# expect_in_sync <name>: both copies' state hashes agree at the last tick they share
expect_in_sync() {
    local s; s=$(sync_state)
    if echo "$s" | grep -q "in sync"; then pass "$1 ($s)"; else fail "$1" "$s"; fi
}

# expect_no_errors <name>: no exception in either log
expect_no_errors() {
    local e
    e=$(grep -aE "NullReferenceException|Exception:|desync|Desync" "$(bb_log host)" "$(bb_log client)" 2>/dev/null | grep -v "gpath.c" | head -2 | cut -c1-200)
    if [ -z "$e" ]; then pass "$1"; else fail "$1" "$e"; fi
}

# ---- The menus ---------------------------------------------------------------------

# new_game_page <instance> [host|match]: the New Game screens' last page, as a player gets there
# from the main menu to host a game (the default) or to find a match
new_game_page() {
    local button=HostNewGameMenuButton c
    [ "${2:-host}" == match ] && button=FindMatchMenuButton
    for c in "click MultiplayerButton" "click $button" "click NextButton" "click NextButton"; do
        send "$1" "$c"; sleep 2
    done
}

# layout_of <instance> <element name>: the element and its children as the harness lays them out
# (position, size, and the text of any label), one per line
layout_of() {
    local log L
    log=$(bb_log "$1")
    send "$1" "layout $2"; sleep 2
    L=$(grep -an "\[Test\] Layout $2:" "$log" | tail -1 | cut -d: -f1)
    [ -n "$L" ] && sed -n "$((L + 1)),\$p" "$log" | awk '/^\[[0-9]+-[0-9]+-[0-9]+\./ {exit} {print}'
}

# button_ys <layout>: the top of each button, comma separated
button_ys() { echo "$1" | grep NineSliceButton | sed -E 's/.* y=([0-9]+) .*/\1/' | paste -sd, -; }
