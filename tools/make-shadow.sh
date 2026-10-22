#!/bin/zsh
# Builds the shadow bundle the launcher starts: a tiny fm.app that points at Steam's
# Frameworks/PlugIns/Resources, carries its own executable and a patched Info.plist
# (LSUIElement, so no Dock icon), and is signed ad hoc.
#
# Usage: tools/make-shadow.sh [--force]
#   FM_HOME  the Steam game folder (default: the usual Steam path)
#   FM_APP   where the shadow goes  (default: ~/fm26_bep/fm.app)
set -eu
cd "${0:A:h}/.."

GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26}"
SHADOW="${FM_APP:-$HOME/fm26_bep/fm.app}"
SRC="$GAME/fm.app"

if [ ! -d "$SRC" ]; then
	echo "Steam paketi yok: $SRC"
	echo "Steam'den kur ya da FM_HOME ile göster."
	exit 1
fi
if [ -d "$SHADOW" ] && [ "${1:-}" != "--force" ]; then
	echo "Gölge paket zaten var: $SHADOW   (yeniden kurmak için: --force)"
	exit 0
fi

mkdir -p "$SHADOW/Contents/MacOS"
cp -f "$SRC/Contents/MacOS/Football Manager 26" "$SHADOW/Contents/MacOS/"
cp -f "$SRC/Contents/Info.plist" "$SHADOW/Contents/Info.plist"
for d in Frameworks PlugIns Resources; do
	rm -rf "$SHADOW/Contents/$d"
	ln -s "$SRC/Contents/$d" "$SHADOW/Contents/$d"
done
/usr/libexec/PlistBuddy -c "Add :LSUIElement bool true" "$SHADOW/Contents/Info.plist" >/dev/null 2>&1 \
	|| /usr/libexec/PlistBuddy -c "Set :LSUIElement true" "$SHADOW/Contents/Info.plist" >/dev/null
codesign --force --sign - "$SHADOW" >/dev/null 2>&1 || true

echo "gölge paket hazır: $SHADOW"
