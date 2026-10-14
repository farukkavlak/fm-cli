#!/bin/zsh
# Clears the dependency checklist one item at a time, each in a fresh Claude session (the same as /clear between tasks).
# Usage: tools/autopilot.sh [max-rounds]        Stop: touch ~/.config/fm-cli/autopilot-stop   Log: ~/.config/fm-cli/autopilot.log
set -u
cd "${0:A:h}/.."
MAX=${1:-40}
STOP="$HOME/.config/fm-cli/autopilot-stop"
LOG="$HOME/.config/fm-cli/autopilot.log"
CHECKLIST=docs/BAGIMLILIKLAR.md
mkdir -p "${LOG:h}"; rm -f "$STOP"
# One at a time: there is one game, and two sessions driving it would undo each other.
LOCK="$HOME/.config/fm-cli/autopilot.lock"
if ! mkdir "$LOCK" 2>/dev/null; then echo "autopilot zaten çalışıyor ($LOCK); değilse o klasörü sil."; exit 1; fi
trap 'rmdir "$LOCK"' EXIT INT TERM
stuck=0
for round in $(seq 1 $MAX); do
	open_items=$(sed -n '/^## CHECKLIST/,/^## Arka plan/p' $CHECKLIST | grep -cE '^\s*- \[ \]')
	if [ "$open_items" -eq 0 ]; then echo "$(date '+%F %T') checklist bitti" | tee -a "$LOG"; break; fi
	if [ -e "$STOP" ]; then echo "$(date '+%F %T') durduruldu (stop dosyası)" | tee -a "$LOG"; break; fi
	before=$(git rev-parse HEAD)
	echo "$(date '+%F %T') tur $round — açık madde: $open_items" | tee -a "$LOG"
	last=$(claude -p "$(cat tools/autopilot-prompt.md)" --permission-mode auto 2>>"$LOG" | tail -1)
	echo "$(date '+%F %T')   → $last" | tee -a "$LOG"
	# Two rounds in a row with no commit: something is wrong that a fresh start does not fix.
	if [ "$(git rev-parse HEAD)" = "$before" ]; then stuck=$((stuck + 1)); else stuck=0; fi
	if [ $stuck -ge 2 ]; then echo "$(date '+%F %T') iki turdur commit yok, duruyorum" | tee -a "$LOG"; break; fi
done
