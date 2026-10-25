#!/bin/zsh
# Installs what this repository can install, then says what is still missing.
# BepInEx itself is not redistributed here: put its arm64 IL2CPP package under the game
# folder once, and the shadow bundle ~/fm26_bep/fm.app beside it.
set -eu
cd "${0:A:h}/.."

GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 26}"
PLUGIN="$GAME/BepInEx/plugins/FmCliProbe"
BIN="$HOME/.local/bin"

if [ ! -d "$GAME" ]; then
	echo "Game folder not found: $GAME"
	echo "Install it from Steam, or point at it with FM_HOME."
	exit 1
fi
if [ ! -d "${FM_APP:-$HOME/fm26_bep/fm.app}" ]; then
	echo "building the shadow app…"
	tools/make-shadow.sh
fi
if [ ! -f "$GAME/run_bepinex_arm64.sh" ]; then
	echo "BepInEx launcher not found: $GAME/run_bepinex_arm64.sh"
	echo "Unpack BepInEx 6 IL2CPP arm64 into the game folder, then run this again."
	exit 1
fi

echo "building the plugin…"
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release >/dev/null
mkdir -p "$PLUGIN"
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll "$PLUGIN/"

echo "building fm…"
dotnet publish cli/fm -c Release -r osx-arm64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
	-o dist/fm >/dev/null
mkdir -p "$BIN"
ln -sf "$PWD/dist/fm/fm" "$BIN/fm"

echo
"$BIN/fm" doctor --install || true
