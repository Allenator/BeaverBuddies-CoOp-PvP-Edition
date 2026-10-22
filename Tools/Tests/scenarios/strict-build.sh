# Players on other builds (Settings.StrictBuild): the host turns away a guest on another build and the guest hears why, a guest refuses a
# host on another build before it loads anything, and when neither minds they play on together, warned, in sync. Each copy is told its
# build and whether it minds itself (BB_FAKE_BUILD_*, BB_STRICT_*), as two sharing one Mac's settings can't. BB_JOIN=code joins by join code
if [ "${BB_JOIN:-ip}" == code ]; then
    pref_set ModSetting.beaverbuddies.Settings.EnableSteamConnection 0
    export BB_RTC_CODE=TEST$(LC_ALL=C tr -dc 'A-Z0-9' </dev/urandom | head -c 6)
    export BB_JOIN_ADDRESS="rtc:$BB_RTC_CODE"
fi
strict_done() { unset BB_RTC_CODE BB_JOIN_ADDRESS BB_FAKE_BUILD_HOST BB_FAKE_BUILD_CLIENT BB_STRICT_HOST BB_STRICT_CLIENT; }
bail() { fail "$1" "$2"; bb_close; strict_done; }

# Fresh copies, the host's new game in its box, and the guest joining it
host_and_join() {
    bb_close
    bb_launch host || return 1
    bb_launch client || return 1
    bb_stack_windows
    send host "hostnew ${BB_MAP:-Waterfalls} Folktails"
    wait_for_watch host "Reading map with length" 120 || return 1
    send client "joinmatch Folktails ${BB_JOIN_ADDRESS:-}"
}

# The guest is on another build and doesn't mind; the host does
export BB_FAKE_BUILD_CLIENT=0badbeef BB_STRICT_CLIENT=0
host_and_join || { bail "the host's game is up" "never hosted"; return; }
wait_for host "Turning away .* this hosts" 120 && pass "the host turns away a guest on another build" || fail "the host turns away a guest on another build" "it let them in"
wait_for client "Turned away by the host" 30 && pass "the guest hears why" || fail "the guest hears why" "no word from the host"
sleep 3
shot "turned-away-host" host
shot "turned-away-guest" client
unset BB_FAKE_BUILD_CLIENT BB_STRICT_CLIENT

# The host is on another build and doesn't mind; the guest does
export BB_FAKE_BUILD_HOST=0badbeef BB_STRICT_HOST=0
host_and_join || { bail "the host's game is up again" "never hosted"; return; }
wait_for client "Not loading: the host plays" 120 && pass "the guest refuses a host on another build" || fail "the guest refuses a host on another build" "it didn't"
wait_for host "Letting in .* though this hosts" 30 && pass "the host, not minding, lets the guest in" || fail "the host, not minding, lets the guest in" "no line for it"
sleep 3
shot "refusing-guest" client
expect_no_log "the refusing guest never loads the game" client "Registering Co-op services"
unset BB_FAKE_BUILD_HOST BB_STRICT_HOST

# Neither minds: they play together, warned
export BB_FAKE_BUILD_CLIENT=0badbeef BB_STRICT_HOST=0 BB_STRICT_CLIENT=0
start_match Folktails Folktails || { bail "they play when neither minds" "never got into the game"; return; }
pass "they play when neither minds"
expect_log "the guest loads, saying the host's build isn't its own" client "Loading though the host plays"
expect_log "the guest is warned of the other build" client "Warning! Server mod build"
send host "speed 1"; sleep 10
expect_in_sync "they stay in sync"
expect_no_errors "no errors"
bb_close
strict_done
