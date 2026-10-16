# How Open matches reads: a hosted game's row (store, map, players) and its details, one you can join and
# then the same one once this copy can't (detailed logging on here only), with what keeps them apart.
# Screenshots to look at, with checks on the text.
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)

bb_close
bb_launch host || { fail "the host starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_launch client || { fail "the guest starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_stack_windows
new_game_page host
send host "click HostNewGameButton"
wait_for_watch host "\[Match\] Listing the hosted game in the Worker's lobby [0-9]+ with code $BB_RTC_CODE" 150 || { fail "the game is listed" "no listing"; unset BB_RTC_CODE; return; }

send client "click MultiplayerButton"; sleep 1
send client "openmatches"
wait_for client "\[Match\] .* open matches, .* on the Worker" 30
sleep 1; send client "listmatches"; sleep 1
expect_log "the game is listed with its players" client "\[Test\] Open match .* code=$BB_RTC_CODE .* compatible=True"
send client "pickcode $BB_RTC_CODE"; sleep 1
shot "joinable" client

send client "templogging on"
send client "showincompatible"; sleep 4
send client "listmatches"; sleep 1
expect_log "with logging on here only, it can't be joined" client "\[Test\] Open match .* code=$BB_RTC_CODE .* compatible=False"
send client "pickcode $BB_RTC_CODE"; sleep 1
shot "not-joinable" client
expect_no_errors "no errors"
unset BB_RTC_CODE
