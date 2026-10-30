# Whether the host of a saved game keeps its first load alive once Start has loaded the save again (the double load of
# the Load game box's Host), as a player's game runs: no profiling (BB_HITCH_MS unset; the leak finder it turns on
# keeps games itself, see gc.md). A copy of the newest saved game from before the run (BB_LEAK_SAVE), hosted, joined
# and started BB_REPEAT (4) times, each from a fresh pair of copies. The host's first game is marked as it waits
# (scenes, a weak reference to its event bus), and 45 s after Start, while the game plays, a full collection says
# whether it's alive, with each copy's collected heap (heap). BB_ROOTS=1: the walk to what keeps it, if it's kept
unset BB_HITCH_MS
settlement="Harness Retain $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Retain) || { fail "a saved game to load" "none pinned at BB_BIG_SAVE, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src"
mb() { grep -a "\[Test\] heap:" "$(bb_log "$1")" | tail -1 | sed -E 's/.*collected ([0-9]+) MB.*/\1/'; }
kept=0
for ((run = 1; run <= ${BB_REPEAT:-4}; run++)); do
    bb_close
    bb_launch host || { fail "start $run: the host starts" "no main menu"; continue; }
    bb_launch client || { fail "start $run: the guest starts" "no main menu"; continue; }
    bb_stack_windows
    # The first game marked (scenes) as the host waits over it
    host_saved_game "$settlement" Retain Folktails scenes || { fail "start $run: the guest joins the saved game" "$HOSTED_WHY"; continue; }
    sleep 3; send host dismiss; send client dismiss; sleep 1
    send host "play 3"; sleep 45
    n=$(grep -ac "\[Test\] scenes:" "$(bb_log host)")
    send host scenes; send host heap; send client heap
    for ((i = 0; i < 60; i++)); do [ "$(grep -ac "\[Test\] scenes:" "$(bb_log host)")" -gt "$n" ] && [ -n "$(mb client)" ] && break; sleep 1; done
    sleep 3
    alive=$(grep -a "\[Test\] scenes:" "$(bb_log host)" | tail -1 | sed -E 's/.*scenes: ([0-9]+) of ([0-9]+).*/\1 of \2/')
    echo "  start $run: the host's earlier games alive: $alive; collected heap: host $(mb host) MB, guest $(mb client) MB"
    case "$alive" in 0\ of*) ;; *) kept=$((kept + 1)) ;; esac
    if [ -n "${BB_ROOTS:-}" ] && ! echo "$alive" | grep -q "^0 of"; then
        # Paused, and with no resync after: the walk reads every static field, which runs type initializers early, and
        # one run mid-tick split the host from its guest (the walk's own doing, not the game's)
        send host "play 0"; send host noresync; sleep 3
        send host roots
        for ((i = 0; i < ${BB_LEAK_WAIT:-900}; i++)); do grep -aq "\[Test\] roots (\|\[Test\] roots: no earlier" "$(bb_log host)" && break; sleep 1; done
        grep -aE "\[Test\] roots (\(|: no earlier)" "$(bb_log host)" | tail -1 | cut -c1-3000 | sed 's/^/    /'
    fi
    send host "play 0"
done
echo "  the host kept its first game in $kept of ${BB_REPEAT:-4} starts"
bb_close
