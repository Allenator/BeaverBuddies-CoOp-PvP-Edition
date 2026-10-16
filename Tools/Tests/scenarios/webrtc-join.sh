# With Steam networking off, as the Epic Games build plays, the guest joins the host by its join code
# over WebRTC (through the signaling Worker, Signaling/), not by address, and the two stay in sync.
pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)
export BB_JOIN_ADDRESS="rtc:$BB_RTC_CODE"

start_match Folktails IronTeeth || { fail "a match starts by join code" "the copies never got into the game"; unset BB_RTC_CODE BB_JOIN_ADDRESS; return; }
pass "a match starts by join code"
expect_log "the host opens its room" host "\[WebRTC\] Hosting with code $BB_RTC_CODE"
expect_log "the guest joins by code" client "Joining by code: $BB_RTC_CODE"
expect_log "the guest connects over WebRTC" client "\[WebRTC\] Connected to $BB_RTC_CODE"
expect_log "the host takes the player over WebRTC" host "\[WebRTC\] Player .* connected"
send host "speed $BB_SPEED"; sleep 10
send host "place Path 74 45 5 instant"; send client "place Path 76 45 5 instant"; sleep 5
expect_in_sync "both copies in sync"
expect_no_errors "no errors"
unset BB_RTC_CODE BB_JOIN_ADDRESS
