#!/bin/zsh
# The CLI as one self-contained binary for this Mac, plus the plugin beside it.
# Usage: tools/publish.sh   → dist/fm/fm and dist/FmCliProbe.dll
set -eu
cd "${0:A:h}/.."

dotnet publish cli/fm -c Release -r osx-arm64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
	-o dist/fm.next
# A running `fm mcp` maps the old file; writing over it breaks that process ("type initializer").
# Renaming keeps the old file alive for it until it exits.
mkdir -p dist/fm
for f in dist/fm.next/*; do mv -f "$f" dist/fm/; done
rmdir dist/fm.next

dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release >/dev/null
mkdir -p dist
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll dist/

echo
echo "ready: dist/fm/fm (one file, no SDK needed)"
echo "eklenti: dist/FmCliProbe.dll → BepInEx/plugins/FmCliProbe/"
