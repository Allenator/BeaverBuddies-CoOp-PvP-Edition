# A measurement, not a check: where a save's time goes on a game the size of a real one (a copy of the newest saved
# game from before the run, BB_LEAK_SAVE), hosted and joined, played a while at speed 3, then saved
# BB_SAVES (5) times by the host and by the guest (BB_SAVE_TIMING, BeaverBuddies.TestHarness/SaveTiming.cs): finishing the tick,
# collecting the world on the main thread, writing its JSON, the other entries, compressing and copying to the file
export BB_SAVE_TIMING=1
settlement="Harness Saves $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Saves) || { fail "a saved game to load" "none pinned at BB_BIG_SAVE, and no BB_LEAK_SAVE"; unset BB_SAVE_TIMING; return; }
echo "  the save: $src"
bb_close
bb_launch host || { fail "the host starts" "no main menu"; unset BB_SAVE_TIMING; return; }
bb_launch client || { fail "the guest starts" "no main menu"; unset BB_SAVE_TIMING; return; }
bb_stack_windows
host_saved_game "$settlement" Saves && pass "the guest joins the saved game" || { fail "the guest joins the saved game" "$HOSTED_WHY"; unset BB_SAVE_TIMING; return; }
sleep 5
send host dismiss; send client dismiss; sleep 2
send host "play 3"; sleep 20
for ((n = 0; n < ${BB_SAVES:-5}; n++)); do
    send host save; sleep 6
    send client save; sleep 6
done
send host "play 0"; sleep 2
for inst in host client; do
    echo "  $inst:"
    grep -a -A3 "\[SaveTiming\]" "$(bb_log $inst)" | grep -a -E "SaveTiming|Saved game in" | sed -E 's/^\[[0-9-]+\.[0-9]+\] //' | sed 's/^/    /' | cut -c1-260
done
expect_log "the host's saves are timed" host "\[SaveTiming\]"
expect_no_log "no desync" host "Out of sync"
bb_close
unset BB_SAVE_TIMING
