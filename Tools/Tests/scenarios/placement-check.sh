# Every machine checks a placement on a preview of the building made for the check alone, and the game's previews
# hide the ground a building sinks into (an Agora's middle, an Underground Pile) and keep water pumps' pipes from being
# drawn through their tiles. Destroyed without the step the building tool takes as its preview leaves a spot, a check
# left both for the rest of the game: holes to the sky where an Agora had been checked, fitting or not, on every machine
# for a placement and on the one machine for the chaos's tries. Checks alone, a placement, its construction site, the
# finished building and their demolitions leave the ground as they found it, on both copies.
# The [Placement] lines are in the detailed log, so it is on, whatever this Mac has it at
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 1

# placed <instance> <count> <x y z>: that copy has played <count> placements of the Agora there, fitting
placed() {
    local inst=$1 n=$2 i
    shift 2
    for ((i = 0; i < 40; i++)); do
        [ "$(grep -aFc "[Placement] Agora.Folktails at ($1, $2, $3) Cw0: valid" "$(bb_log "$inst")")" -ge "$n" ] && return 0
        sleep 0.5
    done
    return 1
}

bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3

host0=$(said host cutouts); client0=$(said client cutouts)
echo "  at the start: host $host0; client $client0"

# Checks alone, as every placement's and each of the chaos's tries: an Agora and an Underground Pile where they don't
# fit (on the district centers), Underground Piles where they may, and Agoras until one fits
said host canplace Agora.Folktails 78 45 5 > /dev/null
said client canplace Agora.Folktails 88 67 5 > /dev/null
said host canplace UndergroundPile.Folktails 79 46 5 > /dev/null
said host canplace UndergroundPile.Folktails 83 50 5 > /dev/null
spot=""
for at in "82 50 5" "76 50 5" "84 44 5" "83 52 5" "78 51 5" "85 48 5" "80 53 5" "86 50 5" "74 52 5" "76 40 5"; do
    [ "$(said host canplace Agora.Folktails $at)" == True ] && { spot=$at; break; }
done
echo "  an Agora fits at ${spot:-none of the spots tried}"
send host "look ${spot:-79 46 5}"; sleep 1; shot checked host
expect_eq "checks leave the ground as it was (host)" "$(said host cutouts)" "$host0"
expect_eq "checks leave the ground as it was (client)" "$(said client cutouts)" "$client0"
[ -n "$spot" ] || { fail "an Agora fits somewhere" "none of the spots tried"; return; }
set -- $spot
x=$1; y=$2; z=$3
# Its middle, hidden once it's finished: tiles x+1..x+3, y+1..y+3 at its base, turned as it is (Cw0)
mid="$((x + 2)) $((y + 2)) $z"

# Measured again, so each step answers for itself
host1=$(said host cutouts); client1=$(said client cutouts)
send client "place Agora.Folktails $spot"
placed host 1 $spot && placed client 1 $spot || { fail "the guest's Agora is placed" "no fitting placement in the logs"; return; }
sleep 2
expect_eq "its construction site hides no ground (host)" "$(said host cutouts)" "$host1"
expect_eq "its construction site hides no ground (client)" "$(said client cutouts)" "$client1"
send client "delete $spot"; sleep 3
expect_eq "nor does its demolition (host)" "$(said host cutouts)" "$host1"
expect_eq "nor does its demolition (client)" "$(said client cutouts)" "$client1"

# Finished, its own middle is hidden, nine cells, and only while it stands
send client "place Agora.Folktails $spot instant"
placed host 2 $spot && placed client 2 $spot || { fail "the finished Agora is placed" "no second fitting placement in the logs"; return; }
sleep 2
for c in host client; do
    before=$([ $c == host ] && echo "$host1" || echo "$client1")
    now=$(said $c cutouts)
    expect_eq "the finished Agora hides its middle's nine cells ($c)" "$(( ${now%% *} - ${before%% *} ))" 9
    expect_match "its middle is hidden ($c)" "$(said $c cutouts $mid)" "hidden there$"
done
send host "look $spot"; sleep 1; shot finished host
send client "delete $spot"; sleep 3
expect_eq "its demolition shows the ground again (host)" "$(said host cutouts)" "$host1"
expect_eq "its demolition shows the ground again (client)" "$(said client cutouts)" "$client1"
expect_in_sync "in sync"
expect_no_errors "no errors"
