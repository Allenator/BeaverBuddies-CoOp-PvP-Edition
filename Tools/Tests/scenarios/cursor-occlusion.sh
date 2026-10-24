# Another player's cursor is drawn solid where the camera sees what it points at, and see-through
# when terrain or a building stands in the way.
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
send host "speed $BB_SPEED"; sleep 5
for x in $(seq 74 87); do send host "place Path $x 45 5 instant"; done
send host "place PowerExchange.Folktails 80 46 5 0 instant"
sleep 3
send host "look 80 45 5 0.4"; sleep 2

# on the road, in plain view
send host "fakecursor a 78 45 5"; sleep 2; send host "cursors plain"; sleep 1
expect_log "a cursor in plain view is solid" host "Cursor plain alpha 1\.00"
shot "cursor-plain" host

# below the ground
send host "fakecursor a 78 45 5 -2"; sleep 2; send host "cursors buried"; sleep 1
expect_log "a cursor under the ground is see-through" host "Cursor buried alpha 0\.35"

# on the ground under a building
send host "fakecursor a 80 46 5"; sleep 2; send host "cursors building"; sleep 1
expect_log "a cursor under a building is see-through" host "Cursor building alpha 0\.35"
shot "cursor-building" host

# and solid again once it moves out
send host "fakecursor a 78 45 5"; sleep 2; send host "cursors again"; sleep 1
expect_log "a cursor back in view is solid again" host "Cursor again alpha 1\.00"
expect_no_errors "no errors"
