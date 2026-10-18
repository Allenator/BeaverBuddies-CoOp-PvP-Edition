# A hosted game with Max players at 3 on a 4-player map shows 1/3 in Open matches: the limit, not the map's room.
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)

bb_close
bb_launch host || { fail "the host starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_launch client || { fail "the guest starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_stack_windows
send host "gamesetting bb.maxplayers 3"
new_game_page host
send host "click HostNewGameButton"
wait_for_watch host "\[Match\] Listing the hosted game in the Worker's lobby [0-9]+ with code $BB_RTC_CODE" 150 \
    || { fail "the hosted game is listed" "no listing"; unset BB_RTC_CODE; return; }

send client "click MultiplayerButton"; sleep 1
send client "openmatches"
wait_for client "\[Match\] .* open matches, .* on the Worker" 30 || { fail "the list loads" "no answer"; unset BB_RTC_CODE; return; }
sleep 1; send client "listmatches"; sleep 1
expect_log "the list shows the player limit" client "\[Test\] Open match .* code=$BB_RTC_CODE .* players=1/3"
send client "pickcode $BB_RTC_CODE"; sleep 1
shot "open-matches-limit" client
expect_no_errors "no errors"
unset BB_RTC_CODE
