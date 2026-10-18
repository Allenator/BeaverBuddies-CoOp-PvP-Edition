# A game with Max players at 2: the guest joins, and a third copy trying to join after is turned away
# (see Connect/PlayerLimit.cs) while the two in it play on in sync.
export BB_HOST_SETTINGS="bb.maxplayers=2"
start_match Folktails IronTeeth || { fail "a 2-player game starts" "the copies never got into the game"; unset BB_HOST_SETTINGS; return; }
unset BB_HOST_SETTINGS
pass "a 2-player game starts"
expect_log "the host starts it with the limit" host "Starting a game with settings: .*bb\.maxplayers=2"

bb_launch third || { fail "a third copy starts" "no main menu"; bb_kill third; return; }
send third "join 127.0.0.1"
if wait_for host "Turning a client away: 1 of 1 already in" 60; then pass "the third player is turned away"; else fail "the third player is turned away" "the host let them in"; fi
if wait_for third "GameFull" 30; then pass "the third player is told the game is full"; else fail "the third player is told the game is full" "no error"; fi
sleep 2
shot "third-turned-away" third
expect_no_log "the third copy never loads the game" third "Registering Co-op services"

send host "speed $BB_SPEED"; sleep 10
send host "place Path 74 45 5 instant"; send client "place Path 76 45 5 instant"; sleep 5
expect_in_sync "the two in the game stay in sync"
bb_kill third
expect_no_errors "no errors"
