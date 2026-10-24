# Mods built before the Game Modes mod add game settings through BeaverBuddies' old calls, with the kinds of
# game they show for as a list of names: those names still pick the same games
bb_close
bb_launch host || { fail "the game starts" "no main menu"; return; }
send host "compatsettings"; sleep 2
expect_log "no list shows for every game" host "compat all: A=True B=True C=True"
expect_log "bb.alone shows for a game alone only" host "compat alone: A=True B=False C=False"
expect_log "bb.others shows for any game with others" host "compat others: A=False B=True C=True"
expect_log "a mode's id shows for it with others only" host "compat pve: A=False B=True C=False"
expect_log "bb.alone:<mode> shows for a game alone in it only" host "compat alonepve: A=True B=False C=False"
expect_no_errors "no errors"
bb_close
