#!/bin/zsh
# Gece playtesti: kariyeri oynar, hata arar, koda dokunmaz.
# Kullanım : tools/playtest.sh [en fazla tur] [bitiş saati HH:MM]
# Durdur   : touch ~/.config/fm-cli/playtest-stop
# Log      : ~/.config/fm-cli/playtest.log
# Ayarlar  : PLAYTEST_MODEL, PLAYTEST_ROUND_MIN
set -u
export PATH="$HOME/.bun/bin:/usr/local/bin:/opt/homebrew/bin:/usr/bin:/bin:/usr/sbin:/sbin:$PATH"
cd "${0:A:h}/.."

MAX=${1:-20}
DEADLINE=${2:-08:00}
MODEL=${PLAYTEST_MODEL:-opencode-go/deepseek-v4.1-flash}
ROUND_MIN=${PLAYTEST_ROUND_MIN:-45}

STOP="$HOME/.config/fm-cli/playtest-stop"
LOG="$HOME/.config/fm-cli/playtest.log"
LOCK="$HOME/.config/fm-cli/playtest.lock"
PROMPT_FILE=tools/playtest-prompt.md

mkdir -p "${LOG:h}"; rm -f "$STOP"
log() { echo "$(date '+%F %T') $*" | tee -a "$LOG"; }

if ! mkdir "$LOCK" 2>/dev/null; then
	echo "playtest zaten çalışıyor ($LOCK); değilse o klasörü sil."; exit 1
fi
trap 'rmdir "$LOCK" 2>/dev/null' EXIT
trap 'rmdir "$LOCK" 2>/dev/null; exit 130' INT TERM

now=$(date +%s)
if dl=$(date -j -f "%H:%M" "$DEADLINE" "+%s" 2>/dev/null); then
	[ "$dl" -le "$now" ] && dl=$((dl + 86400))
else
	dl=$((now + 8 * 3600))
fi

log "playtest başladı — model=$MODEL tur≤$MAX bitiş=$DEADLINE"

run_round() {
	local timeout=$((ROUND_MIN * 60))
	opencode run --auto --agent autopilot --model "$MODEL" --standalone "$(cat $PROMPT_FILE)" >>"$LOG" 2>&1 &
	local pid=$!
	local waited=0
	while kill -0 "$pid" 2>/dev/null; do
		sleep 15; waited=$((waited + 15))
		if [ "$waited" -ge "$timeout" ]; then
			log "tur zaman aşımı (${ROUND_MIN} dk) — süreç öldürülüyor"
			pkill -TERM -P "$pid" 2>/dev/null
			kill -TERM "$pid" 2>/dev/null
			sleep 5
			kill -KILL "$pid" 2>/dev/null
			return 124
		fi
	done
	wait "$pid"
	return $?
}

for round in $(seq 1 $MAX); do
	if [ -e "$STOP" ]; then log "durduruldu (stop dosyası)"; break; fi
	if [ "$(date +%s)" -ge "$dl" ]; then log "bitiş saati geldi ($DEADLINE)"; break; fi

	if ! fm status </dev/null >/dev/null 2>&1; then
		log "köprü sessiz — fm start deneniyor"
		fm start </dev/null >>"$LOG" 2>&1 || log "fm start başarısız"
	fi

	log "tur $round — maç: $(fm status 2>/dev/null | head -1)"
	run_round; rc=$?
	log "tur $round bitti — rc=$rc"
done

log "playtest sona erdi"
