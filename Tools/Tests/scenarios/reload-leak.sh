# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# A game loaded over another one leaves nothing of the first behind: Unity holds about as many objects
# after the third reload as after the second, and so does the managed heap after a full collection (heap).
# A leak shows as a count that climbs with every reload, and a load that takes ten seconds longer each time;
# a game kept alive by references alone keeps Unity's count flat and grows the heap by the whole game.
# These read the logging, so it is on, whatever this Mac has it at
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 1
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 6
census() { grep -aoE "Unity objects after loading: [0-9]+" "$(bb_log client)" | sed -E 's/.*: //'; }
# Whether a ConditionalWeakTable lets its keys go on this runtime (the mod's caches are such tables)
echo "  cwtcheck: $(said host cwtcheck)"
heaps=""
# BB_RELOADS: how many reloads (3 by default; more shows a trend in the heap)
for ((n = 1; n <= ${BB_RELOADS:-3}; n++)); do
    send client "desync traced"
    wait_for client "Received the host's reload save" 60 || { fail "reload $n reaches the client" "no save"; return; }
    # The nth load is in the log once the census after it is
    for ((i = 0; i < 240; i++)); do [ "$(census | wc -l)" -ge $((n + 1)) ] && break; sleep 0.5; done
    send host "dismiss"; send client "dismiss"; sleep 3
    heap=$(said client heap)
    echo "  after reload $n: client $heap"
    heaps="$heaps $(echo "$heap" | sed -E 's/^collected ([0-9]+) MB.*/\1/')"
done
counts=$(census | tr '\n' ' ')
echo "  objects after each load: $counts"
# The load routers stay on in a Co-op game, delivering in the order handlers registered (see EventOrderFix), and say
# where the game's own order has drifted from it
for inst in host client; do
    expect_log "load routing is on ($inst)" $inst "Load event routing: events="
    expect_no_log "no other patch turns it off ($inst)" $inst "another patch changes delivery"
    echo "  $inst: $(said $inst loadorder)"
done
# With BB_HITCH_MS (profiling), each copy's heap after each load, collected, and whether an earlier game is still alive
# (Util/LeakFinder.cs; BB_LEAK_WALK for the path that keeps one)
if [ -n "${BB_HITCH_MS:-}" ]; then
    for inst in host client; do
        grep -aE "\[Pace\] heap after loading|\[Leak\] [0-9]+ of [0-9]+ scenes|\[Leak\] kept by|\[Leak\] scene [0-9]+:" "$(bb_log $inst)" | sed "s/^/  $inst: /" | cut -c1-300
    done
fi
grep -aE "Unity objects after loading" "$(bb_log client)" | tail -1 | cut -c1-400
first=$(census | sed -n 3p); last=$(census | tail -1)
expect_nonempty "the census ran" "$first"
set -- $heaps
if [ $# -ge 3 ]; then
    h2=$2; hl=${!#}
    if [ "$hl" -le $((h2 * 110 / 100 + 100)) ]; then pass "the heap stays flat across reloads (${h2} MB after the second reload, $hl MB after the last)"
    else fail "the heap stays flat across reloads" "${h2} MB after the second reload, $hl MB after the last: an earlier game is still alive"; fi
fi
# BB_LEAK_WALK: the path that keeps an earlier game, once the walk (minutes, off the main thread) has found it
if [ -n "${BB_LEAK_WALK:-}" ]; then
    wait_for client "\[Leak\] kept by" "${BB_LEAK_WAIT:-600}" || echo "  the walk found nothing in ${BB_LEAK_WAIT:-600} s"
    grep -aE "\[Leak\] kept by" "$(bb_log client)" | sed "s/^/  client: /" | cut -c1-3000
fi
if [ -n "$first" ] && [ -n "$last" ]; then
    if [ "$last" -le $((first * 102 / 100)) ]; then pass "the object count stays flat across reloads (${first} after the second reload, $last after the third)"
    else fail "the object count stays flat across reloads" "$first after the second reload, $last after the third"; fi
fi
