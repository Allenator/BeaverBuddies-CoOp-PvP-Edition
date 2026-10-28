# How much of the game's slowing of fast speeds in big colonies is lifted is the host's choice, for every player:
# every machine plays the host's ticks at its own pace, so a guest left slower would fall behind. The guest's own
# choice is the game's (BB_THROTTLE_CLIENT); the host lifts all of it, then half, in the running copy (throttle set),
# and the guest has to follow each within a few seconds, from the host's presence. The test map's population is too
# small to be slowed, so the scale is checked for the 292 characters of the group's game of October 26, 2026: 0.40
# as the game has it, 1.00 with none of it, 0.70 with half
scale() { said $1 throttle 292 | cut -d' ' -f1; }
export BB_THROTTLE_CLIENT=0
bb_close
start_match Folktails Folktails || { unset BB_THROTTLE_CLIENT; fail "a match starts" "the copies never got into the game"; return; }
unset BB_THROTTLE_CLIENT
pass "a match starts"
expect_eq "the game's own at first (host)" "$(scale host)" "0.40"
expect_eq "the game's own at first (guest)" "$(scale client)" "0.40"
said host throttle set 2 > /dev/null; sleep 5
expect_eq "none of it, on the host" "$(scale host)" "1.00"
expect_eq "none of it, on the guest too" "$(scale client)" "1.00"
said host throttle set 1 > /dev/null; sleep 5
expect_eq "half, on the host" "$(scale host)" "0.70"
expect_eq "half, on the guest too" "$(scale client)" "0.70"
expect_log "the guest applied it" client "Fast speeds in big colonies: 50% of the game's slowing lifted"
send host "speed 3"; sleep 3
run_ticks 30
expect_in_sync "in sync"
expect_no_errors "no errors"
