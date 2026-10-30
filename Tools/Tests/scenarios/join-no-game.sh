# A guest that gets nothing at all from the host for 30 s while it waits for the game stops waiting and says so, and
# joining again works. A rejoin over a stuck Steam session gets nothing, and would otherwise wait until the player
# cancels: here the host's first connection gets nothing the host sends (BB_SILENT_FIRST)
export BB_SILENT_FIRST=1
bb_close
bb_launch host || { unset BB_SILENT_FIRST; fail "the host starts" "no main menu"; return; }
bb_launch client || { unset BB_SILENT_FIRST; fail "the guest starts" "no main menu"; return; }
unset BB_SILENT_FIRST
bb_stack_windows
send host "hostnew ${BB_MAP:-Waterfalls} Folktails ${BB_MODE:-}"
wait_for_watch host "Reading map with length" 120 || { fail "the host hosts" "never did"; return; }
send client "joinmatch Folktails"
wait_for host "This connection gets nothing the host sends" 30 || { fail "the first connection is the silent one" "no such line"; return; }
wait_for client "Nothing from the host in 30 s, and no game: not waiting any longer" 60 && pass "the guest stops waiting after 30 s" \
    || { fail "the guest stops waiting after 30 s" "no such line"; return; }
sleep 2
send client "dismiss"; sleep 2
# OK on the failed join's dialog opens the troubleshooting page: a test copy only logs it
expect_log "the dialog's link is logged, not opened" client "Would open https://github.com/.*#troubleshooting"
send client "joinmatch Folktails"
wait_for client "waiting in its lobby" 120 && pass "joining again gets the game" || fail "joining again gets the game" "the guest never got in"
expect_no_errors "no errors"
bb_close
