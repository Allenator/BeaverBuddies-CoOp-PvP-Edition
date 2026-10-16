# Open matches with made-up hosts (the harness's mockmatches), one for each way a match reads: joinable
# with and without a player cap, a Quick match that gets mods first, and each reason one can't be joined.
# A screenshot of every one picked, to look at.
pref_set ModSetting.beaverbuddies.Settings.AlwaysTrace 0
bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
send host "click MultiplayerButton"; sleep 1
send host "openmatches"; sleep 2
send host "mockmatches"; sleep 1
shot "list" host
for i in 0 1 2 3 4 5 6 7; do
    send host "pickhost $i"; sleep 1
    shot "pick-$i" host
done
expect_no_errors "no errors"
