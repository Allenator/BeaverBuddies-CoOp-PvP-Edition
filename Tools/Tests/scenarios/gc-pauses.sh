# A measurement, not a check: the frames garbage collection holds up on a game the size of a real one (a copy of the
# newest saved game from before the run, BB_LEAK_SAVE), hosted and joined, played at speed 7 with the game's
# slowing of fast speeds, as the group plays, for BB_GC_SECONDS (300). Prints each copy's frames over BB_HITCH_MS (50)
# with a collection in them (how many, the median and the longest), its collections and its heap. BB_GAME can be a
# copy of the app with gc-max-time-slice in its boot.config, for incremental collection; BB_GC_MODE=Manual leaves
# collecting to GC.Collect alone, as collecting only at autosaves would, and times one full collection at the end
settlement="Harness GC $(date +%Hh%M%S)"
src=$(copy_save "$settlement" GC) || { fail "a saved game to load" "none pinned at BB_BIG_SAVE, and no BB_LEAK_SAVE"; return; }
echo "  the save: $src; the game: $BB_GAME; GC mode ${BB_GC_MODE:-as it is}"
export BB_HITCH_MS=${BB_HITCH_MS:-50}
bb_close
bb_launch host || { fail "the host starts" "no main menu"; return; }
bb_launch client || { fail "the guest starts" "no main menu"; return; }
bb_stack_windows
host_saved_game "$settlement" GC && pass "the guest joins the saved game" || { fail "the guest joins the saved game" "$HOSTED_WHY"; return; }
sleep 5
send host dismiss; send client dismiss; sleep 2
for inst in host client; do
    [ -n "${BB_GC_MODE:-}" ] && send $inst "gcmode $BB_GC_MODE"
    send $inst mem
done
sleep 2
# macOS's wc pads the count with spaces
hn=$(wc -l < "$(bb_log host)" | tr -d ' '); cn=$(wc -l < "$(bb_log client)" | tr -d ' ')
send host "play 7"
for ((t = 0; t < ${BB_GC_SECONDS:-300}; t += 60)); do
    sleep 60
    send host mem; send client mem
done
send host "play 0"; sleep 3
if [ -n "${BB_GC_MODE:-}" ]; then
    send host gctime; send client gctime; sleep 15
fi
for inst in host client; do
    from=$([ $inst == host ] && echo "$hn" || echo "$cn")
    log=$(bb_log $inst)
    inc=$(grep -a "\[Hitch\] Incremental GC:" "$log" | tail -1 | sed 's/.*Incremental GC: //')
    gcs=$(tail -n +"$from" "$log" | grep -aE "\[Hitch\] [0-9]+ ms frame at tick [0-9]+: [1-9][0-9]* GC" | sed -E 's/.*\[Hitch\] ([0-9]+) ms.*/\1/' | sort -n)
    n=$(echo "$gcs" | grep -c .)
    med=$(echo "$gcs" | awk 'NF { a[++n] = $1 } END { if (n) print a[int((n + 1) / 2)] }')
    max=$(echo "$gcs" | tail -1)
    slow=$(tail -n +"$from" "$log" | grep -acE "\[Hitch\] [0-9]+ ms frame")
    ticks=$(tail -n +"$from" "$log" | grep -aEo "Tick [0-9]+ IO done" | sed -n '1p;$p' | grep -oE "[0-9]+" | tr '\n' ' ')
    echo "  $inst (incremental $inc): $n frames over ${BB_HITCH_MS} ms with a collection, median ${med:-0} ms, longest ${max:-0} ms; $slow over ${BB_HITCH_MS} ms in all; ticks $ticks"
    tail -n +"$from" "$log" | grep -aE "\[Test\] (mem|gctime):" | sed -n '1p;$p' | sed "s/^/    /" | cut -c1-220
    [ -n "${BB_GC_MODE:-}" ] && tail -n +"$from" "$log" | grep -a "\[Test\] gctime:" | tail -1 | sed "s/^/    /" | cut -c1-220
done
expect_no_log "no desync" host "Out of sync"
bb_close
unset BB_HITCH_MS
