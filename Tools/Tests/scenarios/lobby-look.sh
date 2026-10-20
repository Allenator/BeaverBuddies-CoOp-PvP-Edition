# The hosting box with a player in it, before and after the guest has loaded the game
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "hostnew ${BB_MAP:-Waterfalls} Folktails ${BB_MODE:-}"
wait_for_watch host "Reading map with length" 120 || { fail "the box comes up" "never hosted"; return; }
sleep 3
shot "alone" host
send client "joinmatch IronTeeth"
sleep 5
shot "joining" host
for ((i = 0; i < 90; i++)); do
    grep -aq "Load time" "$(bb_log client)" && break
    sleep 1
done
sleep 12
shot "with-guest" host
send host "click CopyCodeButton"; sleep 1
shot "copied" host
send host "click PlayerSettingsButton"; sleep 2
shot "settings" host
send host "click CancelButton"; sleep 1
shot "guest" client
expect_log "the guest waits in the lobby" client "waiting in its lobby"
send host "click ConfirmButton"; sleep 8
shot "guest-after" client
shot "host-after" host
bb_close
