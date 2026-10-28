using System.Text.Json;

namespace FmCli;

/// <summary>
/// The installation, checked without a game or a save: the Steam copy, BepInEx under it, the
/// shadow bundle the launcher starts, the plugin, the command itself and the save folder. Each
/// failing step says what to do about it, so a new machine can be brought up without guessing
/// (install and release).
/// </summary>
internal static partial class Program
{
	private static int InstallDoctor(bool json)
	{
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var game = Environment.GetEnvironmentVariable("FM_HOME")
			?? Path.Combine(home, "Library/Application Support/Steam/steamapps/common/Football Manager 26");
		var bundle = Environment.GetEnvironmentVariable("FM_APP") ?? Path.Combine(home, "fm26_bep/fm.app");
		var saves = Path.Combine(home, "Library/Application Support/Sports Interactive/Football Manager 26/games");

		var checks = new List<(string Name, bool Ok, string Detail, string Fix)>
		{
			(L("Game"), Directory.Exists(game) && File.Exists(Path.Combine(game, "fm.app/Contents/MacOS/Football Manager 26")),
				game, L("Install Football Manager 26 from Steam; set FM_HOME if it is elsewhere.")),
			(L("BepInEx core"), File.Exists(Path.Combine(game, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")),
				Path.Combine(game, "BepInEx"), L("Unpack BepInEx 6 IL2CPP arm64 into the game folder (see docs/PORTING.md).")),
			(L("BepInEx launcher"), File.Exists(Path.Combine(game, "run_bepinex_arm64.sh")),
				Path.Combine(game, "run_bepinex_arm64.sh"), L("Put BepInEx's macOS launcher in the game folder.")),
			(L("Shadow bundle"), Directory.Exists(bundle) && File.Exists(Path.Combine(bundle, "Contents/Info.plist")),
				bundle, L("Create ~/fm26_bep/fm.app from the Steam copy (Info.plist patch; see README).")),
			(L("Plugin"), File.Exists(Path.Combine(game, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll")),
				Path.Combine(game, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll"), L("Build the plugin and copy it to BepInEx/plugins/FmCliProbe/.")),
			(L("fm command"), OnPath(home),
				Environment.ProcessPath ?? "fm", L("Link the built fm into ~/.local/bin or /usr/local/bin.")),
			(L("Save folder"), Directory.Exists(saves), saves,
				L("Open the game once and make a save; the folder appears by itself.")),
			(L("Language model"), NlModelInstalled, ModelDir,
				L("`fm doctor --install` downloads it (for sentences in place of commands).")),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(checks.Select(c => new { c.Name, Ok = c.Ok, c.Detail, Fix = c.Ok ? "" : c.Fix })));
			return checks.All(c => c.Ok) ? 0 : 1;
		}

		Console.WriteLine(L("INSTALL CHECK"));
		foreach (var (name, ok, detail, fix) in checks)
		{
			Console.WriteLine($"  {(ok ? "✓" : "✗")} {name,-20} {detail}");
			if (!ok) Console.WriteLine($"      → {fix}");
		}
		var missing = checks.Count(c => !c.Ok);
		Console.WriteLine(missing == 0
			? L("\nInstall complete: `fm start` opens the game.")
			: L($"\n{missing} steps missing. Follow the → lines above; details: README."));
		return missing == 0 ? 0 : 1;
	}

	private static bool OnPath(string home)
	{
		var local = Path.Combine(home, ".local/bin/fm");
		return File.Exists(local) || File.Exists("/usr/local/bin/fm") || File.Exists("/opt/homebrew/bin/fm");
	}
}
