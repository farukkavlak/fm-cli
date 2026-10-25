#!/bin/bash
# CI check: everything that does not need the game. Compiles the CLI, runs the unit tests and
# checks that the published binary starts. The plugin is built only when the game's interop
# assemblies are present (they are not in CI); smoke.sh is the fuller local pre-flight.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

OUT="$(mktemp -d)"
trap 'rm -rf "$OUT"' EXIT

echo "— CLI"
dotnet build cli/fm/fm.csproj -c Release --nologo

echo "— tests"
dotnet test tests/FmCli.Tests/FmCli.Tests.csproj -c Release --nologo

echo "— publish"
dotnet publish cli/fm/fm.csproj -c Release -o "$OUT" --nologo >/dev/null
"$OUT/fm" help >/dev/null

echo "— plugin"
BEP="${FM26_BEP:-$HOME/fm26_bep}"
if [[ -f "$BEP/BepInEx/core/BepInEx.Core.dll" ]]; then
	dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release --nologo
else
	echo "  (game interop assemblies not found; skipped)"
fi

echo "CI: ok"
