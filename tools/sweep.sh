#!/bin/bash
# Runs every read-only command once against the running game and says which failed, how long each
# took and how much it printed. For a new save, club or game version: a command written against one
# club's screens shows up here as a failure or an empty answer.
# Usage: tools/sweep.sh [output-dir]     (the game must be between matches, not on a results screen)
set -uo pipefail
OUT="${1:-$(mktemp -d)}"
mkdir -p "$OUT"
COMMANDS=(
	"status" "squad" "squad --fitness" "squad --morale" "squad --stats" "squad --u19" "table" "fixtures" "calendar" "news"
	"finances" "budget" "board" "club" "facilities" "happiness" "dynamics" "training" "schedule" "intensity"
	"coaches" "staff" "scout" "shortlist" "contracts" "offers" "health" "internationals" "mentoring" "captains"
	"takers" "penalties" "tactic" "depth" "opponent" "opposition" "progress" "world" "league" "supporters"
	"numbers" "register" "responsibilities" "youth" "youth --training" "report" "saves" "targets" "stages" "prep"
	"inbox" "read" "read club-history" "read data-hub" "manager"
	"news 1" "tactic personal" "planner" "transferroom" "jobs" "fixtures --all" "read award-winners" "internationals --json"
)
# A game that is not running or has no save loaded fails every command for the same reason: say it once.
if ! fm status < /dev/null > /dev/null 2>&1; then
	echo "fm status fails: start the game and load a save first."; exit 2
fi
failed=0
i=0
for c in "${COMMANDS[@]}"; do
	i=$((i + 1))
	f="$OUT/$(printf %02d $i)-${c// /_}.txt"
	start=$(date +%s)
	# shellcheck disable=SC2086
	fm $c < /dev/null > "$f" 2> "$f.err"
	rc=$?
	lines=$(wc -l < "$f" | tr -d ' ')
	mark="ok"
	if [ $rc -ne 0 ] || [ "$lines" -eq 0 ]; then mark="FAIL"; failed=$((failed + 1)); fi
	printf "%-5s %-20s rc=%-3s %3ss %4s lines  %s\n" "$mark" "$c" "$rc" "$(( $(date +%s) - start ))" "$lines" "$(head -1 "$f.err" | cut -c1-80)"
done
echo
echo "${#COMMANDS[@]} commands, $failed failed. Output: $OUT"
[ $failed -eq 0 ]
