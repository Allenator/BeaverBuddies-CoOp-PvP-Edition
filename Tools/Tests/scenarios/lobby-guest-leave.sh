# A guest waiting in the lobby presses Cancel: it leaves for the main menu, and the host's box stays. Then the host
# starts and the guest joins the game running: the lobby it left is over, so it doesn't set its random state again at
# that game's first events, as only a guest waiting for the start does, and the two stay in sync
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "hostnew ${BB_MAP:-Waterfalls} Folktails ${BB_MODE:-}"
wait_for_watch host "Reading map with length" 120 || { fail "the box comes up" "never hosted"; return; }
send client "joinmatch Folktails"
wait_for client "waiting in its lobby" 120 && pass "the guest waits in the lobby" || { fail "the guest waits in the lobby" "never did"; return; }
n=$(grep -ac "Registering Main Menu Services" "$(bb_log client)")
send client "click CancelButton"
for ((i = 0; i < 60; i++)); do
    [ "$(grep -ac "Registering Main Menu Services" "$(bb_log client)")" -gt "$n" ] && break
    sleep 1
done
[ "$(grep -ac "Registering Main Menu Services" "$(bb_log client)")" -gt "$n" ] && pass "cancel leaves for the main menu" || fail "cancel leaves for the main menu" "still in the game"
sleep 3
shot "guest-menu" client
send host "click ConfirmButton"
sleep 3
run_ticks 10
loads=$(grep -ac "Load time" "$(bb_log client)")
send client "joinmatch Folktails"
for ((i = 0; i < 120; i++)); do
    [ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$loads" ] && break
    sleep 1
done
[ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$loads" ] && pass "the guest joins the game running" || { fail "the guest joins the game running" "never loaded it"; bb_close; return; }
sleep 5
run_ticks 30
expect_no_log "no lobby's mark in the next game" client "The host started: setting the random state"
expect_in_sync "in sync"
bb_close
