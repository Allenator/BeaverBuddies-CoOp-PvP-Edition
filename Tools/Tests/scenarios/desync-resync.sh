# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# A desync gets everyone back in sync without leaving the game: the copy that desynced tells the host,
# the host saves and sends the save over the connection they already have, and both load it. Once
# back, the dialog's report button opens Discord, where every player's Player.log goes, and with
# tracing on saves the desync's trace next to Player.log first; without tracing, a button of its own
# beside it turns tracing on for everyone, saying so on every copy's screen. Nothing is posted anywhere.
# Tracing off, as a player has it by default: the scenario is about it being turned on from the dialog
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 0
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
# Each player's District Center first, so nothing a mod asks of a player without one covers the
# dialog the resync ends with
place_starts Folktails Folktails
run_ticks 10
send host "speed $BB_SPEED"
send client "desync traced"
wait_for host "Resyncing everyone after the desync of" 30 && wait_for host "Reloading everyone \([0-9]+ players?\) from a save of [0-9]+ bytes" 30 \
    && pass "the host resyncs" || fail "the host resyncs" "it never sent its save"
wait_for client "Received the host's reload save \([0-9]+ bytes\)" 60 && pass "the save reaches the client over the same connection" \
    || fail "the save reaches the client over the same connection" "it never arrived"
wait_for host "Back in sync after a resync" 120 && wait_for client "Back in sync after a resync" 120 \
    && pass "both copies load it" || fail "both copies load it" "one never finished loading"
expect_no_log "nobody left the game" client "Closing EventIO"
sleep 3
shot "resynced-host" host
shot "resynced-client" client
send host "click InfoButton"; sleep 2
send client "click InfoButton"; sleep 2
expect_log "with tracing on, the trace is saved" client "Desync trace saved to .*BeaverBuddies-desync-"
expect_log "and Discord is where to report it" client "Would open https://discord"
expect_no_log "without tracing, nothing is saved" host "Desync trace saved"
expect_log "...and Discord is still where to report it, with every Player.log" host "Would open https://discord"
send host "click TracingButton"; sleep 3
expect_log "the dialog's own button turns tracing on for everyone" host "Turning on desync tracing for everyone"
expect_log "it plays on the guest too" client "Playing TracingEnabledEvent by .*: Turning on desync tracing for everyone"
for copy in host client; do expect_log "the screen says tracing is on ($copy)" $copy "Desync tracing is on: told the player"; done
trace=$(grep -aoE "Desync trace saved to .*" "$(bb_log client)" | head -1 | sed 's/^Desync trace saved to //')
if [ -n "$trace" ] && [ -f "$trace" ]; then pass "the trace file is there"; rm -f "$trace"; else fail "the trace file is there" "no file at '$trace'"; fi
send host dismiss; send client dismiss; sleep 1
run_ticks 25
expect_in_sync "the game carries on in sync"
expect_eq "...with no second desync once logging's on for both, turned on at different moments" "$(grep -ac "Resyncing everyone after the desync" "$(bb_log host)")" "1"
expect_no_log "nothing posted" host "api.airtable.com"
