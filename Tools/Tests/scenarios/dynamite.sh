# Dynamite goes off without breaking the game, alone and in a Co-op game: a stick on the ground,
# two side by side (the first sets off the second) and one next to a path, with BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

blast() {
    local copy=$1 tag=$2
    for x in 74 75 76 77 78 79 80; do send $copy "buildnow Path $x 45 5"; done
    send $copy "buildnow Dynamite.Folktails 74 47 5"
    send $copy "buildnow Dynamite.Folktails 78 47 5"; send $copy "buildnow Dynamite.Folktails 79 47 5"
    send $copy "buildnow Dynamite.Folktails 77 46 5"
    sleep 3
    for at in "74 47 5" "78 47 5" "77 46 5"; do send $copy "detonate $at"; sleep 4; done
    sleep 5
    expect_log "the dynamite is lit ($tag)" $copy "\[Test\] Detonating Dynamite"
    expect_no_log "the game doesn't crash ($tag)" $copy "First uncaught exception|Can't unblock edge"
}

bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
send host "newgame ${BB_MAP:-Waterfalls} Folktails"
wait_for host "Load time" 180 || { fail "the game starts alone" "never loaded"; return; }
sleep 3; send host dismiss; send host "speedsolo 3"
blast host alone
expect_no_errors "no errors alone"
bb_close

start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
send host "speed 3"; sleep 3
blast host co-op
expect_no_log "the guest doesn't crash (co-op)" client "First uncaught exception|Can't unblock edge"
expect_in_sync "both copies in sync"
expect_no_errors "no errors co-op"
bb_close
