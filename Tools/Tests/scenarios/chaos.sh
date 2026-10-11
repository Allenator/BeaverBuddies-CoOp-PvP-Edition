# Both copies shaken up for a while: frames that stall or run at different rates, messages that arrive
# late, and each player changing the speed, pausing, ticking once, placing and demolishing at random
# moments. Nothing should desync or need a resync. BB_CHAOS_SECONDS (150 by default) is how long,
# BB_CHAOS_OFF parts to leave out (nospeed nodelete notroops noplace nonet nostall), BB_CHAOS_SEED the seed (random by default; the same seed does the same things, though not at the
# same instants), BB_CHAOS_LEVEL 1 to 3.
bb_close
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
pass "a match starts"
place_starts Folktails Folktails
send host "speed 1"; sleep 6
seed=${BB_CHAOS_SEED:-$RANDOM}
level=${BB_CHAOS_LEVEL:-2}
echo "  chaos seed $seed, level $level, ${BB_CHAOS_SECONDS:-150} s"
# Every building the game and the installed mods have for Folktails, and paths more often
# (the army's buildings and the siege machines among them, for the troops the chaos sends about)
bp_zip="$HOME/Library/Application Support/Steam/steamapps/common/Timberborn/Timberborn.app/Contents/Resources/Data/StreamingAssets/Modding/Blueprints.zip"
all=$( { unzip -Z1 "$bp_zip" 2>/dev/null | grep "^Buildings/.*\.Folktails\.blueprint\.json$"
         find "$HOME/Documents/Timberborn/Mods" -path "*Buildings*" -name "*.Folktails.blueprint.json" 2>/dev/null; } \
       | sed 's|.*/||; s|\.blueprint\.json$||' | grep -v "^DistrictCenter\.\|^TemplateCollection\|Collection\|^ClaimFlag" | sort -u)
prefabs="Path,Path,Path,Path,$(echo "$all" | tr '\n' ',' | sed 's/,$//')"
echo "  $(echo "$all" | wc -l | tr -d ' ') building types"
send host "chaos $seed $level $prefabs 79 46 5 12 ${BB_CHAOS_OFF:-}"
send client "chaos $((seed + 100)) $level $prefabs 89 68 5 12 ${BB_CHAOS_OFF:-}"
sleep "${BB_CHAOS_SECONDS:-150}"
send host "chaos off"; send client "chaos off"
sleep 2
send host "speed 1"; sleep 20
for c in host client; do
    echo "  $c: $(grep -ac '\[Chaos\] \(speed\|place\|delete\|tickonce\)' "$(bb_log $c)") actions, $(grep -ac 'Resyncing everyone' "$(bb_log $c)") resyncs"
done
expect_no_log "no desync on the host" host "Resyncing everyone after the desync"
expect_no_log "no desync on the client" client "Desync detected"
grep -a -A12 "========== Summary ==========" "$(bb_log client)" | head -14
# Paused at the same tick, every entity of each copy: what one has and the other doesn't, when they differ
send host "speed 0"; sleep 6
dump_dir="${BB_TEST_DIR:-/tmp}/entities"; mkdir -p "$dump_dir"; rm -f "$dump_dir"/host.txt "$dump_dir"/client.txt
send host "dumpentities $dump_dir/host.txt"; send client "dumpentities $dump_dir/client.txt"; sleep 4
if [ -s "$dump_dir/host.txt" ] && [ -s "$dump_dir/client.txt" ]; then
    d=$(diff "$dump_dir/host.txt" "$dump_dir/client.txt" | grep '^[<>]' | head -12)
    if [ -n "$d" ]; then echo "  entities that differ (< host, > client), $(diff "$dump_dir/host.txt" "$dump_dir/client.txt" | grep -c '^[<>]') lines:"; echo "$d" | sed 's/^/    /'; fi
fi
expect_in_sync "both copies in sync after the shaking"
