#!/bin/zsh
# Installs what this repository can install, then says what is still missing.
# BepInEx itself is not redistributed here: put its arm64 IL2CPP package in ~/fm27_bep/game once.
# The shadow bundle ~/fm27_bep/fm.app is built by the launcher on the first start.
set -eu
cd "${0:A:h}/.."

GAME="${FM_HOME:-$HOME/Library/Application Support/Steam/steamapps/common/Football Manager 27}"
BESIDE="$(dirname "${FM_APP:-$HOME/fm27_bep/fm.app}")/game"
PLUGIN="$BESIDE/BepInEx/plugins/FmCliProbe"
BIN="$HOME/.local/bin"

if [ ! -d "$GAME" ]; then
	echo "Game folder not found: $GAME"
	echo "Install it from Steam, or point at it with FM_HOME."
	exit 1
fi
if [ ! -d "$BESIDE/BepInEx/core" ]; then
	echo "BepInEx not found: $BESIDE/BepInEx"
	echo "Unpack BepInEx 6 IL2CPP arm64 there, then run this again."
	exit 1
fi
cp tools/fm27-bep/run_bepinex_arm64.sh "$BESIDE/"

echo "building the plugin…"
FM26_BEP="$(dirname "$BESIDE")" dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release >/dev/null
mkdir -p "$PLUGIN"
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll "$PLUGIN/"

echo "building fm…"
dotnet publish cli/fm -c Release -r osx-arm64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
	-o dist/fm.next >/dev/null
# A running `fm mcp` maps the old file; writing over it breaks that process ("type initializer").
# Renaming keeps the old file alive for it until it exits.
mkdir -p dist/fm
for f in dist/fm.next/*; do mv -f "$f" dist/fm/; done
rmdir dist/fm.next
# The single file unpacks itself under ~/.net/fm, a new folder (~34 MB) for every build, never removed.
# The newest three are kept: a running `fm mcp` can still hold an older build's.
if [ -d "$HOME/.net/fm" ]; then
	ls -t "$HOME/.net/fm" | tail -n +4 | while read -r d; do rm -rf -- "$HOME/.net/fm/$d"; done
fi
mkdir -p "$BIN"
ln -sf "$PWD/dist/fm/fm" "$BIN/fm"

echo
"$BIN/fm" doctor --install || true
