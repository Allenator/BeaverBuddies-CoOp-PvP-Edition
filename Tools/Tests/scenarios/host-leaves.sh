# Leaving says so. A guest that leaves tells the host, whose log has them leaving rather than a connection that may have
# dropped. The host that leaves for the main menu tells each guest (a Steam session ends without a word, which left a
# guest waiting on a host gone quiet): the guest saves its own copy of the game as far as it got, as the game does on
# leaving, and is told the host left. The guest's saves go in the Multiplayer folder, which the runner puts back as it was.
# Each goodbye is answered, and the side leaving closes once it is, or after a second: with BB_SIM_STEAM_CLOSE=1 the
# connections drop what's on its way as they close, as a Steam session does: unanswered, both goodbyes are lost
saves="$BB_SAVES/Multiplayer"

# A guest leaves
start_match Folktails Folktails || { fail "a match starts" "never got into the game"; return; }
send host "speed 1"; sleep 5
send client "exitmenu nosave"
wait_for host "connection ended: they left the game" 30 && pass "the host hears the guest leave" \
    || fail "the host hears the guest leave" "$(grep -a "connection ended" "$(bb_log host)" | tail -1 | cut -c1-200)"
expect_log "the guest knows the host heard it" client "heard the goodbye in [0-9]+ ms"

# The host leaves
start_match Folktails Folktails || { fail "a match starts again" "never got into the game"; return; }
send host "speed 1"; sleep 5
send host "exitmenu nosave"
wait_for client "The host left: back to the main menu" 30 && pass "the guest is told the host left" \
    || fail "the guest is told the host left" "no such line in the guest's log"
expect_log "from the host's goodbye" client "The host is leaving the game"
# The guest's answer may not make it: it closes as it's told, as a Steam session drops what's on its way (with
# BB_SIM_STEAM_CLOSE). The host waits for it a second at most, and its goodbye has arrived
wait_for host "(heard|didn't confirm) the goodbye" 10 && pass "the host waited for the guest's answer" \
    || fail "the host waited for the guest's answer" "no line saying so"
saved() { sed -n '/Saving this copy of the game before leaving it/,$p' "$(bb_log client)" | grep -aq "Saved game in"; }
for ((i = 0; i < 20; i++)); do saved && break; sleep 1; done
saved && pass "the guest saves its copy of the game" || fail "the guest saves its copy of the game" "no save after the host left"
# "Saving game to Multiplayer - 2026-10-24 16h05m, Day 1-1.autosave at ..."
name=$(sed -n '/Saving this copy of the game before leaving it/,$p' "$(bb_log client)" | grep -aoE "Saving game to Multiplayer - .*\.autosave" | head -1 | sed 's/^Saving game to Multiplayer - //')
[ -n "$name" ] && [ -f "$saves/$name.timber" ] && pass "as an autosave in the Multiplayer folder ($name)" \
    || fail "as an autosave in the Multiplayer folder" "no file for '$name'"
expect_no_log "and isn't left waiting on a host gone quiet" client "Nothing from the host"
expect_no_errors "no errors"
shot "host-left" client
bb_close
