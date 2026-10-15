#!/bin/zsh
# fm-cli gece otopilotu.
#
# Bağımlılık checklist'ini (docs/BAGIMLILIKLAR.md) tek madde tek madde, her turda taze bir
# headless OpenCode oturumunda temizler (turlar arasında /clear gibi). "Sabaha kadar"ı yapan
# şey bu döngüdür; her tur ayrı bir `opencode run` çağrısıdır.
#
# Kullanım : tools/autopilot.sh [en fazla tur] [bitiş saati HH:MM]
# Durdur   : touch ~/.config/fm-cli/autopilot-stop
# Log      : ~/.config/fm-cli/autopilot.log
# Ayarlar  : AUTOPILOT_MODEL, AUTOPILOT_AGENT, AUTOPILOT_ROUND_MIN (tur zaman aşımı, dk),
#            AUTOPILOT_STANDALONE=0 (paylaşılan servisi kullan; varsayılan 1 = yalnız sunucu)
set -u
cd "${0:A:h}/.."

MAX=${1:-200}
DEADLINE=${2:-07:00}
MODEL=${AUTOPILOT_MODEL:-opencode-go/deepseek-v4.1-flash}
AGENT=${AUTOPILOT_AGENT:-autopilot}
ROUND_MIN=${AUTOPILOT_ROUND_MIN:-40}
STANDALONE=${AUTOPILOT_STANDALONE:-1}

STOP="$HOME/.config/fm-cli/autopilot-stop"
LOG="$HOME/.config/fm-cli/autopilot.log"
LOCK="$HOME/.config/fm-cli/autopilot.lock"
CHECKLIST=docs/BAGIMLILIKLAR.md
PROMPT_FILE=tools/autopilot-prompt.md

mkdir -p "${LOG:h}"; rm -f "$STOP"

log() { echo "$(date '+%F %T') $*" | tee -a "$LOG"; }

# Tek oyun var; iki sürücü birbirinin işini bozar.
if ! mkdir "$LOCK" 2>/dev/null; then
	echo "otopilot zaten çalışıyor ($LOCK); değilse o klasörü sil."; exit 1
fi
trap 'rmdir "$LOCK" 2>/dev/null' EXIT
trap 'rmdir "$LOCK" 2>/dev/null; exit 130' INT TERM

# Bitiş: bugünün HH:MM'i geçtiyse yarınınki; çözülemezse 8 saat sonrası.
now=$(date +%s)
if dl=$(date -j -f "%H:%M" "$DEADLINE" "+%s" 2>/dev/null); then
	[ "$dl" -le "$now" ] && dl=$((dl + 86400))
else
	dl=$((now + 8 * 3600))
fi

branch=$(git rev-parse --abbrev-ref HEAD)
if [ "$branch" != "main" ]; then log "UYARI: main'de değiliz ($branch) — çalışmıyorum."; exit 1; fi

log "otopilot başladı — model=$MODEL agent=$AGENT tur≤$MAX bitiş=$DEADLINE standalone=$STANDALONE"

# Tek tur: telefonu kapat, işi yaptır; takılırsa ROUND_MIN sonra öldür.
run_round() {
	local timeout=$((ROUND_MIN * 60))
	local flags=(--auto --agent "$AGENT" --model "$MODEL")
	[ "$STANDALONE" = "1" ] && flags+=(--standalone)
	opencode run "${flags[@]}" "$(cat $PROMPT_FILE)" >>"$LOG" 2>&1 &
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

stuck=0
for round in $(seq 1 $MAX); do
	if [ -e "$STOP" ]; then log "durduruldu (stop dosyası)"; break; fi
	if [ "$(date +%s)" -ge "$dl" ]; then log "bitiş saati geldi ($DEADLINE)"; break; fi

	open_items=$(sed -n '/^## CHECKLIST/,/^## Arka plan/p' "$CHECKLIST" | grep -cE '^[[:space:]]*- \[ \]')
	if [ "$open_items" -eq 0 ]; then log "checklist bitti"; break; fi

	if ! fm status </dev/null >/dev/null 2>&1; then
		log "köprü sessiz — fm start deneniyor"
		fm start </dev/null >>"$LOG" 2>&1 || log "fm start başarısız (tur yine de denenecek)"
	fi

	before=$(git rev-parse HEAD)
	log "tur $round — açık madde: $open_items"
	run_round; rc=$?
	if [ "$(git rev-parse HEAD)" = "$before" ]; then
		log "tur $round bitti — rc=$rc (commit yok)"
	else
		log "tur $round bitti — rc=$rc ($(git log -1 --pretty=%h' '%s))"
	fi

	if [ "$(git rev-parse HEAD)" = "$before" ]; then stuck=$((stuck + 1)); else stuck=0; fi
	if [ "$stuck" -ge 2 ]; then log "iki turdur commit yok — duruyorum"; break; fi
done

log "otopilot sona erdi"
