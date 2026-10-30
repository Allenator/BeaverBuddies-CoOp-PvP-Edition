# A zipline tower takes no more connections than it has slots for in a Co-op game. The game checks only the tower a
# connection is picked to, and its tool goes on from that tower, or is put away: the tower it came from is never
# picked from again. Sending the connection skipped all of that, so the tool stayed on the first tower and each pick
# connected that one again, past its limit. Here the guest and the host connect towers with the
# tool as a player does (ziplineadd, ziplinepick), and a connection from a full tower sent as is (ziplineconnect) is
# refused by every copy.

bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 3

# Five pylons near the host's start: A, B, C and D on a square, so no cable passes over another pylon, and E beside A
spot=""
for at in "82 50" "84 52" "76 52" "80 54" "86 48" "74 48"; do
    ok=1
    for d in "0 0" "4 0" "4 4" "0 4" "-4 2"; do
        set -- $at $d
        [ "$(said host canplace ZiplinePylon.Folktails $(( $1 + $3 )) $(( $2 + $4 )) 5)" == True ] || { ok=0; break; }
    done
    [ $ok == 1 ] && { spot=$at; break; }
done
[ -n "$spot" ] || { fail "five pylons fit" "around none of the spots tried"; return; }
set -- $spot
x=$1; y=$2
A="$x $y 5"; B="$(( x + 4 )) $y 5"; C="$(( x + 4 )) $(( y + 4 )) 5"; D="$x $(( y + 4 )) 5"; E="$(( x - 4 )) $(( y + 2 )) 5"
pass "five pylons fit around ($x, $y, 5)"
for p in "$A" "$B" "$C" "$D" "$E"; do send host "place ZiplinePylon.Folktails $p 0 instant"; done
sleep 3

# ends <copy>: how many connection ends that copy's towers have, two for each connection
ends() { said $1 ziplines | grep -oE ':[0-9]+\[' | tr -dc '0-9\n' | awk '{ s += $1 } END { print s + 0 }'; }
# played <n>: both copies have played n connections
played() {
    local i
    for ((i = 0; i < 30; i++)); do
        [ "$(ends host)" -ge $(( 2 * $1 )) ] && [ "$(ends client)" -ge $(( 2 * $1 )) ] && return 0
        sleep 1
    done
    return 1
}
at() { echo "($1,$2,$3)"; }

# A to D, with the host's tool
send host "ziplineadd $A"
expect_eq "the host's pick goes" "$(said host ziplinepick $D)" picked
played 1 || { fail "the first connection is played" "the host never played it"; return; }

# The guest connects A to B: A is full then, and the tool goes on from B, not A
send client "ziplineadd $A"
expect_eq "the guest's tool connects from A" "$(said client ziplinetool)" "from $(at $A)"
expect_eq "the guest's pick goes" "$(said client ziplinepick $B)" picked
expect_eq "the tool goes on from B, which has a slot left" "$(said client ziplinetool)" "from $(at $B)"
played 2 || { fail "the guest's connection is played" "the host never played it"; return; }
said client ziplinepick $C > /dev/null
played 3 || { fail "the guest's second connection is played" "the host never played it"; return; }
host_z=$(said host ziplines); client_z=$(said client ziplines)
echo "  host:   $host_z"
echo "  client: $client_z"
expect_eq "both copies have the same connections" "$client_z" "$host_z"
expect_match "A has two, to D and to B" "$host_z" "$(at $A | sed 's/[()]/\\&/g'):2\["
expect_match "B has two, to A and to C" "$host_z" "$(at $B | sed 's/[()]/\\&/g'):2\["

# The host connects D to C: C is full then, so the tool is put away
send host "ziplineadd $D"
expect_eq "the host's pick goes" "$(said host ziplinepick $C)" picked
expect_eq "the tool is put away, C having no slot left" "$(said host ziplinetool)" off
played 4 || { fail "the host's connection is played" "the host never played it"; return; }

# From A, which is full, to E, which isn't, sent as is: every copy refuses it
before=$(said host ziplines)
send host "ziplineconnect $A $E"
wait_for host "Not connecting zipline .*: it has no free slot" 20 || fail "the host refuses it" "no warning"
wait_for client "Not connecting zipline .*: it has no free slot" 20 || fail "the guest refuses it" "no warning"
sleep 2
host_z=$(said host ziplines); client_z=$(said client ziplines)
expect_eq "nothing connected on the host" "$host_z" "$before"
expect_eq "nothing connected on the guest" "$client_z" "$before"
expect_no_match "no tower has more than two" "$host_z" ":([3-9]|[1-9][0-9])\["

expect_in_sync "in sync"
expect_no_errors "no errors"
