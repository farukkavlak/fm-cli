# Written by tools/release.sh: url and sha256 belong to the release's zip.
class FmCli < Formula
  desc "Play Football Manager 27 from the terminal and over MCP"
  homepage "https://github.com/farukkavlak/fm-cli"
  url "https://github.com/farukkavlak/fm-cli/releases/download/v0.9.2/fm-cli-osx-arm64.zip"
  version "0.9.2"
  sha256 "c7739062259f1c847f95db9e22fba5fe133101d45b8bf61b4adf5fc1b26c6917"
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
