# A district center placed with the building tool, as a player does, is in every copy's preview district map, the
# one the tool checks a new district center or road against ("Districts in conflict"). The tool's preview puts a
# district of its own there as it's shown; in a Co-op game the placement comes back from the host while that preview
# is still up, so the preview's district held the spot, and putting the tool away took it out of that copy's map.
# The player who placed it could then place a second district center on roads joined to the first, and when those
# roads joined in the game, every copy crashed at once.

# tool_preview <instance> <x y z>: the District Center tool's preview there on that copy, as its log line says
tool_preview() {
    local inst=$1 log before i
    shift
    log=$(bb_log "$inst")
    before=$(grep -aFc "[Test] Tool preview" "$log")
    send "$inst" "tool DistrictCenter.Folktails $*"
    for ((i = 0; i < 100; i++)); do
        [ "$(grep -aFc "[Test] Tool preview" "$log")" -gt "$before" ] && break
        sleep 0.05
    done
    grep -aF "[Test] Tool preview" "$log" | tail -1
}

bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3

host0=$(said host districts); client0=$(said client districts)
echo "  at the start: host $host0"
echo "                client $client0"
expect_eq "both copies start with the same district maps" "$client0" "$host0"

# A spot near the guest's start where the tool shows a district center as buildable, and a click places it: the
# tool checks the district maps only as it places, so a spot shown green may still place nothing
spot=""
for at in "95 68 5" "95 72 5" "84 74 5" "94 76 5" "99 68 5" "84 62 5" "96 62 5" "100 74 5"; do
    line=$(tool_preview client $at)
    echo "  $at: ${line#*Tool preview }"
    if echo "$line" | grep -aq "at ($(echo $at | sed 's/ /, /g')) True .* warning=''$"; then
        set -- $at
        send client "tool DistrictCenter.Folktails $at click hover"
        wait_for host "Playing BuildingPlacedEvent by .* Placing DistrictCenter.Folktails, \\($1, $2, $3\\)" 10 && { spot=$at; break; }
        echo "    the host never played it"
    fi
    send client defaulttool; sleep 0.5
done
[ -n "$spot" ] || { fail "the guest places a district center" "at none of the spots tried"; return; }
pass "the guest places a district center at $spot"

# The tool stays up a while after the host's placement comes back, as a player's does, then is put away
sleep 3
echo "  tool up:  client $(said client districts)"
send client defaulttool
sleep 2

host1=$(said host districts); client1=$(said client districts)
echo "  placed:   host $host1"
echo "            client $client1"
expect_eq "the guest's preview district map has the new district center too" "$client1" "$host1"
expect_eq "the host's maps have one more district center each" \
    "$(echo "$host1" | grep -o "(" | wc -l | tr -d ' ')" "$(( $(echo "$host0" | grep -o "(" | wc -l | tr -d ' ') + 3 ))"

expect_in_sync "in sync"
expect_no_errors "no errors"
