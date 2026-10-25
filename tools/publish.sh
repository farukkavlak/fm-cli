#!/bin/zsh
# The CLI as one self-contained binary for this Mac, plus the plugin beside it.
# Usage: tools/publish.sh   → dist/fm/fm and dist/FmCliProbe.dll
set -eu
cd "${0:A:h}/.."

dotnet publish cli/fm -c Release -r osx-arm64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
	-o dist/fm

dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release >/dev/null
mkdir -p dist
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll dist/

echo
echo "ready: dist/fm/fm (one file, no SDK needed)"
echo "eklenti: dist/FmCliProbe.dll → BepInEx/plugins/FmCliProbe/"
