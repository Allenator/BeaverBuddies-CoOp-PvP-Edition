# A saved game hosted with the Load game box's Host: the host waits over the save loaded once, the guest waits in the lobby, named in the
# host's box, and what it does there isn't sent; Start loads the save again and they play it in sync. BB_FROM=game hosts it from inside the
# game that saved it, not from the main menu; BB_JOIN=code joins by join code over WebRTC with Steam networking off, not at 127.0.0.1
if [ "${BB_JOIN:-ip}" == code ]; then
    pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
    export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)
    export BB_JOIN_ADDRESS="rtc:$BB_RTC_CODE"
fi
bail() { fail "$1" "$2"; unset BB_RTC_CODE BB_JOIN_ADDRESS; }
bb_close
bb_launch host || { bail "the host starts" "no main menu"; return; }
bb_launch client || { bail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "newgame ${BB_MAP:-Waterfalls} Folktails"
wait_for_watch host "Load time" 180 || { bail "the game to save loads" "never did"; return; }
sleep 3
send host "save"
wait_for host "\[Test\] saved" 60 || { bail "the game is saved" "never was"; return; }
save=$(ls -t "$HOME/Documents/Timberborn/Saves"/*/*.timber | head -1)
settlement=$(basename "$(dirname "$save")")
name=$(basename "$save" .timber)
if [ "${BB_FROM:-menu}" == menu ]; then
    n=$(grep -ac "Registering Main Menu Services" "$(bb_log host)")
    send host "exitmenu nosave"
    for ((i = 0; i < 60; i++)); do
        [ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$n" ] && break
        sleep 1
    done
fi
loads=$(grep -ac "Load time" "$(bb_log host)")
send host "hostsaveflow $settlement|$name"
wait_for_watch host "Server started listening" 60 || { bail "the host hosts the save" "no server"; return; }
send client "joinmatch Folktails ${BB_JOIN_ADDRESS:-}"
wait_for client "waiting in its lobby" 180 && pass "the guest waits in the lobby" || { bail "the guest waits in the lobby" "never did"; return; }
[ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && pass "the host waits over the save, loaded once" || fail "the host waits over the save, loaded once" "no load"
wait_for host "\[Lobby\] .* said hello" 30 && pass "the host's box names the guest" || fail "the host's box names the guest" "no hello read"
sleep 3
shot "lobby-host" host
shot "lobby-guest" client
send client "speedkey 1"
send client "play 2"
sleep 2
expect_log "what the guest does in the lobby isn't sent" client "Not sending .*: the host hasn't started the game"
loads=$(grep -ac "Load time" "$(bb_log host)")
send host "click ConfirmButton"
for ((i = 0; i < 180; i++)); do
    [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && break
    sleep 1
done
[ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads" ] && pass "Start loads the save again" || { bail "Start loads the save again" "no second load"; return; }
wait_for client "leaving its lobby" 60 && pass "the guest's lobby closes" || fail "the guest's lobby closes" "still up"
expect_no_log "the guest sent nothing while it waited" host "Discarded [0-9]+ events"
send host "speed 1"; sleep 15
expect_in_sync "they play the same game"
expect_no_errors "no errors"
shot "playing" host
bb_close
unset BB_RTC_CODE BB_JOIN_ADDRESS
