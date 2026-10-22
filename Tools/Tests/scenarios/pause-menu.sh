# What pauses everyone is the host's choice (Settings.PauseReductionSetting): under the host's "nothing" a guest's menu is its own and the
# game plays on, though the guest's own choice is to pause for the menu; under the host's "the menu only" a guest's menu pauses everyone, though
# its own choice is nothing. A save the host makes itself is written paused (Settings.PauseHostSaves), and the game plays on in sync. Each
# copy's own choice: BB_PAUSES_HOST, BB_PAUSES_CLIENT
pauses_done() { unset BB_PAUSES_HOST BB_PAUSES_CLIENT; }
bail() { fail "$1" "$2"; bb_close; pauses_done; }
last_tick() { grep -aEo "Tick [0-9]+ IO done" "$(bb_log "$1")" | tail -1 | grep -oE "[0-9]+" | sed 's/^0*//'; }

# The host's "nothing" over the guest's "the menu only"
export BB_PAUSES_HOST=2 BB_PAUSES_CLIENT=1
start_match Folktails Folktails || { bail "a match starts" "never got into the game"; return; }
send host "speed 1"; sleep 5
before=$(last_tick host)
send client "menu"
sleep 5
after=$(last_tick host)
[ "${after:-0}" -gt "${before:-0}" ] && pass "the guest's menu doesn't pause the game (host's choice: nothing)" \
    || fail "the guest's menu doesn't pause the game (host's choice: nothing)" "the host stayed at tick $after"
send client "toppanel"; sleep 1
expect_log "the guest's menu is open on its own screen" client "toppanel: GameOptionsBox"
send host "toppanel"; sleep 1
expect_no_log "and not on the host's" host "toppanel: GameOptionsBox"
send client "escape"; sleep 1

# A save the host makes itself
send host "save"
wait_for host "\[Test\] saved" 60 && pass "the host saves" || fail "the host saves" "never did"
expect_log "the host's save is written paused" host "Pausing for the host's save"
expect_log "and the game plays on after it" host "Playing on at speed 1 after the host's save"
sleep 8
expect_in_sync "they stay in sync after the host's save"
bb_close

# The host's "the menu only" over the guest's "nothing"
export BB_PAUSES_HOST=1 BB_PAUSES_CLIENT=2
start_match Folktails Folktails || { bail "a match starts again" "never got into the game"; return; }
send host "speed 1"; sleep 5
send client "menu"
wait_for host "Event: Changing speed from [0-9.]+ to 0" 30 && pass "the guest's menu pauses everyone (host's choice: the menu only)" \
    || fail "the guest's menu pauses everyone (host's choice: the menu only)" "the host didn't pause"
sleep 3
before=$(last_tick host); sleep 4; after=$(last_tick host)
[ "${after:-0}" == "${before:-0}" ] && pass "the game stays paused while the menu's up" || fail "the game stays paused while the menu's up" "tick $before to $after"
expect_no_errors "no errors"
bb_close
pauses_done
