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
		var game = Launcher.GameDirectory;
		var bundle = Launcher.AppBundle;
		// BepInEx and its launcher live beside the shadow bundle, not in Steam's folder: an update there leaves them alone.
		var beside = Path.Combine(Path.GetDirectoryName(bundle)!, "game");
		var saves = Directory.Exists(GameFolders.Saves) ? GameFolders.Saves : Path.Combine(home, $"Library/Application Support/Sports Interactive/{Launcher.AppName}/games");

		// Windows: BepInEx and its doorstop sit in the game's own folder; there is no shadow bundle or launcher script.
		var windows = OperatingSystem.IsWindows();
		if (windows) beside = game;
		var checks = windows ? new List<(string Name, bool Ok, string Detail, string Fix)>
		{
			(L("Game"), Launcher.WinExecutable(game) != null, game, Lf("Install {0} from Steam; set FM_HOME if it is elsewhere.", Launcher.AppName)),
			(L("BepInEx core"), File.Exists(Path.Combine(game, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")),
				Path.Combine(game, "BepInEx"), L("`fm doctor --install` puts the BepInEx the package carries in the game's folder.")),
			(L("BepInEx doorstop"), File.Exists(Path.Combine(game, "winhttp.dll")), Path.Combine(game, "winhttp.dll"),
				L("`fm doctor --install` puts it beside the game's executable.")),
			(L("Plugin"), File.Exists(Path.Combine(game, "BepInEx", "plugins", "FmCliProbe", "FmCliProbe.dll")),
				Path.Combine(game, "BepInEx", "plugins", "FmCliProbe", "FmCliProbe.dll"), L("`fm doctor --install` puts it in place.")),
			(L("fm command"), OnPath(home), Environment.ProcessPath ?? "fm", L("Add the folder fm is in to PATH.")),
			(L("Save folder"), Directory.Exists(GameFolders.Saves), GameFolders.Saves, L("Open the game once and make a save; the folder appears by itself.")),
			(L("Language model"), NlModelInstalled, ModelDir, L("`fm doctor --install` downloads it (for sentences in place of commands).")),
		} : new List<(string Name, bool Ok, string Detail, string Fix)>
		{
			(L("Game"), Directory.Exists(game) && File.Exists(Path.Combine(game, $"fm.app/Contents/MacOS/{Launcher.AppName}")),
				game, Lf("Install {0} from Steam; set FM_HOME if it is elsewhere.", Launcher.AppName)),
			(L("BepInEx core"), File.Exists(Path.Combine(beside, "BepInEx/core/BepInEx.Unity.IL2CPP.dll")),
				Path.Combine(beside, "BepInEx"), L("Unpack BepInEx 6 IL2CPP arm64 beside the shadow bundle (see docs/PORTING.md).")),
			(L("BepInEx launcher"), File.Exists(Path.Combine(beside, "run_bepinex_arm64.sh")),
				Path.Combine(beside, "run_bepinex_arm64.sh"), L("Copy tools/fm27-bep/run_bepinex_arm64.sh beside BepInEx.")),
			(L("Shadow bundle"), Directory.Exists(bundle) && File.Exists(Path.Combine(bundle, "Contents/Info.plist")),
				bundle, L("The launcher builds it on the first `fm start`.")),
			(L("Plugin"), File.Exists(Path.Combine(beside, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll")),
				Path.Combine(beside, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll"), L("Build the plugin and copy it to BepInEx/plugins/FmCliProbe/.")),
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

	/// <summary>
	/// A package unpacked beside the fm binary (Homebrew keeps it in the formula's libexec, where it cannot
	/// write into the home folder itself): BepInEx and the plugin it carries are put beside the shadow bundle,
	/// and the plugin again when it changed (after an upgrade). Not while the game runs: it holds the plugin.
	/// </summary>
	private static async Task<bool> PlaceBundledBepInEx()
	{
		var exe = Environment.ProcessPath;
		if (exe == null) return true;
		if (File.ResolveLinkTarget(exe, returnFinalTarget: true) is { } target) exe = target.FullName;
		var package = Path.GetDirectoryName(exe)!;
		var bundledGame = Path.Combine(package, "game");
		var bundledPlugin = Path.Combine(package, "FmCliProbe.dll");
		if (!Directory.Exists(Path.Combine(bundledGame, "BepInEx/core")) || !File.Exists(bundledPlugin)) return true;

		// Windows: into the game's own folder, where its doorstop loads BepInEx.
		var beside = OperatingSystem.IsWindows() ? Launcher.GameDirectory : Path.Combine(Path.GetDirectoryName(Launcher.AppBundle)!, "game");
		var plugin = Path.Combine(beside, "BepInEx/plugins/FmCliProbe/FmCliProbe.dll");
		var needCore = !File.Exists(Path.Combine(beside, "BepInEx/core/BepInEx.Unity.IL2CPP.dll"));
		var needPlugin = !File.Exists(plugin) || !File.ReadAllBytes(plugin).AsSpan().SequenceEqual(File.ReadAllBytes(bundledPlugin));
		if (!needCore && !needPlugin) return true;
		if (await Launcher.Alive())
		{
			Console.Error.WriteLine(L("The game is running: close it (fm stop) and run `fm doctor --install` again to put the new BepInEx and plugin in place."));
			return false;
		}
		Console.WriteLine(L($"Putting BepInEx and the plugin in place: {beside}"));
		if (needCore) CopyTree(bundledGame, beside);
		Directory.CreateDirectory(Path.GetDirectoryName(plugin)!);
		File.Copy(bundledPlugin, plugin, overwrite: true);
		return true;
	}

	private static void CopyTree(string from, string to)
	{
		foreach (var dir in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
			Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, dir)));
		Directory.CreateDirectory(to);
		foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
		{
			var dest = Path.Combine(to, Path.GetRelativePath(from, file));
			File.Copy(file, dest, overwrite: true);
			// Copying drops the executable bit; the launcher script and dotnet's host need it.
			if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(dest, File.GetUnixFileMode(file));
		}
	}

	private static bool OnPath(string home)
	{
		// Windows: any folder on PATH holding fm.exe.
		if (OperatingSystem.IsWindows())
			return (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
				.Any(d => File.Exists(Path.Combine(d.Trim(), "fm.exe")));
		var local = Path.Combine(home, ".local/bin/fm");
		return File.Exists(local) || File.Exists("/usr/local/bin/fm") || File.Exists("/opt/homebrew/bin/fm");
	}
}
