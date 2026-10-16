# With Steam networking off, as the Epic Games build plays: a game hosted from the New Game page goes in
# the directory with its join code (see Matchmaking/OpenListings.cs), the other copy finds it in the list
# of open matches with the host's store beside it, joins it from there over WebRTC, and the two play in sync.
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)

bb_close
bb_launch host || { fail "the host starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_launch client || { fail "the guest starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_stack_windows
new_game_page host
send host "click HostNewGameButton"
if wait_for_watch host "\[Match\] Listing the hosted game in the directory as .* with code $BB_RTC_CODE" 150; then
    pass "the hosted game goes in the directory"
else
    fail "the hosted game goes in the directory" "no listing"; unset BB_RTC_CODE; return
fi
expect_no_log "no Steam lobby with Steam networking off" host "\[Match\] Listing the hosted game [0-9]"

send client "click MultiplayerButton"; sleep 1
send client "openmatches"
wait_for client "\[Match\] .* open matches, .* in the directory" 30 || { fail "the list loads" "no answer"; unset BB_RTC_CODE; return; }
sleep 1; send client "listmatches"; sleep 1
expect_log "the game is in the list with the host's store" client "\[Test\] Open match .* platform=steam code=$BB_RTC_CODE lobby=0 hosted=True compatible=True"
shot "open-matches" client
send client "pickcode $BB_RTC_CODE"; sleep 1
shot "open-matches-picked" client
send client "joinpicked"
if wait_for client "\[WebRTC\] Connected to $BB_RTC_CODE" 40; then pass "joining from the list connects over WebRTC"; else fail "joining from the list connects over WebRTC" "no connection"; unset BB_RTC_CODE; return; fi
wait_for host "\[WebRTC\] Player .* connected" 20
sleep 3; shot "hosting-box" host
send host dismiss
for ((i = 0; i < 180; i++)); do
    # Both: the host loads the game it hosted again, and can take longer than the guest
    sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
        && sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -aq "Load time" && break
    sleep 1
done
sleep 5; send host dismiss; send client dismiss; sleep 2
send host "speed $BB_SPEED"; sleep 10
expect_in_sync "both copies in sync"
expect_log "the full game comes off the directory, or stays while there's room" host "\[Match\] .* is off the directory|\[Match\] Listing the hosted game in the directory"
expect_no_errors "no errors"
unset BB_RTC_CODE
