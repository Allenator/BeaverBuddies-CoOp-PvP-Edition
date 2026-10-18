# The "Steam friends only" box under Tutorial on the Host page: shown only when hosting, and a game hosted with it ticked stays off the list of open matches.
# A hosted game without it is listed. The same box shows beside Host on Host saved game, and keeps a loaded save off the list.
bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
new_game_page host match
send host "friendsbox"; sleep 1
expect_log "the box is hidden on the Find a match page" host "friendsbox shown False ticked False"
bb_close
bb_launch host || { fail "the game starts again" "no main menu"; return; }
new_game_page host
send host "friendsbox"; sleep 1
expect_log "the box shows on the Host page" host "friendsbox shown True ticked False"
shot "friends-only-box" host
send host "friendsbox on"; sleep 1
expect_log "ticking it sticks" host "friendsbox shown True ticked True"
send host "click HostNewGameButton"
sleep 75
expect_no_log "a game hosted friends only isn't listed" host "\[Match\] Listing the hosted game"
expect_no_errors "no errors with it ticked"
bb_close
bb_launch host || { fail "the game starts a third time" "no main menu"; return; }
new_game_page host
send host "click HostNewGameButton"
if wait_for host "\[Match\] Listing the hosted game" 120; then pass "a game hosted without it is listed"; else fail "a game hosted without it is listed" "no listing in 120 s"; fi
expect_no_errors "no errors without it"
bb_close
bb_launch host || { fail "the game starts a fourth time" "no main menu"; return; }
for c in "click MultiplayerButton" "click HostSavedGameButton"; do send host "$c"; sleep 2; done
send host "friendsbox"; sleep 1
expect_log "the box shows on Host saved game" host "friendsbox shown True ticked False"
shot "friends-only-save-box" host
send host "friendsbox on"; sleep 1
send host "click HostButton"
sleep 75
expect_no_log "a save hosted friends only isn't listed" host "\\[Match\\] Listing the hosted game"
expect_no_errors "no errors hosting a save"
