# Chaos until the first desync, then both copies stopped as they are and their entities compared: what
# one has and the other lacks, and the lines of both logs that mention them. Both copies shaken up for a while: frames that stall or run at different rates, messages that arrive
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
send host "noresync"; send client "noresync"
# Until a desync is found, or the time is up
for ((i = 0; i < ${BB_CHAOS_SECONDS:-300}; i += 2)); do
    grep -aq "Desync kept, not resyncing" "$(bb_log client)" && break
    # An entity to keep an eye on (BB_WATCH=its id): the log says when it goes
    [ -n "${BB_WATCH:-}" ] && { send host "has $BB_WATCH"; send client "has $BB_WATCH"; }
    sleep 2
done
send host "chaos off"; send client "chaos off"
send host "speed 0"; sleep 8
if ! grep -aq "Desync kept, not resyncing" "$(bb_log client)"; then pass "no desync in the time given"; return; fi
dump_dir="${BB_TEST_DIR:-/tmp}/entities"; mkdir -p "$dump_dir"; rm -f "$dump_dir"/host.txt "$dump_dir"/client.txt
send host "dumpentities $dump_dir/host.txt"; send client "dumpentities $dump_dir/client.txt"; sleep 4
echo "  the desync (client):"; grep -a -A8 "Desync kept, not resyncing" "$(bb_log client)" | grep -a "Summary\|first differed\|mine \|other\|order\|move\|random" | head -8 | sed 's/^/    /'
echo "  what the host says differs:"; grep -a -A40 "What differs at tick" "$(bb_log host)" | grep -av "^\s*at \|Skipped" | head -45 | cut -c1-260 | sed 's/^/    /'
echo "  last tick: host $(grep -a 'IO done' "$(bb_log host)" | tail -1 | cut -c1-60) client $(grep -a 'IO done' "$(bb_log client)" | tail -1 | cut -c1-60)"
diff "$dump_dir/host.txt" "$dump_dir/client.txt" | grep '^[<>]' > "$dump_dir/diff.txt"
echo "  entities that differ (< host, > client): $(wc -l < "$dump_dir/diff.txt" | tr -d ' ') lines"; head -20 "$dump_dir/diff.txt" | sed 's/^/    /'
# What each log says about them
for id in $(awk '{print $2}' "$dump_dir/diff.txt" | head -6); do
    echo "  == $id"
    for c in host client; do grep -a "$id" "$(bb_log $c)" | grep -v "^  at " | head -6 | cut -c1-200 | sed "s/^/    $c: /"; done
done
fail "the copies stayed in sync" "see the entities above"
