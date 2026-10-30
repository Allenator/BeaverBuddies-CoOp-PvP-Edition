# BB_EXPECT_DESYNC: this scenario parts the copies on purpose
# A guest whose state parts from the host's in walker positions alone, with tracing off as players play: the guest
# checks its order, move and clock hashes against those the host's heartbeat brings every tick, and has everyone
# resynced within a few ticks, saying what differed, where only the random state was checked before, which can take
# hundreds of ticks to part (290 in a real game's desync). With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 0
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
place_starts Folktails Folktails
send host "speed 1"; sleep 8
expect_no_log "no difference before one is made (guest)" client "State differs from the host's"

send client "nudge"
wait_for client "Nudged .* at tick [0-9]+" 10 || { fail "a beaver is nudged on the guest" "no line saying so"; return; }
nudged=$(grep -aoE "Nudged .* at tick [0-9]+" "$(bb_log client)" | tail -1 | grep -oE "[0-9]+$")
if wait_for client "State differs from the host's at tick [0-9]+: walker positions" 30; then
    seen=$(grep -aoE "State differs from the host's at tick [0-9]+" "$(bb_log client)" | tail -1 | grep -oE "[0-9]+$")
    pass "the guest sees its walker positions differ (nudged at tick $nudged, seen at $seen)"
    if [ $((seen - nudged)) -le 3 ]; then pass "within a few ticks"; else fail "within a few ticks" "$((seen - nudged)) ticks after"; fi
else
    fail "the guest sees its walker positions differ" "no line saying so"
fi
wait_for host "Resyncing everyone after the desync of" 30 && pass "the host resyncs everyone" || fail "the host resyncs everyone" "it never did"
expect_log "the host's log says what differed" host "Playing ClientDesyncedEvent by .*: Out of sync: walker positions differ from the host's since tick"
# ...and which walker, from the guest's record of that tick, without tracing
who=$(grep -aoE "Nudged [0-9a-f]{8}" "$(bb_log client)" | tail -1 | cut -d' ' -f2)
expect_log "the host names the walker nudged" host "walker ${who:-x}[0-9a-f-]* .* at tick [0-9]+: here .*, there "
wait_for host "Back in sync after a resync" 120 && wait_for client "Back in sync after a resync" 120 \
    && pass "both copies load the host's save" || fail "both copies load the host's save" "one never finished loading"
sleep 3
send host dismiss; send client dismiss; sleep 2
send host "speed 1"; sleep 10
expect_eq "no second resync" "$(grep -ac "Resyncing everyone after the desync" "$(bb_log host)")" "1"
expect_in_sync "in sync after the resync"
