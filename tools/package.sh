#!/bin/zsh
# Builds the distributable: BepInEx (arm64) bundled, the plugin, the self-contained fm and
# an install script, so a new Mac does not have to find BepInEx itself.
set -eu
cd "${0:A:h}/.."
GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26}"
STAGE=dist/fm-cli-osx-arm64
ZIP=dist/fm-cli-osx-arm64.zip

if [ ! -d "$GAME/BepInEx" ]; then
	echo "BepInEx not found: $GAME/BepInEx (install it on this machine first)"
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
[ -d "$GAME" ] || { echo "Game not found: $GAME"; exit 1; }
echo "installing BepInEx…"
cp -R bepinex/. "$GAME/"
mkdir -p "$GAME/BepInEx/plugins/FmCliProbe" "$HOME/.local/bin"
cp FmCliProbe.dll "$GAME/BepInEx/plugins/FmCliProbe/"
cp fm "$HOME/.local/bin/fm"; chmod +x "$HOME/.local/bin/fm"
if [ ! -d "$SHADOW" ]; then
	./make-shadow.sh
fi
echo "checking the install…"
"$HOME/.local/bin/fm" doctor --install
INSTALL
chmod +x "$STAGE/install.sh" "$STAGE/make-shadow.sh"
cat > "$STAGE/NOTICE.txt" <<'NOTICE'
This package contains BepInEx 6 (IL2CPP, arm64) — https://github.com/BepInEx/BepInEx — LGPL-2.1.
BepInEx and the FM26 Player Export (macOS) package keep their own licenses; their source is at the
address above. The fm-cli plugin (FmCliProbe) belongs to this repository.
NOTICE
cat > "$STAGE/README.txt" <<'TXT'
fm-cli (macOS, Apple Silicon)
1) Install Football Manager 26 from Steam.
2) Unpack this folder and run ./install.sh.
3) Check with fm doctor --install; start with fm start.
BepInEx 6 IL2CPP arm64 and its plugin are in this package (license: NOTICE.txt).
TXT
(cd dist && rm -f fm-cli-osx-arm64.zip && zip -qr fm-cli-osx-arm64.zip fm-cli-osx-arm64)
ls -lh "$ZIP"
