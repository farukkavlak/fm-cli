#!/bin/zsh
# Builds the Windows distributable (T52) on this Mac: the self-contained fm.exe, the plugin and BepInEx 6 IL2CPP
# win-x64 (the same build as the macOS one, be.755), with an install script. Untested on Windows until T81.
set -eu
cd "${0:A:h}/.."
STAGE=dist/fm-cli-win-x64
ZIP=dist/fm-cli-win-x64.zip
BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/755/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755%2B3fab71a.zip"
CACHE="${XDG_CACHE_HOME:-$HOME/.cache}/fm-cli/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755.zip"

rm -rf "$STAGE"; mkdir -p "$STAGE/game"
if [ ! -s "$CACHE" ]; then
	mkdir -p "$(dirname "$CACHE")"
	curl -fsSL -o "$CACHE.part" "$BEPINEX_URL"
	mv "$CACHE.part" "$CACHE"
fi
# BepInEx as its zip lays it out beside a game's executable: BepInEx/, dotnet/, winhttp.dll, doorstop_config.ini.
unzip -q "$CACHE" -d "$STAGE/game"
[ -f "$STAGE/game/winhttp.dll" ] && [ -d "$STAGE/game/BepInEx/core" ] || { echo "unexpected BepInEx layout in $CACHE"; exit 1; }
# The plugin needs BepInEx's main-thread tick (IL2CPPChainloader.MainThreadTick), which be.755 as released does not
# have: the core assembly is built from BepInEx's source with tools/bepinex-patch (managed, AnyCPU).
zsh tools/bepinex-patch/build.sh >/dev/null
cp dist/bepinex-core/BepInEx.Unity.IL2CPP.dll "$STAGE/game/BepInEx/core/BepInEx.Unity.IL2CPP.dll"
# Only what differs from BepInEx's defaults (it writes the rest on the first start): no console window beside the
# hidden game. The macOS settings are not copied: they are its launcher's (interop kept, console on).
mkdir -p "$STAGE/game/BepInEx/config"
printf '[Logging.Console]\nEnabled = false\n' > "$STAGE/game/BepInEx/config/BepInEx.cfg"

dotnet publish cli/fm -c Release -r win-x64 --self-contained true \
	-p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "$STAGE/bin" >/dev/null
cp "$STAGE/bin/fm.exe" "$STAGE/fm.exe"
rm -rf "$STAGE/bin"
FM26_BEP="${FM26_BEP:-$HOME/fm27_bep}" dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release >/dev/null
cp plugin/FmCliProbe/bin/Release/net6.0/FmCliProbe.dll "$STAGE/"
cp tools/win/install.ps1 "$STAGE/install.ps1"
cat > "$STAGE/NOTICE.txt" <<'NOTICE'
This package contains BepInEx 6 (IL2CPP, win-x64, be.755) — https://github.com/BepInEx/BepInEx — LGPL-2.1.
BepInEx keeps its own license; its source is at the address above. The fm-cli plugin (FmCliProbe)
belongs to this repository.
NOTICE
cat > "$STAGE/README.txt" <<'TXT'
fm-cli (Windows x64)
1) Install Football Manager 27 from Steam and start it once.
2) Unpack this folder, then in PowerShell: powershell -ExecutionPolicy Bypass -File install.ps1
3) Check with fm doctor --install; with Steam open, start with fm start.
BepInEx 6 IL2CPP win-x64 and its plugin are in this package (license: NOTICE.txt).
TXT
rm -f "$ZIP"
(cd dist && zip -qr "${ZIP:t}" "${STAGE:t}")
echo "$ZIP"
