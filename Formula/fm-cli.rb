# Written by tools/release.sh: url and sha256 belong to the release's zip.
class FmCli < Formula
  desc "Play Football Manager 27 from the terminal and over MCP"
  homepage "https://github.com/farukkavlak/fm-cli"
  url "https://github.com/farukkavlak/fm-cli/releases/download/v0.9.5/fm-cli-osx-arm64.zip"
  version "0.9.5"
  sha256 "b17333c1266b36eba6e5c23ad659cc6dcbc78de11dd5f6e3d18c2a59cb0a8aa7"
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
