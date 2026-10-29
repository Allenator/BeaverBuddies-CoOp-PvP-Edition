# A new game hosted as the New Game screens' Host game button does, in place: the host plays on from the game it set
# up, its guests from a load of the host's save of it. What a save doesn't keep, or what only a load does, could leave
# them apart from the first tick, as it does a saved game started in place (see arch-idle). Each map of BB_MAPS
# (Waterfalls and Lakes) is hosted, joined and started BB_REPEAT (2) times, both copies traced from tick 0 (BB_TRACE,
# on by default) and their walker paths before the first tick listed (BB_NAV_AUDIT), and played BB_PLAY_TICKS (300)
# ticks. A trace report ("Desync detected"), a path only one copy found, or a tick whose hashes differ, fails
export BB_NAV_AUDIT=1
hosted_ticks() { awk '/Registering Co-op/ { buf = "" } { buf = buf $0 "\n" } END { printf "%s", buf }' "$(bb_log "$1")" | grep -aE "Tick [0-9]+ IO done" | sed -E 's/.*Tick ([0-9]+) IO done(.*)/\1|\2/' | sort -t '|' -k1,1; }
audit() { awk '/Registering Co-op/ { buf = "" } { buf = buf $0 "\n" } END { printf "%s", buf }' "$(bb_log "$1")" | grep -a "\[NavAudit\] [0-9a-f]\{8\}-" | sed -E 's/^\[[0-9-]+\.[0-9]+\] \[NavAudit\] //' | sort; }
for map in ${BB_MAPS:-Waterfalls Lakes}; do
    for ((run = 1; run <= ${BB_REPEAT:-2}; run++)); do
        name="$map, start $run"
        bb_close
        bb_launch host || { fail "$name: the host starts" "no main menu"; continue; }
        bb_launch client || { fail "$name: the guest starts" "no main menu"; continue; }
        bb_stack_windows
        if [ "${BB_TRACE-1}" == 1 ]; then send host "templogging on"; send client "templogging on"; sleep 1; fi
        send host "hostnew $map Folktails"
        wait_for_watch host "Reading map with length" 120 || { fail "$name: the host hosts" "no box"; continue; }
        send client "joinmatch IronTeeth"
        wait_for client "waiting in its lobby" 180 || { fail "$name: the guest waits in the lobby" "never did"; continue; }
        sleep "${BB_LOBBY_WAIT:-10}"
        send host "click ConfirmButton"
        wait_for client "leaving its lobby" 60 || { fail "$name: the guest's lobby closes" "still up"; continue; }
        sleep 3; send host dismiss; send client dismiss; sleep 1
        run_ticks "${BB_PLAY_TICKS:-300}" 600
        only=$(diff <(audit host) <(audit client) | grep -E "^[<>]" | sed 's/^</host:/; s/^>/guest:/')
        counts=$(for inst in host client; do grep -a "\[NavAudit\] before" "$(bb_log $inst)" | tail -1 | sed -E 's/.*before the first tick: //'; done | tr '\n' '/')
        traced=$(grep -a -h -A4 "========== Summary" "$(bb_log host)" "$(bb_log client)" | grep -a -E "agree for|mine:|other:" | head -3 | tr '\n' ' ' | cut -c1-300)
        first=$(join -t '|' <(hosted_ticks host) <(hosted_ticks client) | awk -F '|' '$2 != $3' | sort -n | head -1 | cut -c1-120)
        both=$(join -t '|' <(hosted_ticks host) <(hosted_ticks client) | wc -l | tr -d ' ')
        echo "  $name: paths before the first tick $counts $both ticks compared"
        [ -n "$only" ] && echo "$only" | head -8 | cut -c1-200 | sed 's/^/    /'
        if [ -z "$only" ] && [ -z "$traced" ] && [ -z "$first" ]; then
            pass "$name: the same paths, traces and hashes ($both ticks)"
        else
            fail "$name" "$([ -n "$only" ] && echo "paths only one copy found; ")${traced:+traces split: $traced; }${first:+hashes split at tick $first}"
        fi
    done
done
bb_close
unset BB_NAV_AUDIT
