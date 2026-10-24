#!/bin/zsh
# Builds the distributable: BepInEx (arm64) bundled, the plugin, the self-contained fm and
# an install script, so a new Mac does not have to find BepInEx itself.
set -eu
cd "${0:A:h}/.."
GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26}"
STAGE=dist/fm-cli-osx-arm64
ZIP=dist/fm-cli-osx-arm64.zip

if [ ! -d "$GAME/BepInEx" ]; then
	echo "BepInEx yok: $GAME/BepInEx (önce bu makinede kurulu olmalı)"
	exit 1
fi
rm -rf "$STAGE"; mkdir -p "$STAGE/bepinex"
# The BepInEx installation: its folder and the launcher files at the game's root.
cp -R "$GAME/BepInEx" "$STAGE/bepinex/"
for f in run_bepinex_arm64.sh libdoorstop.dylib doorstop_config.ini .doorstop_version; do
	[ -e "$GAME/$f" ] && cp -R "$GAME/$f" "$STAGE/bepinex/"
done

tools/publish.sh >/dev/null
cp dist/fm/fm "$STAGE/fm"
cp dist/FmCliProbe.dll "$STAGE/"
cp tools/make-shadow.sh "$STAGE/"
cat > "$STAGE/install.sh" <<'INSTALL'
#!/bin/zsh
# Run from the unpacked folder: installs BepInEx, the plugin and fm into this Mac's FM 26.
set -eu
cd "${0:A:h}"
GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26}"
SHADOW="${FM_APP:-$HOME/fm26_bep/fm.app}"
export FM_HOME="$GAME"
[ -d "$GAME" ] || { echo "Oyun yok: $GAME"; exit 1; }
echo "BepInEx kuruluyor…"
cp -R bepinex/. "$GAME/"
mkdir -p "$GAME/BepInEx/plugins/FmCliProbe" "$HOME/.local/bin"
cp FmCliProbe.dll "$GAME/BepInEx/plugins/FmCliProbe/"
cp fm "$HOME/.local/bin/fm"; chmod +x "$HOME/.local/bin/fm"
if [ ! -d "$SHADOW" ]; then
	./make-shadow.sh
fi
echo "Kurulum denetleniyor…"
"$HOME/.local/bin/fm" doctor --install
INSTALL
chmod +x "$STAGE/install.sh" "$STAGE/make-shadow.sh"
cat > "$STAGE/NOTICE.txt" <<'NOTICE'
Bu paket BepInEx 6 (IL2CPP, arm64) içerir — https://github.com/BepInEx/BepInEx — LGPL-2.1.
BepInEx'in ve FM26 Player Export (macOS) paketinin lisansları kendi projelerine aittir; kaynak
kodları yukarıdaki adreste bulunur. fm-cli eklentisi (FmCliProbe) bu depoya aittir.
NOTICE
cat > "$STAGE/README.txt" <<'TXT'
fm-cli (macOS, Apple Silicon)
1) Football Manager 26'yı Steam'den kur.
2) Bu klasörü aç ve ./install.sh çalıştır.
3) fm doctor --install ile denetle; fm start ile başlat.
BepInEx 6 IL2CPP arm64 ve eklentisi bu pakette (lisans: NOTICE.txt).
TXT
(cd dist && rm -f fm-cli-osx-arm64.zip && zip -qr fm-cli-osx-arm64.zip fm-cli-osx-arm64)
ls -lh "$ZIP"
