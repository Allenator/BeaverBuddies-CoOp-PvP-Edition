# Whether a long frame alone can split the game: game logic that went by the frame's time rather than the tick's
# would. The host's main thread, then the guest's, is held BB_STALL_MS (900) at a time, BB_STALLS (10) times each, as
# a long collection or a save holds it, mostly between the parts of a tick (a tick is spread over frames), while a
# copy of the newest saved game from before the run (BB_LEAK_SAVE) plays at speed 7. On October 29, 2026, the roots
# walk held the host 875 ms mid-tick and the next tick split; this tells a long frame from the walk's own doing
settlement="Harness Stall $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Stall) || { fail "a saved game to load" "none in Multiplayer, and no BB_LEAK_SAVE"; return; }
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
host_saved_game "$settlement" Stall && pass "the guest joins the saved game" || { fail "the guest joins the saved game" "$HOSTED_WHY"; return; }
sleep 3; send host dismiss; send client dismiss; sleep 1
send host "play 7"; sleep 10
for inst in host client; do
    for ((n = 0; n < ${BB_STALLS:-10}; n++)); do send $inst "stall ${BB_STALL_MS:-900}"; sleep 4; done
    expect_log "the $inst's frames were held" $inst "\[Test\] stalled"
done
sleep 10
send host "play 0"; sleep 3
expect_no_log "no desync" host "Out of sync"
expect_in_sync "in sync after the held frames"
bb_close
