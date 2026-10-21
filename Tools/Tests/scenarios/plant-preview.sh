# Hovering a planting button builds a preview plant for the tool's description, and that plant wakes
# up without an entity: the death delay fix has to leave it alone instead of throwing.
bb_close
start_match Folktails Folktails || { fail "a match starts" "never started"; return; }
pass "a match starts"
send host "speed $BB_SPEED"; sleep 5
send host "plantpreview"
send client "plantpreview"
wait_for host "Planting previews: [1-9]" 30
wait_for client "Planting previews: [1-9]" 30
expect_no_log "no error on the host" host "plantpreview failed"
expect_no_log "no error on the guest" client "plantpreview failed"
expect_in_sync "in sync"
expect_no_errors "no errors"
