# A new game hosted waits in its box behind the loading background, not the map, and Cancel leaves for
# the Multiplayer page of the main menu.
bb_close
bb_launch host || { fail "the copy starts" "no main menu"; return; }
send host "hostmatch ${BB_MAP:-Waterfalls} Folktails ${BB_MODE:-}"
wait_for_watch host "Reading map with length" 120 || { fail "the box comes up" "never hosted"; return; }
sleep 4
shot "box-over-cover" host
expect_log "the cover is up" host "HostingCover shown"
n=$(grep -ac "Registering Main Menu Services" "$(bb_log host)")
send host "click CancelButton"
for ((i = 0; i < 60; i++)); do
    [ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$n" ] && break
    sleep 1
done
[ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$n" ] && pass "cancel leaves for the main menu" || fail "cancel leaves for the main menu" "still in the game"
sleep 3
shot "multiplayer-page" host
bb_close
