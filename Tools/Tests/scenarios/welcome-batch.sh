# A guest's first batch of the host's events can hold the host's welcome, which marks the guest waiting in its lobby,
# and an event of the host's after it: the guest sets its random state to the start's before that event (0bbb54f).
# Checked once a batch, before the batch, the mark came too late, and a guest whose load had drawn random numbers split
# at tick 0, "the random state differs from the host's at tick 0" (digest-cost, October 28, 2026). Here a copy of the
# newest saved game from before the run (BB_LEAK_SAVE), big enough for its load to draw, is hosted in place (hostsave),
# which sends the host's dev tools event, made as it loaded, right behind the welcome. The game stays paused, so the
# batch plays at tick 0 and no tick follows: a split at tick 3, which a start in place has now and then for another
# reason, can't come. Without the fix it failed every time, "Random state mismatch" as the host's event played
settlement="Harness Welcome $(date +%Hh%M%S)"
src=$(copy_save "$settlement" Welcome) || { fail "a saved game to load" "none pinned at BB_BIG_SAVE, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src"
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
send host "hostsave $settlement|Welcome"
wait_for_watch host "Server started listening" 180 || { fail "the host hosts the save" "no server"; return; }
send client "joinmatch Folktails"
# The batch the welcome comes in plays as the guest's lobby opens and closes again at once (the host started)
wait_for client "leaving its lobby" 400 || { fail "the guest gets the host's first batch" "its lobby never closed"; return; }
sleep 5
first=$(sed -n '/Registering Co-op/,$p' "$(bb_log client)")
echo "$first" | grep -aq "Playing DevToolsAllowedEvent by .* at tick 0" \
    && pass "the welcome's batch holds an event of the host's" \
    || { fail "the welcome's batch holds an event of the host's" "no dev tools event at tick 0: the case didn't happen"; return; }
echo "$first" | grep -aq "The host started: setting the random state" \
    && pass "the guest takes the start's random state" || fail "the guest takes the start's random state" "never did"
echo "$first" | grep -aq "Random state mismatch" \
    && fail "no split at tick 0" "$(echo "$first" | grep -a "Random state mismatch" | head -1 | cut -c1-160)" \
    || pass "no split at tick 0"
bb_close
