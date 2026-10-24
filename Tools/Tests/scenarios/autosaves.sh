# A game hosted or joined autosaves as the player's settings say (Multiplayer: "Autosave every", "Autosaves to keep" and
# "Skip autosaves while paused"), on the host and on the guest alike, and an autosave that's due waits while the game is
# paused, not while a guest only waits for the host's next tick; a game alone autosaves as the game does, every 10
# minutes keeping 3, paused or not, but one of the co-op games' (the Multiplayer settlement, where a guest saves) goes
# by the settings too. Checked from what the game's autosaver goes by (autosaver), whatever the player has picked; the
# settings screen's Autosaves section, with the line saying which games they apply to, is the "settings" shot. With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

# "every M min, keeps N, waits W; settings M2 min, N2, skip paused S"
autosaver() { said "$1" autosaver; }

start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
place_starts Folktails Folktails
send host "speed 1"; sleep 3
for copy in host client; do
    line=$(autosaver $copy)
    read -r _ minutes _ _ kept _ _ _ set_minutes _ set_kept _ _ skip <<< "$(echo "$line" | tr -d ',;')"
    expect_nonempty "the autosaver answers ($copy)" "$line"
    expect_eq "autosaves as often as the setting says ($copy)" "$minutes" "$set_minutes"
    expect_eq "and keeps as many ($copy)" "$kept" "$set_kept"
    expect_match "playing, an autosave that's due doesn't wait ($copy)" "$line" "waits no"
done

send host "speed 0"; sleep 3
for copy in host client; do
    if [ "$skip" == "True" ]; then
        expect_match "paused, an autosave that's due waits ($copy)" "$(autosaver $copy)" "waits yes"
    else
        expect_match "paused, an autosave that's due goes ahead, as the setting says ($copy)" "$(autosaver $copy)" "waits no"
    fi
done
send host "speed 1"; sleep 3
expect_match "playing again, it doesn't wait (host)" "$(autosaver host)" "waits no"

# A game alone, on the host's copy: the game's own autosaves
menus=$(grep -ac "Registering Main Menu Services" "$(bb_log host)")
send client "exitmenu nosave"; send host "exitmenu nosave"
for i in $(seq 1 120); do [ "$(grep -ac "Registering Main Menu Services" "$(bb_log host)")" -gt "$menus" ] && break; sleep 0.5; done
sleep 5
# The settings, where a player picks them
send host "modsettings beaverbuddies"; sleep 3
send host "scrollto Skip autosaves while paused"; sleep 1
shot "settings" host
send host escape; sleep 2
n=$(grep -ac "Load time" "$(bb_log host)")
send host "newgame ${BB_MAP:-Waterfalls} Folktails"
for i in $(seq 1 120); do [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$n" ] && break; sleep 1; done
sleep 3; send host dismiss; sleep 2
send host "speedsolo 0"; sleep 2
expect_match "a game alone autosaves as the game does, paused or not" "$(autosaver host)" "every 10 min, keeps 3, waits no"

# The guest's copy of the co-op game, opened alone: the settings, as when it was joined
joined=$(grep -aoE "Saves/Multiplayer/[^/]+\.timber" "$(bb_log client)" | head -1 | sed 's|^Saves/Multiplayer/||; s|\.timber$||')
expect_nonempty "the guest saved the game it joined" "$joined"
n=$(grep -ac "Load time" "$(bb_log client)")
send client "loadsave Multiplayer|$joined"
for i in $(seq 1 120); do [ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$n" ] && break; sleep 1; done
sleep 3; send client dismiss; sleep 2
send client "speedsolo 1"; sleep 2
expect_match "a co-op game opened alone autosaves as the settings say" "$(autosaver client)" "every $set_minutes min, keeps $set_kept, waits no"
send client "speedsolo 0"; sleep 2
if [ "$skip" == "True" ]; then
    expect_match "and paused, an autosave that's due waits" "$(autosaver client)" "waits yes"
else
    expect_match "and paused, an autosave that's due goes ahead, as the setting says" "$(autosaver client)" "waits no"
fi
