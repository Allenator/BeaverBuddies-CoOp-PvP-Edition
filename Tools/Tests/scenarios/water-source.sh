# A water source's dev mode boxes, Strength and Contamination, change it for everyone. Neither was sent: the strength
# changed on the machine of the player who typed it in alone, and the contamination too, until the next badtide or
# load. Here the guest types in both, with dev mode on, and both copies' water sources have to be the same after
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3
list=$(said host watersource list)
first=$(echo "$list" | grep -oE "\([0-9]+,[0-9]+,[0-9]+\)" | head -1)
[ -n "$first" ] || { fail "the map has a water source" "none: $list"; return; }
pass "the map has a water source at $first"
at=$(echo "$first" | tr -d '()' | tr ',' ' ')
send client "devmode"; sleep 1
send client "watersource $at strength 3.5"; sleep 3
send client "watersource $at contamination 40"; sleep 3
host_list=$(said host watersource list); client_list=$(said client watersource list)
expect_eq "both copies have the same water sources" "$client_list" "$host_list"
expect_match "the host's has the guest's strength and contamination" "$host_list" "$(echo "$first" | sed 's/[()]/\\&/g') 3\.50 0\.40"
run_ticks 30
expect_in_sync "in sync"
expect_no_errors "no errors"
