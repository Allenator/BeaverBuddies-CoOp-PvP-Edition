# BB_EXPECT_DESYNC: this scenario reloads both copies from the host's save on purpose, to see what the save keeps
# Cosmetic choices in a Co-op game, each made on one side and the same on both: a banner's and a tail painter's image
# picked with the picker's buttons, an image flipped, a light customized and colored from its field and from a preset,
# an HTTP Lever colored from the HTTP API, and a speaker's sound. The host has none of the custom files the guest has
# (BB_HIDE_CUSTOM_HOST), as a player who never got them: it keeps their names anyway, shows or plays the default for
# them, and keeps them through Refresh and through a reload from its own save. With BeaverBuddies on its own.
if mod_installed TimberEmpires; then mod_disable TimberEmpires; fi

# Custom files for the guest, gone again when the scenario ends (names without spaces: commands are split on them)
DOCS="$HOME/Documents/Timberborn"
BANNER=BBtestbanner.png; TAIL=BBtesttail.png; SOUND=BBtestsound
cleanup_files() { rm -f "$DOCS/Banners/$BANNER" "$DOCS/Tails/$TAIL" "$DOCS/Sounds/$SOUND.wav"; }
trap cleanup_files EXIT
mkdir -p "$DOCS/Banners" "$DOCS/Tails" "$DOCS/Sounds"
cp "$BB_SCENARIO_DIR/../../../BeaverBuddies/thumbnail.png" "$DOCS/Banners/$BANNER"
cp "$BB_SCENARIO_DIR/../../../BeaverBuddies/thumbnail.png" "$DOCS/Tails/$TAIL"
python3 -I -c 'import sys, wave; w = wave.open(sys.argv[1], "wb"); w.setnchannels(1); w.setsampwidth(2); w.setframerate(22050); w.writeframes(b"\0\0" * 2205); w.close()' "$DOCS/Sounds/$SOUND.wav"

BANNER_AT="74 47 5"; TAIL_AT="80 47 5"; SPEAKER_AT="86 47 5"; LIGHT_AT="88 47 5"

# ask <copy> <command> <label>: sends the command and waits (10 s at most) for the copy's next "[Test] label: ..."
# line, printing what follows the label
ask() {
    local i n; n=$(grep -ac "\[Test\] $3: " "$(bb_log $1)")
    send $1 "$2"
    for ((i = 0; i < 20; i++)); do
        [ "$(grep -ac "\[Test\] $3: " "$(bb_log $1)")" -gt "$n" ] && break
        sleep 0.5
    done
    grep -aoE "\[Test\] $3: .*" "$(bb_log $1)" | tail -1 | sed "s/.*$3: //"
}
# What a copy says of the building there
decal_of() { ask $1 "decal $2" "Decal at $2"; }
light_of() { ask $1 "light $2" "Light at $2"; }
speaker_of() { ask $1 "speaker $2" "Speaker at $2"; }
# list_of <copy> <command> <label>: a list a copy logs, one item a line
list_of() { ask $1 "$2" "$3" | tr '|' '\n'; }

bb_close
export BB_HIDE_CUSTOM_HOST=1
start_match Folktails Folktails || { fail "a match starts" "the copies never got into the game"; return; }
send host "speed 1"; sleep 3

# The buildings with images to pick, as this game has them
send host decaltools; sleep 2
banner_tool=$(grep -aoE "\[Test\] Decal tool [^ ]+ category Banners tails False" "$(bb_log host)" | head -1 | awk '{ print $4 }')
tail_tool=$(grep -aoE "\[Test\] Decal tool [^ ]+ category Tails tails True" "$(bb_log host)" | head -1 | awk '{ print $4 }')
[ -n "$banner_tool" ] && [ -n "$tail_tool" ] || { fail "the game has a banner and a tail painter" "found '$banner_tool' and '$tail_tool'"; return; }
send host "buildnow $banner_tool $BANNER_AT"; send host "buildnow $tail_tool $TAIL_AT"
send host "buildnow HttpLever.Folktails $LIGHT_AT"; send host "buildnow Speaker.Folktails $SPEAKER_AT"
sleep 4

host_banners=$(list_of host "decals Banners" "Decals Banners"); client_banners=$(list_of client "decals Banners" "Decals Banners")
expect_match "the guest has the custom banner" "$client_banners" "^$BANNER$"
expect_no_match "the host doesn't" "$host_banners" "^$BANNER$"
default_banner=$(echo "$host_banners" | head -1); builtin=$(echo "$host_banners" | sed -n 2p)
default_tail=$(list_of host "decals Tails" "Decals Tails" | head -1)
default_sound=$(list_of host sounds "Sounds" | head -1 | sed 's/^built-in //')
custom_sound=$(ask client sounds "Sounds" | grep -oE "custom .*" | sed 's/^custom //' | tr '|' '\n' | grep "$SOUND" | head -1)
[ -n "$custom_sound" ] || fail "the guest has the custom sound" "not among its sounds"

# A built-in banner, picked by the guest
send client "decalpick $BANNER_AT Banners $builtin"; sleep 3
for copy in host client; do
    expect_eq "a built-in banner the guest picks ($copy)" "$(decal_of $copy "$BANNER_AT")" "Banners/$builtin, flipped False, shows $builtin"
done

# The custom one, by the guest: the host keeps its name and shows the default; flipped by the host
send client "decalpick $BANNER_AT Banners $BANNER"; sleep 3
expect_eq "a custom banner the guest picks, shown (client)" "$(decal_of client "$BANNER_AT")" "Banners/$BANNER, flipped False, shows $BANNER"
expect_eq "kept by name where there's no file, the default shown (host)" "$(decal_of host "$BANNER_AT")" "Banners/$BANNER, flipped False, shows $default_banner"
send host "flip $BANNER_AT on"; sleep 3
for copy in host client; do
    expect_match "a banner the host flips ($copy)" "$(decal_of $copy "$BANNER_AT")" "^Banners/$BANNER, flipped True, "
done
send host "decalrefresh Banners"; sleep 2
expect_eq "Refresh without the file keeps the name (host)" "$(decal_of host "$BANNER_AT")" "Banners/$BANNER, flipped True, shows $default_banner"

# A tail painter's custom image, by the guest
send client "decalpick $TAIL_AT Tails $TAIL"; sleep 3
expect_eq "a custom tail the guest picks, shown (client)" "$(decal_of client "$TAIL_AT")" "Tails/$TAIL, flipped False, shows $TAIL"
expect_eq "kept by name where there's no file, the default shown (host)" "$(decal_of host "$TAIL_AT")" "Tails/$TAIL, flipped False, shows $default_tail"

# A light: customized by the host, colored by the guest from its field, by the host from a preset, and by the guest's
# HTTP API
send host "lightcustomize $LIGHT_AT"; sleep 3
for copy in host client; do expect_match "a light the host customizes ($copy)" "$(light_of $copy "$LIGHT_AT")" "^customized True, "; done
send client "lightcolor $LIGHT_AT FF8800"; sleep 3
for copy in host client; do expect_eq "a color the guest types ($copy)" "$(light_of $copy "$LIGHT_AT")" "customized True, color FF8800"; done
send host "lightpreset $LIGHT_AT 3366FF"; sleep 3
for copy in host client; do expect_eq "a preset the host picks ($copy)" "$(light_of $copy "$LIGHT_AT")" "customized True, color 3366FF"; done
send client "lever $LIGHT_AT httpcolor 00FF00"; sleep 3
for copy in host client; do expect_eq "an HTTP Lever's color from the guest's API ($copy)" "$(light_of $copy "$LIGHT_AT")" "customized True, color 00FF00"; done

# A speaker's custom sound, by the guest: the host keeps its name and plays the default
send client "speaker $SPEAKER_AT $custom_sound"; sleep 3
expect_eq "a custom sound the guest picks, played (client)" "$(speaker_of client "$SPEAKER_AT")" "'$custom_sound', plays '$custom_sound'"
expect_eq "kept by name where there's no file, the default played (host)" "$(speaker_of host "$SPEAKER_AT")" "'$custom_sound', plays '$default_sound'"

# Both reloaded from the host's save, made without the files: the names are still there
send client "desync"
wait_for host "Back in sync after a resync" 120 && wait_for client "Back in sync after a resync" 120 \
    || fail "both copies reload from the host's save" "one never finished loading"
sleep 3; send host dismiss; send client dismiss; sleep 2
send host "speed 1"; sleep 3
expect_eq "after the reload, the custom banner, flipped (client)" "$(decal_of client "$BANNER_AT")" "Banners/$BANNER, flipped True, shows $BANNER"
expect_eq "and kept by name (host)" "$(decal_of host "$BANNER_AT")" "Banners/$BANNER, flipped True, shows $default_banner"
for copy in host client; do
    expect_match "the custom tail kept by name ($copy)" "$(decal_of $copy "$TAIL_AT")" "^Tails/$TAIL, "
    expect_match "the custom sound kept by name ($copy)" "$(speaker_of $copy "$SPEAKER_AT")" "^'$custom_sound', "
    expect_eq "the light's color ($copy)" "$(light_of $copy "$LIGHT_AT")" "customized True, color 00FF00"
done

expect_in_sync "both copies in sync"
# Not expect_no_errors, which counts the reload's desync
for copy in host client; do expect_no_log "no exceptions ($copy)" $copy "Exception:"; done
