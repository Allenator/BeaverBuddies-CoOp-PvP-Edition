# The Load game box's Load, in a co-op game, loads nothing and says what to do instead. The game would load the save
# on that machine alone, under the connection it has: when the host did, its guest stood still, waiting for ticks the
# host's new game started again from 0, and no desync was reported (October 28, 2026). Here the host and then the guest
# press Load on a save of the host's, with the game menu's Load game as a player opens it: neither loads, the box
# says why, and both copies play on in sync
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3
send host "save"
wait_for host "\[Test\] saved" 60 || { fail "the host saves" "never did"; return; }
save=$(ls -t "$HOME/Documents/Timberborn/Saves"/*/*.timber | head -1)
settlement=$(basename "$(dirname "$save")")
name=$(basename "$save" .timber)
run_ticks 30
for inst in host client; do
    loads=$(grep -ac "Load time" "$(bb_log $inst)")
    send $inst "menu"; sleep 2
    send $inst "click LoadGameButton"; sleep 2
    send $inst "selectsave $settlement|$name"; sleep 1
    send $inst "click LoadButton"; sleep 3
    expect_log "the box says why ($inst)" $inst "Load in a co-op game: not loaded"
    expect_eq "nothing loads ($inst)" "$(grep -ac "Load time" "$(bb_log $inst)")" "$loads"
    # The message, the Load box and the game menu
    send $inst "click ConfirmButton"; sleep 1
    send $inst "click CloseButton"; sleep 1
    send $inst "click ResumeButton"; sleep 1
done
run_ticks 30
expect_in_sync "both play on in sync"
expect_no_errors "no errors"
