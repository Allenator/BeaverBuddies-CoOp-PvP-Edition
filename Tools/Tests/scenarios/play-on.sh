# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# After everyone reloads for a desync, the host's game plays on at the speed it was at once every player is back, with
# "Play on after a resync or a join" on; off, it waits paused for the host, as it always did. A game left running
# would otherwise stay paused until someone came back, for hours
ticks() { grep -ac "Tick [0-9]* IO done" "$(bb_log "$1")"; }
resync() {
    local n; n=$(grep -ac "Received the host's reload save" "$(bb_log client)")
    send client "desync"
    for ((i = 0; i < 90; i++)); do
        [ "$(grep -ac "Received the host's reload save" "$(bb_log client)")" -gt "$n" ] && break
        sleep 1
    done
    sleep 15
    send host dismiss; send client dismiss; sleep 1
}
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
# The host's speed button: the game runs at 3 before each resync
running() {
    send host "play 3"; sleep 5
    local t; t=$(ticks client); sleep 3
    [ "$(ticks client)" -gt "$t" ] && pass "the game runs at speed 3 ($1)" || fail "the game runs at speed 3 ($1)" "the guest's ticks stood at $t"
}
running off
# Off, as by default: paused after the resync
expect_eq "it's off" "$(said host modsetting PlayOnAfterReload 0)" "False"
resync
t=$(ticks client); sleep 8
expect_eq "off, the game waits paused after a resync" "$(ticks client)" "$t"
expect_no_log "off, nothing says it plays on" host "Playing on at speed"
expect_log "the reload says it was a resync from speed 3" host "Why everyone reloads: Resync, from speed 3"
# On: back at speed 3 once the guest is back
expect_eq "the host turns it on" "$(said host modsetting PlayOnAfterReload 1)" "True"
running on
resync
wait_for host "Playing on at speed 3: 1 of 1 players back" 60 && pass "on, the host plays on at speed 3 once the guest is back" \
    || fail "on, the host plays on at speed 3 once the guest is back" "no such line"
t=$(ticks client); sleep 8
[ "$(ticks client)" -gt "$t" ] && pass "the guest plays on too" || fail "the guest plays on too" "its ticks stood at $t"
expect_in_sync "in sync"
