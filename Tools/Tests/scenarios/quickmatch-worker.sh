# With Steam networking off, as the Epic Games build plays: Quick match through the Worker's lobbies
# (Matchmaking/Lobbies/WorkerLobbies.cs). The first copy waits in a lobby of its own, the second finds it,
# they settle the match, one hosts and the other joins its game by join code over WebRTC, and they play in
# sync. Through Steam, the two test copies share an account and can't get this far.
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)

bb_close
bb_launch host || { fail "the first copy starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_launch client || { fail "the second copy starts" "no main menu"; unset BB_RTC_CODE; return; }
bb_stack_windows
new_game_page host match
send host "click FindMatchButton"
if wait_for host "\[Match\] Waiting in [0-9]+" 40; then pass "the first waits in a Worker lobby"; else fail "the first waits in a Worker lobby" "no lobby"; unset BB_RTC_CODE; return; fi
shot "waiting" host
# Open matches lists it too, with its store
send client "click MultiplayerButton"; sleep 1
send client "openmatches"
wait_for client "\[Match\] .* open matches, .* on the Worker" 30
sleep 1; send client "listmatches"; sleep 1
expect_log "the waiting player is in Open matches" client "\[Test\] Open match .* platform=steam code= lobby=[0-9]+ hosted=False compatible=True"
shot "open-matches" client
send client "click CloseButton"; sleep 1
# Back on the Multiplayer page Open matches was opened from: on to Find a match from there
for c in "click FindMatchMenuButton" "click NextButton" "click NextButton"; do send client "$c"; sleep 2; done
send client "click FindMatchButton"
if wait_for client "\[Match\] Matched with" 40 && wait_for host "\[Match\] .* joined [0-9]+" 20; then pass "the second finds and joins it"; else fail "the second finds and joins it" "no match"; unset BB_RTC_CODE; return; fi
if wait_for host "\[Match\] Settled with" 30 && wait_for client "\[Match\] Settled with" 30; then pass "they settle the match"; else fail "they settle the match" "no result"; unset BB_RTC_CODE; return; fi
shot "settled" client

# The coin flip picks who hosts
if grep -aq "\[Match\] Settled with .*: hosting" "$(bb_log host)"; then hoster=host; joiner=client; else hoster=client; joiner=host; fi
if wait_for $hoster "\[Match\] Hosting the game with code $BB_RTC_CODE" 180; then pass "the host passes on its join code"; else fail "the host passes on its join code" "no code"; unset BB_RTC_CODE; return; fi
if wait_for $joiner "\[WebRTC\] Connected to $BB_RTC_CODE" 40; then pass "the other joins its game over WebRTC"; else fail "the other joins its game over WebRTC" "no connection"; unset BB_RTC_CODE; return; fi
for ((i = 0; i < 180; i++)); do
    sed -n '/Registering Co-op/,$p' "$(bb_log $joiner)" | grep -aq "Load time" \
        && sed -n '/Registering Co-op/,$p' "$(bb_log $hoster)" | grep -aq "Load time" && break
    sleep 1
done
expect_log "the match's game starts by itself" $hoster "\[Match\] The game is starting"
sleep 5; send host dismiss; send client dismiss; sleep 2
send $hoster "speed $BB_SPEED"; sleep 10
expect_in_sync "both copies in sync"
expect_no_errors "no errors"
unset BB_RTC_CODE
