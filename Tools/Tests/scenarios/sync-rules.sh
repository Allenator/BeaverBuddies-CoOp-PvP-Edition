# The sync rules' small cases. The tick guard: a synced method the game's own tick calls is game logic, which every
# machine runs alike, going out as a player's action, and the guard says so in the copy whose tick called it (the run
# fails any other scenario where it does; this one trips it on purpose, BB_EXPECT_SYNC_GUARD), while a player's own
# setting, between ticks, doesn't. And a name typed with spaces around it is trimmed for everyone, as the game does
# alone. With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

LEVER="74 47 5"
OTHER="76 47 5"

start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
send host "speed 1"; sleep 3
send host "buildnow Lever.Folktails $LEVER"; send host "buildnow Lever.Folktails $OTHER"
sleep 3

# A player's own setting, from the guest, between ticks
send client "lever $OTHER spring"; sleep 3
expect_no_log "a player's own setting doesn't trip the guard (guest)" client "\[SyncGuard\]"
expect_no_log "nor where it plays (host)" host "\[SyncGuard\]"

# Game logic: the host's tick pins a lever
send host "lever $LEVER tickpin"
wait_for host "Lever pinned from inside the tick" 30 || fail "a lever is pinned from inside the tick" "no line saying so"
sleep 3
expect_log "the guard says the game's tick sent a player's action" host "\[SyncGuard\] .*came from the game's tick"
expect_no_log "only in the copy whose tick called it" client "\[SyncGuard\]"

# A name with spaces around it, typed by the guest
send client "rename $LEVER __Spaced_lever__"; sleep 3
send host "name $LEVER"; send client "name $LEVER"; sleep 2
for copy in host client; do
    expect_log "a name typed with spaces around it is trimmed ($copy)" $copy "Name at $LEVER: 'Spaced lever'"
done

expect_in_sync "both copies in sync"
