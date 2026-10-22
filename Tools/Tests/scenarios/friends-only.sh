# The "Steam friends only" box under Tutorial on the Host page: shown only when hosting, and a game hosted with it ticked stays off the list of open matches.
# A hosted game without it is listed. The same box shows beside Host on Host saved game, and keeps a loaded save off the list: the save the game
# hosted without it made, picked by name, which is listed when hosted without the box too. Each "isn't listed" is checked once the game is
# hosted, so a game that never got hosted can't pass it
re_quote() { printf '%s' "$1" | sed 's/[][\.*^$(){}?+|/]/\\&/g'; }
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
if wait_for host "Reading map" 120; then pass "a game is hosted friends only"; else fail "a game is hosted friends only" "not hosted in 120 s"; fi
sleep 60
expect_no_log "a game hosted friends only isn't listed" host "\[Match\] Listing the hosted game"
expect_no_errors "no errors with it ticked"
bb_close
bb_launch host || { fail "the game starts a third time" "no main menu"; return; }
new_game_page host
send host "click HostNewGameButton"
if wait_for host "\[Match\] Listing the hosted game" 120; then pass "a game hosted without it is listed"; else fail "a game hosted without it is listed" "no listing in 120 s"; fi
expect_no_errors "no errors without it"
# The save hosting it made, for Host saved game, from the game's "Saving game to <settlement> - <save> at <time>"
saved=$(sed -nE 's/.*Saving game to (Co-op [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9]{2}h[0-9]{2}) - (.+) at [0-9]{4}-[0-9]{2}-[0-9]{2} [0-9:]+Z.*/\1|\2/p' "$(bb_log host)" | tail -1)
bb_close
[ -n "$saved" ] || { fail "the hosted game is saved" "no \"Saving game to\" line in the host log"; return; }

# host_saved <name> [on]: Host saved game, the save picked and the box shown, ticked with on, and Host pressed; 1 if it isn't hosted
host_saved() {
    bb_launch host || { fail "the game starts for $1" "no main menu"; return 1; }
    for c in "click MultiplayerButton" "click HostSavedGameButton"; do send host "$c"; sleep 2; done
    send host "selectsave $saved"; sleep 1
    grep -aEq "\[Test\] selectsave: $(re_quote "$saved")" "$(bb_log host)" || { fail "$1" "the save $saved isn't picked"; return 1; }
    send host "friendsbox${2:+ $2}"; sleep 1
    send host "click HostButton"
    wait_for host "Host saved game: $(re_quote "$saved")" 10 && wait_for host "Reading map" 60 || { fail "$1" "Host didn't host it"; return 1; }
}
if host_saved "a save is hosted without the box"; then
    expect_log "the box shows on Host saved game" host "friendsbox shown True ticked False"
    shot "friends-only-save-box" host
    pass "a save is hosted without the box"
    if wait_for host "\[Match\] Listing the hosted game" 120; then pass "a save hosted without it is listed"; else fail "a save hosted without it is listed" "no listing in 120 s"; fi
    expect_no_errors "no errors hosting a save without it"
fi
bb_close
if host_saved "a save is hosted friends only" on; then
    expect_log "the box ticks on Host saved game" host "friendsbox shown True ticked True"
    pass "a save is hosted friends only"
    sleep 60
    expect_no_log "a save hosted friends only isn't listed" host "\[Match\] Listing the hosted game"
    expect_no_errors "no errors hosting a save friends only"
fi
bb_close
