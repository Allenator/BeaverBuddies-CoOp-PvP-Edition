# A co-op game keeps the computer awake, hosted or joined, and lets go once it's left: the host's Mac slept with a
# game hosted on October 26, 2026, and stopped everyone's for four hours. Each copy holds a power assertion while in
# the game, which macOS lists (pmset -g assertions) under the mod's name, and releases it at the main menu
pref_set ModSetting.beaverbuddies.Settings.KeepComputerAwake 1
held() { pmset -g assertions | grep -c "Timberborn co-op game"; }
before=$(held)
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
sleep 2
expect_eq "the host keeps the computer awake" "$(said host keepawake)" "on, a power assertion"
expect_eq "the guest keeps the computer awake" "$(said client keepawake)" "on, a power assertion"
expect_eq "macOS lists both copies' assertions" "$(held)" "$((before + 2))"
for inst in client host; do
    n=$(grep -ac "Registering Main Menu Services" "$(bb_log $inst)")
    send $inst "exitmenu nosave"
    for ((i = 0; i < 60; i++)); do
        [ "$(grep -ac "Registering Main Menu Services" "$(bb_log $inst)")" -gt "$n" ] && break
        sleep 1
    done
done
sleep 3
expect_eq "at the main menu, neither does" "$(held)" "$before"
expect_log "the host says it let go" host "Keeping the computer awake: off"
bb_close
