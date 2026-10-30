# The Load game box's Load, in a co-op game, loads nothing and says what to do instead. The game would load the save
# on that machine alone, under the connection it has: when the host did, its guest stood still, waiting for ticks the
# host's new game started again from 0, and no desync was reported (October 28, 2026). Here the host and then the guest
# press Load on a save of the host's, with the game menu's Load game as a player opens it: neither loads, the box
# says why, and both copies play on in sync. Then a game alone, which runs under the mod's IO for one player
# (LocalEventIO) once a hosting box over it is cancelled, loads as ever: the guard is for games hosted or joined
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3
send host "save"
wait_for host "\[Test\] saved" 60 || { fail "the host saves" "never did"; return; }
save=$(ls -t "$BB_SAVES"/*/*.timber | head -1)
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

bb_close
bb_launch host || { fail "a copy starts alone" "no main menu"; return; }
send host "newgame ${BB_MAP:-Waterfalls} Folktails"
wait_for_watch host "Load time" 180 || { fail "a game alone loads" "never did"; return; }
sleep 3; send host dismiss; sleep 1
send host "save"
wait_for host "\[Test\] saved" 60 || { fail "the game alone is saved" "never was"; return; }
save=$(ls -t "$BB_SAVES"/*/*.timber | head -1)
settlement=$(basename "$(dirname "$save")")
name=$(basename "$save" .timber)
# Hosted again over itself, and the box cancelled: the game plays on alone
send host "rehost"
wait_for_watch host "Server started listening" 60 || { fail "the game is hosted again" "no server"; return; }
sleep 3
send host "click CancelButton"; sleep 3
loads=$(grep -ac "Load time" "$(bb_log host)")
send host "menu"; sleep 2
send host "click LoadGameButton"; sleep 2
send host "selectsave $settlement|$name"; sleep 1
send host "click LoadButton"
for ((i = 0; i < 15; i++)); do [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && break; sleep 1; done
# The game asking first, if it does
[ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] || send host "click ConfirmButton"
for ((i = 0; i < 120; i++)); do [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && break; sleep 1; done
expect_no_log "the guard leaves a game alone be" host "Load in a co-op game: not loaded"
[ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && pass "a game alone loads its save" || fail "a game alone loads its save" "nothing loaded"
expect_no_errors "no errors alone"
bb_close
