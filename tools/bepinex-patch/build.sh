#!/bin/zsh
# Builds BepInEx.Unity.IL2CPP.dll with fm-cli's patch (MainThreadTick, the arm64 hook fixes) from BepInEx's own
# source at be.755's commit. The plugin needs the patched core on macOS and Windows alike; the assembly is managed
# (AnyCPU), one build serves both. Output: dist/bepinex-core/BepInEx.Unity.IL2CPP.dll
set -eu
cd "${0:A:h}/../.."
COMMIT=3fab71a1914132a1ce3a545caf3192da603f2258
SRC="${XDG_CACHE_HOME:-$HOME/.cache}/fm-cli/bepinex-src"
OUT=dist/bepinex-core
if [ ! -d "$SRC/.git" ]; then
	mkdir -p "$(dirname "$SRC")"
	git clone -q https://github.com/BepInEx/BepInEx.git "$SRC"
fi
git -C "$SRC" checkout -q --force "$COMMIT"
git -C "$SRC" clean -qfdx Runtimes/Unity/BepInEx.Unity.IL2CPP
git -C "$SRC" apply "$PWD/tools/bepinex-patch/il2cpp-main-thread-tick.patch"
dotnet build "$SRC/Runtimes/Unity/BepInEx.Unity.IL2CPP/BepInEx.Unity.IL2CPP.csproj" -c Release -p:GeneratePackageOnBuild=false >/dev/null
mkdir -p "$OUT"
cp "$SRC/Runtimes/Unity/BepInEx.Unity.IL2CPP/bin/Unity.IL2CPP/BepInEx.Unity.IL2CPP.dll" "$OUT/"
echo "$OUT/BepInEx.Unity.IL2CPP.dll"
