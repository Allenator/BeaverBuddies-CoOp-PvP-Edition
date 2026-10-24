# A guest keeps close behind the host at speed 7 as at speeds 1 to 3: its own actions take about the cushion (100 ms)
# beyond the way there and back, not the 0.6 s they took in the session of October 24, 2026, where at speed 7 a tick
# (86 ms) was shorter than the cushion, the guest saw its lead as never more than a tick, slowed itself, and settled 7
# ticks behind. Its actions here are its speed set to the speed it is, a player's action that changes nothing, each a
# [Latency] line in its log. And it keeps that pace without stopping for the host's ticks. With BeaverBuddies on its own
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi
export BB_HITCH_MS=${BB_HITCH_MS:-50}
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
place_starts Folktails Folktails
send host "speed 7"
# Time to settle: a guest slowing itself 3% loses half a second in about 15 s
sleep 25
n=$(grep -ac "\[Latency\]" "$(bb_log client)")
pace=$(grep -ac "\[Pace\]" "$(bb_log client)")
for i in $(seq 1 30); do send client "speed 7"; sleep 1; done
sleep 3

# "[Latency] SpeedSetEvent took 692 ms: 34 ms waiting for the host's tick, 658 ms there and back and catching up. ..."
rest=$(grep -a "\[Latency\]" "$(bb_log client)" | tail -n +$((n + 1)) | grep -oE "[0-9]+ ms there and back" | grep -oE "^[0-9]+" | sort -n)
count=$(echo "$rest" | grep -c .)
median=$(echo "$rest" | awk '{a[NR] = $1} END {print a[int((NR + 1) / 2)]}')
echo "  $count actions; there and back and catching up: median ${median:-?} ms, longest $(echo "$rest" | tail -1) ms"
[ "$count" -ge 20 ] && pass "the guest's actions play ($count of 30)" || fail "the guest's actions play" "$count of 30"
[ "${median:-9999}" -le 300 ] && pass "at speed 7 the guest keeps close behind the host (median ${median} ms)" \
    || fail "at speed 7 the guest keeps close behind the host" "median ${median:-?} ms, where 0.6 s is a guest 7 ticks behind"
# Frames the guest stopped for a tick that hadn't come, over the 10 s stretches while it acted
stalls=$(grep -a "\[Pace\]" "$(bb_log client)" | tail -n +$((pace + 1)) | grep -oE "[0-9]+ stalled" | awk '{s += $1} END {print s + 0}')
[ "$stalls" -le 5 ] && pass "without stopping for the host's ticks ($stalls frames)" || fail "without stopping for the host's ticks" "$stalls frames stalled"
grep -a "\[Pace\]" "$(bb_log client)" | tail -n +$((pace + 1)) | sed 's/.*\[/    [/' | cut -c1-220
expect_in_sync "both copies in sync"
expect_no_errors "no errors"
