#!/bin/zsh
# A release: builds the package, writes the Homebrew formula for it and publishes both.
#   tools/release.sh 0.9.1            # package, Formula/fm-cli.rb, then asks before publishing
#   tools/release.sh 0.9.1 --formula  # only the formula, for a zip already in dist/
set -eu
cd "${0:A:h}/.."
VERSION="${1:?usage: tools/release.sh <version> [--formula]}"
ZIP=dist/fm-cli-osx-arm64.zip
[ "${2:-}" = --formula ] || tools/package.sh
SUM=$(shasum -a 256 "$ZIP" | cut -d' ' -f1)
URL="${FM_CLI_FORMULA_URL:-https://github.com/farukkavlak/fm-cli/releases/download/v$VERSION/fm-cli-osx-arm64.zip}"

mkdir -p Formula
cat > Formula/fm-cli.rb <<RUBY
# Written by tools/release.sh: url and sha256 belong to the release's zip.
class FmCli < Formula
  desc "Play Football Manager 27 from the terminal and over MCP"
  homepage "https://github.com/farukkavlak/fm-cli"
  url "$URL"
  version "$VERSION"
  sha256 "$SUM"
  license "MIT"

  depends_on arch: :arm64
  depends_on :macos

  def install
    # The package as it is (fm, the plugin, BepInEx beside the game); fm doctor --install puts BepInEx in place.
    libexec.install Dir["*"]
    bin.install_symlink libexec/"fm"
  end

  def caveats
    <<~EOS
      Puts BepInEx and the plugin beside the game, and downloads the language model
      (run it once now and after each upgrade, with the game closed):
        fm doctor --install
      Open the game once and save a career before the first: fm start
    EOS
  end

  test do
    system bin/"fm", "help"
  end
end
RUBY
echo "Formula/fm-cli.rb: $VERSION $SUM"
[ "${2:-}" = --formula ] && exit 0

printf "publish v%s on GitHub (zip) now? [y/N] " "$VERSION"
read -r answer
[ "$answer" = y ] || { echo "not published"; exit 0; }
gh release create "v$VERSION" "$ZIP" --target main --title "fm-cli $VERSION (macOS, Apple Silicon)" --latest \
	--notes "Install: \`curl -fsSL https://raw.githubusercontent.com/farukkavlak/fm-cli/main/install.sh | zsh\`, Homebrew or this zip: see the README."
echo "commit and push Formula/fm-cli.rb so brew sees this release"
