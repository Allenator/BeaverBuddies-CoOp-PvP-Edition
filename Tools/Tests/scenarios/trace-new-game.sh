# With detailed logging on for both players, a hosted new game is traced from its first tick, not from its load: the host
# generates the map and the guest loads it from the host's save, so their loads differ, and traced they read as a desync
# at once (tick -1). Logging on for the session, as the desync dialog turns it on (templogging), and by "Always trace"
trace_round() {
    local how=$1 i
    bb_close
    bb_launch host || { fail "the host starts ($how)" "no main menu"; return 1; }
    bb_launch client || { fail "the guest starts ($how)" "no main menu"; return 1; }
    bb_stack_windows
    if [ "$how" == "for the session" ]; then send host "templogging on"; send client "templogging on"; sleep 1; fi
    send host "hostmatch ${BB_MAP:-Waterfalls} Folktails"
    wait_for_watch host "Server started listening" 120 || { fail "the host hosts ($how)" "no server"; return 1; }
    send client "joinmatch IronTeeth"
    for ((i = 0; i < 180; i++)); do
        sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
            && sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -aq "Load time" && break
        sleep 1
    done
    [ $i -lt 180 ] || { fail "both load the game ($how)" "not in 180 s"; return 1; }
    sleep 3; send host dismiss; send client dismiss; sleep 2
    send host "speed 1"; sleep 15
    expect_log "the host traces from the first tick ($how)" host "Tracing from tick 0"
    expect_log "and the guest too ($how)" client "Tracing from tick 0"
    expect_no_log "no desync at the load ($how)" client "Desync detected"
    expect_in_sync "they play on in sync ($how)"
}
trace_round "for the session"
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 1
trace_round "by Always trace"
bb_close
