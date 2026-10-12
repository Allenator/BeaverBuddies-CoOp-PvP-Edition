# Demolishing a finished building with the demolish tool, as a player does: it has to take the
# building down on both copies, without throwing on the way.
bb_close
start_match Folktails Folktails || { fail "a match starts" "never started"; return; }
place_starts Folktails Folktails
send host "speed 3"
send host "place GathererFlag.Folktails 81 49 5 0 instant"
send client "place GathererFlag.Folktails 85 66 5 0 instant"
sleep 3
send host "finish 81 49 5"; send client "finish 85 66 5"
sleep 2
send host "tooldelete 81 49 5"
send client "tooldelete 85 66 5"
wait_for host "Tool deleted 1 at 81 49 5" 20
wait_for client "Tool deleted 1 at 85 66 5" 20
sleep 5
expect_no_log "no error on the host" host "tooldelete .* failed"
expect_no_log "no error on the client" client "tooldelete .* failed"
expect_no_match "the host's flag is gone for the guest" "$(probe client "at 81 49 5")" "GathererFlag"
expect_no_match "the guest's flag is gone for the host" "$(probe host "at 85 66 5")" "GathererFlag"
expect_in_sync "in sync"
