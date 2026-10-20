# BB_EXPECT_DESYNC: this scenario resyncs the copies on purpose
# A game loaded over another one leaves nothing of the first behind: Unity holds about as many objects
# after the third reload as after the second. A leak shows as a count that climbs
# with every reload, and a load that takes ten seconds longer each time.
# These read the logging, so it is on, whatever this Mac has it at
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 1
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 6
census() { grep -aoE "Unity objects after loading: [0-9]+" "$(bb_log client)" | sed -E 's/.*: //'; }
for n in 1 2 3; do
    send client "desync traced"
    wait_for client "Received the host's reload save" 60 || { fail "reload $n reaches the client" "no save"; return; }
    # The nth load is in the log once the census after it is
    for ((i = 0; i < 240; i++)); do [ "$(census | wc -l)" -ge $((n + 1)) ] && break; sleep 0.5; done
    send host "dismiss"; send client "dismiss"; sleep 3
done
counts=$(census | tr '\n' ' ')
echo "  objects after each load: $counts"
grep -aE "Unity objects after loading" "$(bb_log client)" | tail -1 | cut -c1-400
first=$(census | sed -n 3p); last=$(census | tail -1)
expect_nonempty "the census ran" "$first"
if [ -n "$first" ] && [ -n "$last" ]; then
    if [ "$last" -le $((first * 102 / 100)) ]; then pass "the object count stays flat across reloads (${first} after the second reload, $last after the third)"
    else fail "the object count stays flat across reloads" "$first after the second reload, $last after the third"; fi
fi
