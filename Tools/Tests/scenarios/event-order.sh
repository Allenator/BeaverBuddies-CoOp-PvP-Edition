# The game's event bus calls an event's handlers in the same order on every machine, whatever one player's own
# panels did in the meantime. It kept them in a Dictionary, where a handler that registers takes the place the last
# one to go left, and some handlers are one player's alone (the batch control window's tabs), so the goods piles on
# a platform registered in a different order on each machine, and taking the platform down moved, merged and
# deleted them in a different order on each, and the copies desynced. Here the guest's own handlers come and
# go (eventchurn) before the piles are made, and the piles' handlers of a deletion have to be in the same order on
# both copies.

bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3

# Three platforms in a row near the host's start, each with room on top
row=""
for at in "82 50" "84 52" "76 52" "80 54" "86 48" "74 48"; do
    set -- $at
    ok=1
    for dx in 0 1 2; do
        [ "$(said host canplace Platform.Folktails $(( $1 + dx )) $2 5)" == True ] || { ok=0; break; }
    done
    [ $ok == 1 ] && { row=$at; break; }
done
[ -n "$row" ] || { fail "three platforms fit in a row" "none of the rows tried"; return; }
set -- $row
x=$1; y=$2
pass "three platforms fit at ($x, $y, 5)"

said client eventchurn 20 > /dev/null
for dx in 0 1 2; do send host "place Platform.Folktails $(( x + dx )) $y 5 0 instant"; done
sleep 3
for dx in 0 1 2; do send host "pile $(( x + dx )) $y 6 Log 3"; done
sleep 3

host_order=$(said host eventorder); client_order=$(said client eventorder)
echo "  host:   $host_order"
echo "  client: $client_order"
expect_eq "three piles rest on the platforms" "$(echo $host_order | wc -w | tr -d ' ')" 3
expect_eq "their handlers of a deletion are in the same order on both copies" "$client_order" "$host_order"

for dx in 0 1 2; do send host "delete $(( x + dx )) $y 5"; done
sleep 5
expect_in_sync "in sync"
expect_no_errors "no errors"
