# A player who leaves while joining a game in progress doesn't have everyone reload: the host saves the game for them
# (slowly here, BB_SLOW_JOIN_SAVE ms), they press Cancel meanwhile, and once it's saved the host sees they're gone,
# skips the reload and plays on at the speed it was at, its guest with it (upstream's fe74d66). The host only looks
# as the save is done, so a player who leaves later still has everyone reload. Over WebRTC join codes, which mark a
# connection closed as the other side closes it: Steam marks a failed session, while TCP says a connection is open
# until a read fails, and nothing reads from a player waiting for the map. They join again after it: that reload is
# for them, and the game plays on in sync
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)
export BB_JOIN_ADDRESS="rtc:$BB_RTC_CODE"
export BB_SLOW_JOIN_SAVE=${BB_SLOW_JOIN_SAVE:-6000}
bail() { fail "$1" "$2"; bb_kill third; unset BB_SLOW_JOIN_SAVE BB_RTC_CODE BB_JOIN_ADDRESS; }
last_tick() { grep -aEo "Tick [0-9]+ IO done" "$(bb_log host)" | tail -1 | grep -oE "[0-9]+" | sed 's/^0*//'; }
start_match Folktails IronTeeth || { bail "a game starts" "never did"; return; }
pass "a game starts"
send host "speed 1"; sleep 5
loads=$(grep -ac "Load time" "$(bb_log host)")
bb_launch third || { bail "a third copy starts" "no main menu"; return; }
send third "join $BB_JOIN_ADDRESS"
wait_for host "\[Test\] The join's save takes" 120 && pass "the host saves the game for the player joining" \
    || { bail "the host saves the game for the player joining" "no join save"; return; }
send third "click CancelButton"
wait_for third "Cancelled joining before the map arrived" 10 && pass "the player joining cancels" || fail "the player joining cancels" "no cancel"
wait_for host "The player joining left before the game was saved: not reloading|Reloading everyone" 30 && grep -aq "not reloading" "$(bb_log host)" && pass "the host sees they left and doesn't reload" \
    || { bail "the host sees they left and doesn't reload" "it reloaded"; return; }
t0=$(last_tick); sleep 6; t1=$(last_tick)
[ "${t1:-0}" -gt "${t0:-0}" ] && pass "the host plays on at its speed (ticks $t0 to $t1)" || fail "the host plays on at its speed" "stuck at tick ${t0:-none}"
[ "$(grep -ac "Load time" "$(bb_log host)")" -eq "$loads" ] && pass "nobody reloads" || fail "nobody reloads" "the host loaded again"
expect_no_log "the guest is sent no reload" client "Received the host's reload save"
run_ticks 30
expect_in_sync "the two in the game stay in sync"

cloads=$(grep -ac "Load time" "$(bb_log client)")
# The same copy, back at the main menu since it cancelled
sleep 3
send third "join $BB_JOIN_ADDRESS"
wait_for host "Reloading everyone \([0-9]+ players\)" 120 && pass "joining again reloads everyone for them" \
    || { bail "joining again reloads everyone for them" "no reload"; return; }
wait_for third "Load time" 120 && pass "the player joins on their second try" || { bail "the player joins on their second try" "never loaded"; return; }
for ((i = 0; i < 120; i++)); do [ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$cloads" ] && break; sleep 1; done
[ $i -lt 120 ] && pass "the guest already in reloads too" || fail "the guest already in reloads too" "no second load"
sleep 5
send host dismiss; send client dismiss; send third dismiss; sleep 1
run_ticks 30
expect_in_sync "in sync after the reload"
bb_kill third
expect_no_errors "no errors"
bb_close
unset BB_SLOW_JOIN_SAVE BB_RTC_CODE BB_JOIN_ADDRESS
