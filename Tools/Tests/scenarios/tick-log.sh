# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# A player's log has the tick's hash line every 100th tick, not every tick (13 MB of a night's 20 MB, October 25, 2026),
# and the last ticks' lines are written out, as they were, when a desync or a resync wants them. The copies log as a
# player's game does (BB_TICKLOG_LIVE): after 250 ticks, the 100th and 200th are there and the 150th isn't; then the
# guest desyncs, and its log has the ticks before, as does the host's at the resync
export BB_TICKLOG_LIVE=1
bb_close
start_match Folktails Folktails || { unset BB_TICKLOG_LIVE; fail "a match starts" "the copies never got into the game"; return; }
unset BB_TICKLOG_LIVE
pass "a match starts"
run_ticks 250
expect_log "the 100th tick's line" host "Tick 00100 IO done"
expect_log "the 200th tick's line" host "Tick 00200 IO done"
expect_no_log "no line for the 150th" host "Tick 00150 IO done"
send client "desync"
wait_for client "The last [0-9]+ ticks' hashes, at this machine's desync" 30 && pass "the guest writes out its last ticks at its desync" \
    || fail "the guest writes out its last ticks at its desync" "no such line"
wait_for host "The last [0-9]+ ticks' hashes, at the resync" 30 && pass "the host writes out its last ticks at the resync" \
    || fail "the host writes out its last ticks at the resync" "no such line"
expect_log "...the 150th among them" client "Tick 00150 IO done"
