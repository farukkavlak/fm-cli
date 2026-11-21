#!/bin/zsh
# Installs fm-cli from the latest release:
#   curl -fsSL https://raw.githubusercontent.com/farukkavlak/fm-cli/main/install.sh | zsh
# Downloads the package, unpacks it in a temporary folder and runs the install script inside it.
set -eu
if [ "$(uname -s)" != Darwin ] || [ "$(uname -m)" != arm64 ]; then
	echo "fm-cli needs a Mac with Apple Silicon for now."
	exit 1
fi
URL="${FM_CLI_ZIP:-https://github.com/farukkavlak/fm-cli/releases/latest/download/fm-cli-osx-arm64.zip}"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT
echo "downloading fm-cli…"
curl -fSL --progress-bar "$URL" -o "$TMP/fm-cli.zip"
ditto -x -k "$TMP/fm-cli.zip" "$TMP"
zsh "$TMP/fm-cli-osx-arm64/install.sh" </dev/null
