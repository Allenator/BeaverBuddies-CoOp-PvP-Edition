# What the always-on record of each tick's entities costs, on a game the size of a real one: a copy of the newest
# saved game from before the run (BB_LEAK_SAVE) hosted and joined, played at speed 3 for BB_COST_SECONDS (60) with
# profiling (BB_HITCH_MS), and each copy's pace lines printed: the memory allocated in each 10 s with no collection,
# and the ticking time. BB_DIGEST_OFF=1 turns the record off to compare. A measurement, not a check
newest_save() {
    local f
    ls -t "$HOME/Documents/Timberborn/Saves/Multiplayer"/*.timber 2>/dev/null | while read -r f; do
        [ -n "${RUN_START:-}" ] && [ "$f" -nt "$RUN_START" ] && continue
        echo "$f"; break
    done
}
src=${BB_LEAK_SAVE:-$(newest_save)}
[ -f "$src" ] || { fail "a saved game to load" "none in Multiplayer, and no BB_LEAK_SAVE"; return; }
settlement="Harness Cost $(date +%Hh%M%S)"
mkdir -p "$HOME/Documents/Timberborn/Saves/$settlement"
cp "$src" "$HOME/Documents/Timberborn/Saves/$settlement/Cost.timber"
echo "  the save: $src; the record $([ -n "${BB_DIGEST_OFF:-}" ] && echo off || echo on)"
export BB_HITCH_MS=${BB_HITCH_MS:-1000}
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "hostsave $settlement|Cost"
wait_for_watch host "Server started listening" 180 || { fail "the host hosts the save" "no server"; return; }
send client "joinmatch Folktails"
for ((i = 0; i < 300; i++)); do
    sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
        && [ "$(sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -ac "Load time")" -ge 1 ] && break
    sleep 1
done
[ $i -lt 300 ] && pass "the guest joins the saved game" || { fail "the guest joins" "never loaded"; return; }
sleep 5
send host dismiss; send client dismiss; sleep 2
send host "play 3"
sleep "${BB_COST_SECONDS:-60}"
for inst in host client; do
    grep -aE "\[Pace\] tick [0-9]+ speed" "$(bb_log $inst)" | tail -6 | sed -E "s/.*(ticking avg [0-9.]+ ms), (.*)/  $inst: \1, \2/"
done
unset BB_HITCH_MS
