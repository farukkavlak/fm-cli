#!/bin/zsh
# Builds the distributable: BepInEx (arm64) bundled, the plugin, the self-contained fm and
# an install script, so a new Mac does not have to find BepInEx itself.
set -eu
cd "${0:A:h}/.."
BESIDE="$(dirname "${FM_APP:-$HOME/fm27_bep/fm.app}")/game"
STAGE=dist/fm-cli-osx-arm64
ZIP=dist/fm-cli-osx-arm64.zip

if [ ! -d "$BESIDE/BepInEx/core" ]; then
	echo "BepInEx not found: $BESIDE/BepInEx (install it on this machine first)"
	exit 1
fi
rm -rf "$STAGE"; mkdir -p "$STAGE/game/BepInEx"
# BepInEx and what starts it. Its generated files (interop, cache) belong to one game version: the first start makes them.
for d in core patchers unity-libs; do
	[ -e "$BESIDE/BepInEx/$d" ] && cp -R "$BESIDE/BepInEx/$d" "$STAGE/game/BepInEx/"
done
# Only BepInEx's own settings: other mods' settings and plugins on this machine stay out.
mkdir -p "$STAGE/game/BepInEx/config"
cp "$BESIDE/BepInEx/config/BepInEx.cfg" "$STAGE/game/BepInEx/config/"
cp -R "$BESIDE/dotnet_arm64" "$BESIDE/libdoorstop.dylib" "$STAGE/game/"
cp tools/fm27-bep/run_bepinex_arm64.sh "$STAGE/game/"

tools/publish.sh >/dev/null
cp dist/fm/fm "$STAGE/fm"
cp dist/FmCliProbe.dll "$STAGE/"
cat > "$STAGE/install.sh" <<'INSTALL'
#!/bin/zsh
# Run from the unpacked folder: installs BepInEx, the plugin and fm for this Mac's FM27.
set -eu
cd "${0:A:h}"
# A downloaded zip leaves every file quarantined: macOS then refuses the unsigned fm and BepInEx's libraries.
xattr -dr com.apple.quarantine . 2>/dev/null || true
GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 27}"
BESIDE="$(dirname "${FM_APP:-$HOME/fm27_bep/fm.app}")/game"
[ -d "$GAME" ] || { echo "Game not found: $GAME"; exit 1; }
echo "installing BepInEx…"
mkdir -p "$BESIDE" "$HOME/.local/bin"
cp -R game/. "$BESIDE/"
mkdir -p "$BESIDE/BepInEx/plugins/FmCliProbe"
cp FmCliProbe.dll "$BESIDE/BepInEx/plugins/FmCliProbe/"
cp fm "$HOME/.local/bin/fm"; chmod +x "$HOME/.local/bin/fm"
echo "checking the install…"
"$HOME/.local/bin/fm" doctor --install || true
case ":$PATH:" in
	*":$HOME/.local/bin:"*) ;;
	*)
		echo
		echo "fm is in ~/.local/bin, which is not on your PATH. Add it once, for your shell:"
		case "${SHELL##*/}" in
			bash) echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.bash_profile && exec bash" ;;
			fish) echo "  fish_add_path ~/.local/bin" ;;
			*) echo "  echo 'export PATH=\"\$HOME/.local/bin:\$PATH\"' >> ~/.zshrc && exec zsh" ;;
		esac
		;;
esac
INSTALL
chmod +x "$STAGE/install.sh"
cat > "$STAGE/NOTICE.txt" <<'NOTICE'
This package contains BepInEx 6 (IL2CPP, arm64) — https://github.com/BepInEx/BepInEx — LGPL-2.1.
BepInEx and its macOS launcher keep their own licenses; their source is at the
address above. The fm-cli plugin (FmCliProbe) belongs to this repository.
NOTICE
cat > "$STAGE/README.txt" <<'TXT'
fm-cli (macOS, Apple Silicon)
1) Install Football Manager 27 from Steam.
2) Unpack this folder and run: zsh install.sh
3) Check with fm doctor --install; start with fm start.
BepInEx 6 IL2CPP arm64 and its plugin are in this package (license: NOTICE.txt).
TXT
(cd dist && rm -f fm-cli-osx-arm64.zip && zip -qr fm-cli-osx-arm64.zip fm-cli-osx-arm64)
ls -lh "$ZIP"
