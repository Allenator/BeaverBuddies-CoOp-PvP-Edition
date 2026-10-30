# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# reload-leak on a game the size of a real one: a copy of a saved game (BB_LEAK_SAVE, the newest in Multiplayer from
# before the run by default) hosted from a settlement of the harness's own, joined, and reloaded for everyone
# BB_RELOADS times (3), with both copies' heap after a full collection (heap) after each. The heap a game keeps alive is
# about the game's size, so an earlier game left behind shows here as a heap that climbs by a whole game every reload;
# a small new game's can hide in the noise.
# These read the logging, so it is on, whatever this Mac has it at
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 1
settlement="Harness Leak $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Leak) || { fail "a saved game to load" "none in Multiplayer, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src"
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
# In place, on purpose: the double load keeps the host's first game alive every time, which would leave nothing for
# this to measure but that. A start in place now and then splits at tick 3 (experiments of October 28), an extra
# reload before the first one counted: run again
send host "hostsave $settlement|Leak"
wait_for_watch host "Server started listening" 180 || { fail "the host hosts the save" "no server"; return; }
send client "joinmatch Folktails"
for ((i = 0; i < 300; i++)); do
    sed -n '/Registering Co-op/,$p' "$(bb_log client)" | grep -aq "Load time" \
        && [ "$(sed -n '/Registering Co-op/,$p' "$(bb_log host)" | grep -ac "Load time")" -ge 1 ] && break
    e=$(bb_load_error) && { fail "the guest joins" "load error: $e"; return; }
    sleep 1
done
[ $i -lt 300 ] || { fail "the guest joins" "never loaded"; return; }
pass "the guest joins the saved game"
sleep 5
send host dismiss; send client dismiss; sleep 2
loads() { grep -ac "Load time" "$(bb_log client)"; }
heap_mb() { echo "$1" | sed -E 's/^collected ([0-9]+) MB.*/\1/'; }
host_heaps=""; client_heaps=""
said host scenes > /dev/null; said client scenes > /dev/null
h=$(said host heap); c=$(said client heap)
echo "  joined:          host $h"
echo "                   client $c"
host_heaps="$(heap_mb "$h")"; client_heaps="$(heap_mb "$c")"
for ((n = 1; n <= ${BB_RELOADS:-3}; n++)); do
    before=$(loads)
    send client "desync traced"
    wait_for client "Received the host's reload save" 120 || { fail "reload $n reaches the client" "no save"; return; }
    for ((i = 0; i < 360; i++)); do [ "$(loads)" -gt "$before" ] && break; sleep 0.5; done
    sleep 5
    send host "dismiss"; send client "dismiss"; sleep 3
    # BB_RELOAD_GAP: seconds to play each game before the next reload; 35 or more lets the leak finder's check 30 s into a
    # game run (BB_HITCH_MS), whether the games before it are still alive after a full collection
    sleep "${BB_RELOAD_GAP:-0}"
    h=$(said host heap); c=$(said client heap)
    echo "  after reload $n: host $h; $(said host scenes)"
    echo "                   client $c; $(said client scenes)"
    host_heaps="$host_heaps $(heap_mb "$h")"; client_heaps="$client_heaps $(heap_mb "$c")"
done
# With BB_HITCH_MS, what the leak finder (BeaverBuddies.TestHarness/LeakFinder.cs) says of the games before each one, 30 s into it
if [ -n "${BB_HITCH_MS:-}" ]; then
    for inst in host client; do
        grep -aE "\[Leak\] 30 s in" "$(bb_log $inst)" | sed "s/^/  $inst: /" | cut -c1-200
    done
fi
# BB_LEAK_WALK: the path that keeps an earlier game, once the walk (minutes, off the main thread) has found it
if [ -n "${BB_LEAK_WALK:-}" ]; then
    # The walk from each load, and the one 30 s into the game (the one that counts: the load's own can still see the game
    # going out)
    for ((i = 0; i < ${BB_LEAK_WAIT:-600}; i++)); do
        [ "$(grep -ac "\[Leak\] kept by" "$(bb_log host)")" -ge 2 ] && break
        sleep 1
    done
    sleep 20
    for inst in host client; do
        grep -aE "\[Leak\] kept by" "$(bb_log $inst)" | sed "s/^/  $inst: /" | cut -c1-4000
    done
fi
echo "  host heaps (MB):   $host_heaps"
echo "  client heaps (MB): $client_heaps"
# BB_ROOTS: what keeps each copy's earliest earlier game alive, if one is (roots, a walk of the heap that takes minutes)
if [ -n "${BB_ROOTS:-}" ]; then
    for inst in host client; do send $inst roots; done
    for ((i = 0; i < ${BB_LEAK_WAIT:-900}; i++)); do
        [ "$(grep -ac "\[Test\] roots (\|\[Test\] roots: no earlier" "$(bb_log host)")" -ge 1 ] \
            && [ "$(grep -ac "\[Test\] roots (\|\[Test\] roots: no earlier" "$(bb_log client)")" -ge 1 ] && break
        sleep 1
    done
    for inst in host client; do
        grep -aE "\[Test\] roots (\(|: no earlier)" "$(bb_log $inst)" | sed "s/^/  $inst: /" | cut -c1-6000
    done
fi
# The heap figures are reported, not checked: Mono's used size under its collector (Boehm) counts the free room in blocks
# partly in use, and swings by a game's size with nothing left behind. Whether the earlier games are gone is the check
for inst in host client; do
    left=$(said $inst scenes)
    if echo "$left" | grep -q "^0 of"; then pass "$inst: no earlier game is left alive ($left)"
    else fail "$inst: no earlier game is left alive" "$left"; fi
done
