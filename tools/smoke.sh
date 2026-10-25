#!/bin/bash
# Local pre-flight: compile both projects, run the tests and check the published CLI.
# The live status check is skipped when the game bridge is not up.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

echo "— CLI"
dotnet build cli/fm/fm.csproj -c Release --nologo

echo "— plugin"
dotnet build plugin/FmCliProbe/FmCliProbe.csproj -c Release --nologo

echo "— tests"
dotnet test tests/FmCli.Tests/FmCli.Tests.csproj -c Release --nologo

echo "— help"
[[ -x dist/fm/fm ]] && dist/fm/fm help >/dev/null

echo "— live status"
if fm status >/dev/null 2>&1; then
	fm status | head -4
else
	echo "  (game bridge is down; skipped)"
fi

echo "Smoke: ok"
