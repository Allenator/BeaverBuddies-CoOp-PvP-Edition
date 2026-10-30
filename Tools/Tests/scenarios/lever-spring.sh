# A spring-return lever does in a Co-op game what it does alone: a pulse from the HTTP API's switch-on
# is on for one tick's automation and off by that tick's end, with only the switch-on sent as a player's
# action (not the lever going back off, on any machine), and a lever's button held for 10 ticks keeps it
# on for them, whoever holds it, sending only its press and let-go; the other player letting go of it
# meanwhile, as the pinned levers do when the pointer leaves one, does nothing. The button is one, shared:
# held by the host and the guest, and then by the guest and a third player (Tools/FakePlayer), a lever
# stays on until the last of them lets go, two clicks at once switch it once, and a lever a player holds
# as they leave is let go of for them. Each copy logs the lever tick by tick (BeaverBuddies.TestHarness/LeverWatch.cs),
# alone first for what the game does, then in the match. With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

LEVER="74 47 5"
HTTP_LEVER="78 47 5"

# levers <instance>: a lever and an HTTP lever, finished, both spring-return
levers() {
    send $1 "buildnow Lever.Folktails $LEVER"
    send $1 "buildnow HttpLever.Folktails $HTTP_LEVER"
    sleep 3
    send $1 "lever $LEVER spring"; send $1 "lever $HTTP_LEVER spring"
    sleep 3
}

# watched <instance> <label>: that watch's ticks, "tick start end" a line
watched() {
    grep -aoE "\[Test\] Lever $2 tick [0-9]+: start (on|off), end (on|off)" "$(bb_log "$1")" \
        | sed -E 's/.* tick ([0-9]+): start (on|off), end (on|off)/\1 \2 \3/'
}

# on_ticks <instance> <label>: the ticks whose automation began with the lever on
on_ticks() { watched "$1" "$2" | awk '$2 == "on"' | wc -l | tr -d ' '; }

# still_on <instance> <label>: the ticks that ended with it still on
still_on() { watched "$1" "$2" | awk '$3 == "on"' | wc -l | tr -d ' '; }

# sent <instance> <label>: the lever switchings that copy sent as its player's while the watch ran (its own, logged as
# they're sent, right after the time; the others' it plays are logged too, as "Playing ... by ...: Calling ...")
sent() {
    sed -n "/\[Test\] leverwatch .* $2/,/\[Test\] Lever $2 watch done/p" "$(bb_log "$1")" \
        | grep -ac "\] Calling Timberborn.AutomationBuildings.Lever.SwitchState"
}

# buttons <instance> <label>: the presses and let-gos of a lever's button that copy sent while the watch ran
buttons() {
    sed -n "/\[Test\] leverwatch .* $2/,/\[Test\] Lever $2 watch done/p" "$(bb_log "$1")" \
        | grep -acE "\] (Pressing|Letting go of) Lever "
}

# watch_lever <lever> <ticks> <label>: both copies watch it, the guest first: it plays each tick after the host has
watch_lever() { send client "leverwatch $1 $2 $3"; send host "leverwatch $1 $2 $3"; sleep 1; }
watched_out() {
    wait_for host "Lever $1 watch done" 120 && wait_for client "Lever $1 watch done" 60 || fail "the $1 is watched" "no end to the watch"
}
# switches <instance> <label>: how often the lever went on or off from one tick to the next
switches() { watched "$1" "$2" | awk 'NR > 1 && $2 != last { n++ } { last = $2 } END { print n + 0 }'; }
# last_state <instance> <label>: on or off, as the watch's last tick began
last_state() { watched "$1" "$2" | tail -1 | awk '{ print $2 }'; }
# agree <label>: both copies the same tick by tick, the ticks both watched
agree() {
    local same; same=$(join <(watched host "$1") <(watched client "$1"))
    [ -n "$same" ] && echo "$same" | awk '$2 != $4 || $3 != $5' | wc -l | tr -d ' ' || echo "no ticks in common"
}

# Ten ticks held, give or take the frame it's let go of in and, in the match, the trip to the host and back
HELD="^([89]|1[0-3])$"

bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
send host "newgame ${BB_MAP:-Waterfalls} Folktails"
wait_for host "Load time" 180 || { fail "the game starts alone" "never loaded"; return; }
sleep 3; send host dismiss; send host "speedsolo 1"
levers host
send host "leverwatch $HTTP_LEVER 20 solopulse"; send host "lever $HTTP_LEVER http on"
wait_for host "Lever solopulse watch done" 120 || fail "the pulse is watched alone" "no end to the watch"
send host "leverwatch $LEVER 40 soloheld"; send host "lever $LEVER hold 10"
wait_for host "Lever soloheld watch done" 120 || fail "the hold is watched alone" "no end to the watch"
expect_eq "alone, a pulse is on for one tick's automation" "$(on_ticks host solopulse)" 1
expect_eq "alone, and off by that tick's end" "$(still_on host solopulse)" 0
expect_match "alone, a lever held for 10 ticks is on for them" "$(on_ticks host soloheld)" "$HELD"
expect_no_errors "no errors alone"
bb_close

start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
send host "speed 1"; sleep 3
levers host

# The guest watches first: it plays each tick after the host has
send client "leverwatch $HTTP_LEVER 30 pulse"; send host "leverwatch $HTTP_LEVER 30 pulse"
sleep 1
send host "lever $HTTP_LEVER http on"
wait_for host "Lever pulse watch done" 120 && wait_for client "Lever pulse watch done" 60 \
    || fail "the pulse is watched in the match" "no end to the watch"
for copy in host client; do
    expect_eq "a pulse is on for one tick's automation ($copy)" "$(on_ticks $copy pulse)" 1
    expect_eq "and off by that tick's end, as alone ($copy)" "$(still_on $copy pulse)" 0
done
same=$(join <(watched host pulse) <(watched client pulse))
expect_match "both copies see the pulse" "$same" " on "
expect_eq "at the same tick, and the same tick by tick" "$(echo "$same" | awk '$2 != $4 || $3 != $5' | wc -l | tr -d ' ')" 0
expect_eq "the host sends only the switch-on" "$(sent host pulse)" 1
expect_eq "the guest sends nothing" "$(sent client pulse)" 0

for holder in client host; do
    other=$([ $holder == host ] && echo client || echo host)
    send client "leverwatch $LEVER 40 held-$holder"; send host "leverwatch $LEVER 40 held-$holder"
    sleep 1
    send $holder "lever $LEVER hold 10"
    # A few ticks in, the other player's pointer leaves it on the pinned levers
    sleep 2
    send $other "lever $LEVER release"
    wait_for host "Lever held-$holder watch done" 120 && wait_for client "Lever held-$holder watch done" 60 \
        || fail "the hold by the $holder is watched" "no end to the watch"
    for copy in host client; do
        expect_match "a lever the $holder holds for 10 ticks is on for them ($copy)" "$(on_ticks $copy held-$holder)" "$HELD"
    done
    expect_eq "the $holder sends its press and let-go" "$(buttons $holder held-$holder)" 2
    expect_eq "and no switching" "$(sent $holder held-$holder)" 0
    expect_eq "the $other sends nothing, letting go of what it didn't press" "$(buttons $other held-$holder) $(sent $other held-$holder)" "0 0"
done

# The host and the guest on one lever, each first in turn: held by both, on until the last of them lets go
for first in host client; do
    second=$([ $first == host ] && echo client || echo host)
    # Held by the first for 30 ticks, and by the second for a few seconds in the middle: on for the 30
    watch_lever "$HTTP_LEVER" 45 both-$first-long
    send $first "lever $HTTP_LEVER hold 30"; sleep 1
    send $second "lever $HTTP_LEVER press"; sleep 3
    send $second "lever $HTTP_LEVER release"
    watched_out both-$first-long
    for copy in host client; do
        expect_match "held by the $first for 30 ticks and the $second in the middle, on for the 30 ($copy)" "$(on_ticks $copy both-$first-long)" "^(2[89]|3[0-3])$"
    done
    expect_eq "both copies the same tick by tick (both-$first-long)" "$(agree both-$first-long)" 0
    # Held by the first for 6 ticks and by the second for 8 s: on until the second lets go
    watch_lever "$HTTP_LEVER" 40 both-$first-short
    send $first "lever $HTTP_LEVER hold 6"; sleep 1
    send $second "lever $HTTP_LEVER press"; sleep 8
    send $second "lever $HTTP_LEVER release"
    watched_out both-$first-short
    for copy in host client; do
        expect_match "held by the $first for 6 ticks and the $second longer, on until the $second lets go ($copy)" "$(on_ticks $copy both-$first-short)" "^(1[1-9]|2[0-9])$"
        expect_eq "on once, then off ($copy)" "$(switches $copy both-$first-short) $(last_state $copy both-$first-short)" "2 off"
    done
    expect_eq "both copies the same tick by tick (both-$first-short)" "$(agree both-$first-short)" 0
done

# A third player (Tools/FakePlayer), on the host's build, joining as a guest does: everyone reloads
fake=$BB_TEST_DIR/fakeplayer
dotnet build "$BB_SCENARIO_DIR/../../FakePlayer" -c Release -nologo -v q -o "$fake" > /dev/null 2>&1 \
    || { fail "the third player builds" "dotnet build failed"; bb_close; return; }
send host compat
wait_for host "\[Test\] compat " 10
said=$(grep -a "\[Test\] compat " "$(bb_log host)" | tail -1 | sed 's/.*\[Test\] compat //')
loads_host=$(grep -ac "Load time" "$(bb_log host)"); loads_client=$(grep -ac "Load time" "$(bb_log client)")
rm -f "$BB_CMD_PREFIX-fake"
BB_INSTANCE=fake BB_COMPAT="${said%% *}" BB_BUILD="${said#* }" dotnet "$fake/FakePlayer.dll" 600 Third 33AAFF > "$(bb_log fake)" 2>&1 &
fake_pid=$!
for ((i = 0; i < 120; i++)); do
    [ "$(grep -ac "Load time" "$(bb_log host)")" -gt "$loads_host" ] && [ "$(grep -ac "Load time" "$(bb_log client)")" -gt "$loads_client" ] && break
    sleep 1
done
[ $i -lt 120 ] || { fail "a third player joins" "nobody reloaded for them"; kill $fake_pid 2>/dev/null; bb_close; return; }
sleep 3; send host dismiss; send client dismiss; sleep 2
send host "speed 1"; sleep 2
expect_no_log "the host lets the third player in" host "Turning away Third"
third=$(grep -aoE "as Third \([0-9a-f-]{36}\)" "$(bb_log fake)" | grep -oE "[0-9a-f-]{36}")
send host "check $HTTP_LEVER"; send host "check $LEVER"; sleep 1
http_id=$(grep -aoE "\[Test\] HttpLever[^ ]* [0-9a-f-]{36} finished:" "$(bb_log host)" | tail -1 | grep -oE "[0-9a-f-]{36}")
lever_id=$(grep -aoE "\[Test\] Lever[^ ]* [0-9a-f-]{36} finished:" "$(bb_log host)" | tail -1 | grep -oE "[0-9a-f-]{36}")

# Held by the guest for 30 ticks, and by the third player for a few seconds in the middle: on for the 30
watch_lever "$HTTP_LEVER" 45 share-a
send client "lever $HTTP_LEVER hold 30"; sleep 1
send fake "press $http_id"; sleep 3
send fake "release $http_id"
watched_out share-a
for copy in host client; do
    expect_match "a lever the third player lets go of first stays on for the guest's 30 ticks ($copy)" "$(on_ticks $copy share-a)" "^(2[89]|3[0-3])$"
done
expect_eq "both copies the same tick by tick (share-a)" "$(agree share-a)" 0

# Held by the guest for 6 ticks and the third player for 8 s: on until the third player lets go
watch_lever "$HTTP_LEVER" 40 share-b
send client "lever $HTTP_LEVER hold 6"; sleep 1
send fake "press $http_id"; sleep 8
send fake "release $http_id"
watched_out share-b
for copy in host client; do
    expect_match "a lever the guest lets go of first stays on until the third player does ($copy)" "$(on_ticks $copy share-b)" "^(1[1-9]|2[0-9])$"
    expect_eq "on once, then off ($copy)" "$(switches $copy share-b) $(last_state $copy share-b)" "2 off"
done
expect_eq "both copies the same tick by tick (share-b)" "$(agree share-b)" 0

# A let-go the third player sends as the guest, as a modified game could: the host plays it as the third player's,
# who holds nothing, so the guest's 20-tick hold stands
watch_lever "$HTTP_LEVER" 35 forged
send client "lever $HTTP_LEVER hold 20"; sleep 1
send fake "releaseas client $http_id"
watched_out forged
for copy in host client; do
    expect_match "a let-go sent as another player doesn't end their hold ($copy)" "$(on_ticks $copy forged)" "^(1[89]|2[0-3])$"
done
expect_log "the host plays it as the player who sent it" host "Third sent LeverButtonEvent as [0-9a-f-]{36}: played as $third"
expect_eq "both copies the same tick by tick (forged)" "$(agree forged)" 0

# A lever without spring return, clicked by the guest and the third player at once: one button, one switch
send host "lever $LEVER spring off"; sleep 2
watch_lever "$LEVER" 30 toggle
send client "lever $LEVER press"; sleep 1
send fake "press $lever_id"; sleep 1
send client "lever $LEVER release"; sleep 1
send fake "release $lever_id"
watched_out toggle
for copy in host client; do
    expect_eq "two players' clicks at once switch it once ($copy)" "$(switches $copy toggle) $(last_state $copy toggle)" "1 on"
done
expect_eq "both copies the same tick by tick (toggle)" "$(agree toggle)" 0

# The same by the host and the guest, the lever on from that: off again, once
watch_lever "$LEVER" 30 toggle-both
send host "lever $LEVER press"; sleep 1
send client "lever $LEVER press"; sleep 1
send host "lever $LEVER release"; sleep 1
send client "lever $LEVER release"
watched_out toggle-both
for copy in host client; do
    expect_eq "the host's and the guest's clicks at once switch it once ($copy)" "$(switches $copy toggle-both) $(last_state $copy toggle-both)" "1 off"
done
expect_eq "both copies the same tick by tick (toggle-both)" "$(agree toggle-both)" 0

# The third player holds the HTTP lever and leaves: the host lets go of it for them
watch_lever "$HTTP_LEVER" 40 leave
send fake "press $http_id"; sleep 3
send fake quit
wait_for host "Third's connection ended" 20 || fail "the third player leaves" "the host never saw them go"
watched_out leave
for copy in host client; do
    expect_match "a lever a player held as they left is let go of ($copy)" "$(on_ticks $copy leave) $(last_state $copy leave)" "^([2-9]|1[0-9]|20) off$"
    expect_log "for the player who left ($copy)" $copy "Letting go of Lever $http_id for $third, who left"
done
expect_eq "both copies the same tick by tick (leave)" "$(agree leave)" 0
kill $fake_pid 2>/dev/null; rm -f "$BB_CMD_PREFIX-fake"
bb_keep_log fake

expect_in_sync "both copies in sync"
expect_no_errors "no errors in the match"
bb_close
