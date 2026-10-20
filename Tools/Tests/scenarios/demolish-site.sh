# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# A construction site demolished while a builder is on it: the demolition has to take the site away
# for both copies at the same moment, or one's builder finishes it and the other's walks off.
bb_close
start_match Folktails Folktails || { fail "a match starts" "never started"; return; }
place_starts Folktails Folktails
send host "speed 3"; sleep 5
for i in $(seq 1 25); do
    x=$((80 + i % 9)); y=$((66 + i % 5))
    send client "place Path $x $y 5"
    sleep 1.5
    send client "delete $x $y 5"
    sleep 1
    x=$((77 + i % 7)); y=$((47 + i % 4))
    send host "place Path $x $y 5"
    sleep 1.5
    send host "delete $x $y 5"
    sleep 1
done
sleep 10
expect_no_log "no desync on the host" host "Resyncing everyone after the desync"
expect_no_log "no desync on the client" client "Desync detected"
grep -a -A10 "========== Summary ==========" "$(bb_log client)" | head -12
expect_in_sync "in sync"
