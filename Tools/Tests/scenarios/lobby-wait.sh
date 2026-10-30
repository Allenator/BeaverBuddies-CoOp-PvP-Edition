# A guest who waits a while in the lobby of a new game the host hasn't started plays it in sync: the game uses some of
# its random state up while the guest waits, so the guest sets it again when the host's first events play, as the
# host did when it started (upstream's e3e6fe6). BB_LOBBY_WAIT: how long it waits (30 s)
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "hostnew ${BB_MAP:-Waterfalls} Folktails ${BB_MODE:-}"
wait_for_watch host "Reading map with length" 120 || { fail "the box comes up" "never hosted"; return; }
send client "joinmatch Folktails"
wait_for client "waiting in its lobby" 120 && pass "the guest waits in the lobby" || { fail "the guest waits in the lobby" "never did"; return; }
sleep "${BB_LOBBY_WAIT:-30}"
send host "click ConfirmButton"
wait_for client "leaving its lobby" 60 && pass "the guest's lobby closes" || { fail "the guest's lobby closes" "still up"; return; }
run_ticks 30
expect_log "the guest takes the host's start state" client "The host started: setting the random state to"
expect_in_sync "in sync after the wait"
expect_no_errors "no errors"
bb_close
