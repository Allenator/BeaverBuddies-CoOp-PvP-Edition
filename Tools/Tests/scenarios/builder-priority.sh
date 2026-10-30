# A player dragging the builders' priority tool over things that have that priority already. Alone, the game sets
# each of them again anyway: a demolish job (a bush marked with the
# demolish resources tool, say) goes to the back of its priority's queue, the order builders look through, taking the
# first of the jobs equally close. That has to happen on every copy alike, or a builder with two bushes equally close
# walks to a different one on the copy that dragged. The guest marks the natural resources in a square near its start,
# sets them to VeryHigh, then drags VeryHigh again over the first half of the rows only (the whole square dragged again,
# in the same order, would end in the order it was in), and both copies' queues are compared after each. Then the
# builders work on them for a while, and both copies have to stay in sync. The marking names one more that's gone by the
# time it plays (a builder took it since), which mustn't keep the rest from being marked. With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi
bb_close
start_match Folktails Folktails || { fail "a match starts" "never started"; return; }
place_starts Folktails Folktails
# A few seconds played, so the starts stand and their districts reach around them; then paused, so no builder
# takes a bush away while the queues are compared
send host "speed 3"; sleep 5
send host "speed 0"; sleep 3

# Both copies' demolish jobs, in their order, by priority (see demolishjobs in TestHarness.cs)
queues() { H=$(said host demolishjobs); C=$(said client demolishjobs); }
# How many jobs a queue line has
jobs_in() { echo "$1" | grep -oE "[0-9a-f]{8}" | wc -l | tr -d ' '; }

read -r cx cy _ <<< "${BB_CLIENT_START:-89 68 5}"
area=$(said client "findresources $cx $cy 20 6 6")
read -r x0 y0 x1 y1 n <<< "$area"
if [ -z "$n" ]; then fail "natural resources the guest's builders reach" "findresources found $area"; return; fi
pass "$n natural resources the guest's builders reach in $x0 $y0 $x1 $y1"
mid=$((y0 + (y1 - y0 + 1) / 2 - 1))

send client "markresources $x0 $y0 $x1 $y1 gone"; sleep 3
queues
expect_no_log "one gone doesn't fail the rest" host "Failed to replay event"
expect_match "the guest's marks reach the host" "$H" "Normal [0-9a-f]{8}"
expect_eq "the same queues on both after marking" "$C" "$H"

said client "priority $x0 $y0 $x1 $y1 VeryHigh" > /dev/null; sleep 3
queues
expect_match "the guest's VeryHigh reaches the host" "$H" "^VeryHigh [0-9a-f]{8}"
expect_eq "the same queues on both after setting VeryHigh" "$C" "$H"

# "N to set, M already VeryHigh"
again=$(said client "priority $x0 $y0 $x1 $mid VeryHigh"); sleep 3
read -r to_set _ _ already _ <<< "$again"
expect_match "the second drag has bushes to set" "$to_set" "^[1-9]"
expect_eq "the second drag is over bushes VeryHigh already" "$already" "$to_set"
queues
expect_eq "the same queues on both after setting VeryHigh again on some" "$C" "$H"

# Then the builders: with the queues apart, one with two bushes equally close can walk to another on each copy.
# It can also pass by luck, when no builder meets such a tie
before=$(jobs_in "$H")
send host "speed $BB_SPEED"; sleep ${BB_PRIORITY_SECONDS:-30}
send host "speed 0"; sleep 3
queues
after=$(jobs_in "$H")
if [ "$after" -lt "$before" ]; then pass "the builders took $((before - after)) of the $before"
else fail "the builders took some" "$after of the $before still there"; fi
expect_eq "the same queues on both after the builders' work" "$C" "$H"
expect_in_sync "in sync after the builders' work"
