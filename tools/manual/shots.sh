#!/bin/bash
# Re-captures the screenshots in docs/manual/img from the running game.
# Usage: tools/manual/shots.sh            all shots
#        tools/manual/shots.sh squad      only the ones whose name matches
# Only read-only commands live here; the game state is not changed.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
OUT="$ROOT/docs/manual/img"
WORK="$(mktemp -d)"
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"
COLS=${COLS:-110}
mkdir -p "$OUT"

# name | command
SHOTS=(
	"status|fm status"
	"inbox|fm inbox"
	"squad|fm squad"
	"squad-fitness|fm squad --fitness"
	"squad-morale|fm squad --morale"
	"squad-stats|fm squad --stats"
	"player|fm player Ederson"
	"tactic|fm tactic"
	"table|fm table"
	"fixtures|fm fixtures"
	"report|fm report"
	"search|fm search Arda"
	"career|fm player Kerem --career"
	"calendar|fm calendar eylül"
	"fixtures-all|fm fixtures --all"
	"budget|fm budget"
	"contracts|fm contracts"
	"health|fm health"
	"doctor|fm doctor"
	"league|fm league"
	"schedule|fm schedule"
	"coaches|fm coaches"
	"club|fm club"
	"staff|fm staff"
	"finances|fm finances"
	"finances-expenditure|fm finances gider sezon"
	"supporters|fm supporters"
	"board|fm board"
	"facilities|fm facilities"
	"stages|fm stages 'turkish cup' 'second round'"
	"scout|fm scout"
	"scout-focus|fm scout 5"
	"progress|fm progress"
	"youth|fm youth"
	"world|fm world"
	"responsibilities|fm responsibilities medya"
	"opponent|fm opponent"
)

shoot() {
	local name="$1" cmd="$2"
	script -q "$WORK/$name.txt" sh -c "stty cols $COLS rows 60; $cmd" </dev/null >/dev/null || true
	{ printf '\033[2m$\033[0m %s\n' "$cmd"; cat "$WORK/$name.txt"; } >"$WORK/$name.ansi"
	local size; size=$(python3 "$ROOT/tools/manual/ansi2html.py" "$WORK/$name.ansi" "$WORK/$name.html" "$cmd")
	local width=${size% *} lines=${size#* }
	local h=$(( lines * 18 + 52 )) w=$(( width * 8 + 38 ))
	"$CHROME" --headless=new --disable-gpu --hide-scrollbars --force-device-scale-factor=2 \
		--default-background-color=0e1013ff --window-size="$w,$h" \
		--screenshot="$OUT/$name.png" "file://$WORK/$name.html" >/dev/null 2>&1
	echo "  $name.png"
}

for s in "${SHOTS[@]}"; do
	name="${s%%|*}"; cmd="${s#*|}"
	[[ $# -gt 0 && "$name" != *"$1"* ]] && continue
	shoot "$name" "$cmd"
done
rm -rf "$WORK"
