# The Rosetta run, for the desync of October 25, 09:27: the host's ARM64 copy against both x86-64 guests, during 20
# minutes nobody played, a walker's position off by float rounding. Here the guest plays as an Intel Mac's game does,
# under Rosetta (BB_ARCH_CLIENT, x86_64 by default; empty for the native control), on a game the size of a real one (a
# copy of the newest saved game from before the run, BB_LEAK_SAVE), hosted and joined, and both play it with
# nobody doing anything at speed BB_IDLE_SPEED (30) for BB_IDLE_MINUTES (60). Every tick's hashes are compared, as the
# guest does; the first tick they differ at, if any, is where it split. It tests the processor and the JIT, not
# Windows' or Wine's math library, so a clean run doesn't clear the desync. Set BB_SCENARIO_TIMEOUT and BB_RUN_TIMEOUT
# past it
export BB_ARCH_CLIENT=${BB_ARCH_CLIENT-x86_64}
# Each copy's tick lines of the hosted game, up to a reload: a resync starts the ticks again from 0
hosted_ticks() { sed -n '/Registering Co-op/,$p' "$(bb_log "$1")" | sed -E '/Reloading the game for everyone|reload save/q' | grep -aE "Tick [0-9]+ IO done"; }
settlement="Harness Idle $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Idle) || { fail "a saved game to load" "none pinned at BB_BIG_SAVE, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src; the guest ${BB_ARCH_CLIENT:-native}"
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host arch; send client arch; sleep 2
grep -a "\[Test\] arch:" "$(bb_log host)" | tail -1 | sed 's/^/  host: /'
grep -a "\[Test\] arch:" "$(bb_log client)" | tail -1 | sed 's/^/  guest: /'
if [ -n "$BB_ARCH_CLIENT" ]; then
    expect_log "the guest plays as x86-64" client "\[Test\] arch: process X64"
fi
# BB_TRACE=1: desync tracing on for both, whose report names the first random call or position they differ at
if [ -n "${BB_TRACE:-}" ]; then send host "templogging on"; send client "templogging on"; sleep 1; fi
# Hosted as the Load game box's Host does, the save loaded again for the host at Start, as for its guests.
# BB_HOST_FLOW=place starts it in place instead (the harness's hostsave, as a new game is): its guests start from
# another state than the host's, and now and then split at tick 3, whatever the processor (experiments of October 28)
if [ "${BB_HOST_FLOW:-}" == place ]; then
    send host "hostsave $settlement|Idle"
    wait_for_watch host "Server started listening" 180 || { fail "the host hosts the save" "no server"; return; }
    send client "joinmatch Folktails"
    for ((i = 0; i < 400; i++)); do
        sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
            && [ "$(sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -ac "Load time")" -ge 1 ] && break
        sleep 1
    done
    [ $i -lt 400 ] && pass "the guest joins the saved game" || { fail "the guest joins" "never loaded"; return; }
else
    host_saved_game "$settlement" Idle && pass "the guest joins the saved game" || { fail "the guest joins the saved game" "$HOSTED_WHY"; return; }
fi
sleep 5
send host dismiss; send client dismiss; sleep 2
send host "speed ${BB_IDLE_SPEED:-30}"
start=$(date +%s)
end=$((start + ${BB_IDLE_MINUTES:-60} * 60))
while [ "$(date +%s)" -lt "$end" ]; do
    sleep 60
    grep -aq "Resyncing everyone\|Out of sync" "$(bb_log host)" && break
    [ -z "$(bb_pid host)" ] || [ -z "$(bb_pid client)" ] && break
    h=$(grep -aEo "Tick [0-9]+ IO done" "$(bb_log host)" | tail -1 | grep -oE "[0-9]+")
    c=$(grep -aEo "Tick [0-9]+ IO done" "$(bb_log client)" | tail -1 | grep -oE "[0-9]+")
    echo "  $(( ($(date +%s) - start) / 60 )) min: host at tick $((10#${h:-0})), guest at $((10#${c:-0}))"
done
send host "speed 0"; sleep 5
# The first tick whose hashes differ, of those both logged
first=$(join -t '|' \
    <(hosted_ticks host | sed -E 's/.*Tick ([0-9]+) IO done(.*)/\1|\2/' | sort -t '|' -k1,1) \
    <(hosted_ticks client | sed -E 's/.*Tick ([0-9]+) IO done(.*)/\1|\2/' | sort -t '|' -k1,1) \
    | awk -F '|' '$2 != $3' | sort -n | head -1)
both=$(join -t '|' \
    <(hosted_ticks host | grep -aEo "Tick [0-9]+" | grep -oE "[0-9]+" | sort -u) \
    <(hosted_ticks client | grep -aEo "Tick [0-9]+" | grep -oE "[0-9]+" | sort -u) | wc -l | tr -d ' ')
echo "  ticks both played: $both, in $(( ($(date +%s) - start) / 60 )) min"
grep -aE "Out of sync|Resyncing everyone|walker" "$(bb_log host)" | head -5 | cut -c1-300 | sed 's/^/  host: /'
# BB_NAV_AUDIT=1: the walkers each copy's load found no path for (BeaverBuddies.TestHarness/NavAudit.cs)
if grep -aq "\[NavAudit\]" "$(bb_log host)"; then
    for inst in host client; do grep -a "\[NavAudit\] before" "$(bb_log $inst)" | sed -E 's/^\[[0-9-]+\.[0-9]+\] //' | sed "s/^/  $inst: /"; done
    # The last load's: a host that loads the save again logs its first load's too
    audit() { awk '/Registering Co-op/ { buf = "" } { buf = buf $0 "\n" } END { printf "%s", buf }' "$(bb_log "$1")" | grep -a "\[NavAudit\] [0-9a-f]\{8\}-" | sed -E 's/^\[[0-9-]+\.[0-9]+\] \[NavAudit\] //' | sort; }
    echo "  paths only one copy's load found:"
    diff <(audit host) <(audit client) | grep -E "^[<>]" | sed 's/^</    host:/; s/^>/    guest:/' | cut -c1-230 | head -20
fi
if [ -z "$first" ]; then pass "every tick both played is the same ($both ticks)"; else fail "every tick both played is the same" "first split at tick $first"; fi
expect_no_log "no desync reported" host "Out of sync"
bb_close
unset BB_ARCH_CLIENT
