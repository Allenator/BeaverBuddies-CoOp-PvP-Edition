# BeaverBuddies on its own, with Timber Empires switched off: the New Game page is the game's own
# with no mode to pick, and a hosted game starts as Co-op and runs in sync, with the Trading Post,
# Power Exchange and toll station in the toolbar and building.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
new_game_page host
expect_nonempty "the page is up: its difficulties are laid out" "$(layout_of host Modes)"
l=$(layout_of host GameModesColumn)
expect_eq "no game mode column with only Classic and Co-op to pick" "$l" ""
shot "new-game-page" host
bb_close

start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
expect_log "the game starts as Co-op" host "Starting a game with options: bb.gamemode=bb.coop"
send host "speed $BB_SPEED"; sleep 10
# BeaverBuddies' own buildings, in the Folktails toolbar and going up on the host's road
for t in TradingPost.Folktails PowerExchange.Folktails TollStation.Folktails; do send host "hastool $t"; done
for x in $(seq 74 87); do send host "place Path $x 45 5 instant"; done
send host "place TradingPost.Folktails 74 46 5 0 instant"
send host "place PowerExchange.Folktails 80 46 5 0 instant"
send host "place TollStation.Folktails 86 46 5 0 instant"
sleep 5
send host "where 74 46 5"; send host "where 80 46 5"; send host "where 86 46 5"; sleep 2
for t in TradingPost.Folktails PowerExchange.Folktails TollStation.Folktails; do
    expect_log "the $t tool is in the toolbar" host "Has tool $t: True"
    # The Trading Post and Power Exchange go down as a pair of halves, a site even when instant
    expect_log "a $t goes down" host "\[Test\] $t.* at \("
done
send host "look 80 46 5 0.4"; send host "pick 75 46 5"; sleep 1; shot "trading-post" host
expect_log "the Trading Post's panel opens" host "selectable TradingPost"
expect_in_sync "both copies in sync"
if mod_installed TimberEmpires; then fail "Timber Empires is off" "its folder is still in Mods"; else pass "Timber Empires is off"; fi
expect_no_log "nothing of Timber Empires loaded" host "\[TimberEmpires\]"
expect_no_errors "no errors"
