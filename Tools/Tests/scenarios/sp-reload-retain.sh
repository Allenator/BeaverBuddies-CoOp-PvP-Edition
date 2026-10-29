# Whether a game loaded again from inside a game keeps the first alive with no co-op at all: one copy, alone, loads a
# copy of the newest saved game from before the run (BB_LEAK_SAVE), marks it (scenes), loads it again from inside it,
# plays 45 s, and a full collection says whether the first is alive (scenes, heap). BB_REPEAT (2) times. The host of a
# saved game kept its first load every time on October 29, 2026 (double-load-retain); this tells the game's own from
# co-op's; BB_VIA_MENU=1 goes back to the main menu between the loads. No profiling (BB_HITCH_MS unset), whose
# leak finder keeps games itself
unset BB_HITCH_MS
settlement="Harness Alone $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Alone) || { fail "a saved game to load" "none in Multiplayer, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src"
kept=0
# A save the game asks about first (its mods' versions, say) waits on it: confirmed if no load has begun in 20 s
confirm_load() {
    local i
    for ((i = 0; i < 20; i++)); do [ "$(grep -ac "Loading saved game" "$(bb_log host)")" -gt "$1" ] && return; sleep 1; done
    send host "click ConfirmButton"
}
for ((run = 1; run <= ${BB_REPEAT:-2}; run++)); do
    bb_close
    bb_launch host || { fail "run $run: the copy starts" "no main menu"; continue; }
    send host "loadsave $settlement|Alone"
    confirm_load 0
    wait_for_watch host "Load time" 180 || { fail "run $run: the first load" "never ended"; continue; }
    sleep 5; send host dismiss; sleep 1
    send host scenes; sleep 2
    loads=$(grep -ac "Load time" "$(bb_log host)")
    # BB_VIA_MENU=1: back to the main menu first, then the load from there
    if [ -n "${BB_VIA_MENU:-}" ]; then
        m=$(grep -ac "Registering Main Menu Services" "$(bb_log host)")
        send host "exitmenu nosave"
        for ((i = 0; i < 60; i++)); do [ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$m" ] && break; sleep 1; done
        sleep 3
    fi
    send host "loadsave $settlement|Alone"
    confirm_load "$(grep -ac "Loading saved game" "$(bb_log host)")"
    for ((i = 0; i < 200; i++)); do [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && break; sleep 1; done
    sleep 5; send host dismiss; sleep 1
    send host "speedkey 2"; sleep 45; send host "speedkey 0"; sleep 2
    n=$(grep -ac "\[Test\] scenes:" "$(bb_log host)")
    send host scenes; send host heap
    for ((i = 0; i < 60; i++)); do [ "$(grep -ac "\[Test\] scenes:" "$(bb_log host)")" -gt "$n" ] && break; sleep 1; done
    sleep 3
    alive=$(grep -a "\[Test\] scenes:" "$(bb_log host)" | tail -1 | sed -E 's/.*scenes: ([0-9]+) of ([0-9]+).*/\1 of \2/')
    heap=$(grep -a "\[Test\] heap:" "$(bb_log host)" | tail -1 | sed -E 's/.*collected ([0-9]+) MB.*/\1/')
    echo "  run $run: earlier games alive: $alive; collected heap $heap MB"
    case "$alive" in 0\ of*) ;; *) kept=$((kept + 1)) ;; esac
done
echo "  alone, the first game was kept in $kept of ${BB_REPEAT:-2} runs"
bb_close
