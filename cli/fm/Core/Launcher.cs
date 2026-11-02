using System.Diagnostics;

namespace FmCli;

/// <summary>
/// Starts the game and gets it to a playable save, out of sight.
///
/// Running it truly headless does not work: with -batchmode -nographics the process and the
/// bridge come up, but the UI never boots and so the binding tree is never built — the whole
/// data path depends on the UI having bound its data. The window therefore has to exist, but it
/// does not have to be seen, and getting it out of the way takes three separate things:
///
///   1. LSUIElement in the bundle, or the Dock keeps an icon and a menu bar appears.
///   2. A windowed launch, because a fullscreen game owns a Space of its own. The game applies
///      its own saved display settings over the command line, so those are set too.
///   3. The window made transparent and click-through from inside the game (the plugin's
///      WindowHider, asked for with FMCLI_HIDDEN=1). Hiding from outside (System Events,
///      minimising) does not reach this window: measured 2 Nov 2026 with tools/window-watch,
///      the window stayed on screen while System Events said it had none.
///
/// The game goes on drawing while transparent, so data keeps flowing.
/// </summary>
internal static class Launcher
{
	private const string AppName = "Football Manager 27";

	/// <summary>
	/// The bundle the launcher starts — a shadow of the Steam one, assembled for
	/// BepInEx. Its Info.plist is ours to patch; Steam's copy is left alone.
	/// </summary>
	private static string AppBundle =>
		Environment.GetEnvironmentVariable("FM_APP")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fm27_bep/fm.app");

	private static string GameDirectory =>
		Environment.GetEnvironmentVariable("FM_HOME")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/Application Support/Steam/steamapps/common", AppName);

	/// <summary>
	/// Hides the game, minimised or not: Continue and button events go on with it hidden (measured
	/// 2 Nov 2026, FM27: ten `next` in a row with another app in front), so no command brings it
	/// forward.
	/// </summary>
	private static void Hide() => Osascript($"tell application \"System Events\" to set visible of process \"{AppName}\" to false");

	public static async Task<bool> Start(Bridge fm, bool debug, bool load = true)
	{
		// An already-running game is attached to rather than refused: the save may still need
		// loading, which is the part worth doing.
		if (await Up(fm))
		{
			Console.WriteLine(L($"FM is already open — connecting to it{(debug ? "" : L(", minimising the window"))}."));
			if (!debug) { Minimise(); Hide(); }
			await GameFolders.Learn(fm);
			return !load || await LoadSave(fm);
		}

		// FM27: BepInEx and its launcher live beside the shadow bundle (~/fm27_bep/game), not in
		// Steam's folder; the launcher is told the Steam app and where the shadow is.
		var script = Path.Combine(Path.GetDirectoryName(AppBundle)!, "game", "run_bepinex_arm64.sh");
		if (!File.Exists(script))
		{
			Console.Error.WriteLine(L($"BepInEx launcher not found: {script}"));
			Console.Error.WriteLine(L("If the game is elsewhere, point to it with FM_HOME."));
			return false;
		}

		if (!debug)
		{
			DropFromDock();
			SmallWindow();
		}

		var log = Path.Combine(Path.GetTempPath(), "fm-cli-launch.log");
		try
		{
			Spawn(script, log, debug);
			Console.WriteLine(L($"Starting FM…{(debug ? L($"  (log: {log})") : "")}"));

			if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
			{
				Console.Error.WriteLine(L($"The game did not reach the main menu. Log: {log}"));
				return false;
			}

			await GameFolders.Learn(fm);
			return !load || await LoadSave(fm);
		}
		finally
		{
			if (!debug) { Minimise(); Hide(); }
		}
	}

	/// <summary>
	/// The game's own display settings, which it applies over the command line once it is up:
	/// FM.UI.ScreenSettingsManager keeps whether it is windowed (a bool) and its chosen resolution,
	/// Unity keeps its screen mode (3 = Windowed) and size. A full-screen window cannot be minimised
	/// on macOS, so before every launch they are set to a window (transparent, see the plugin's
	/// WindowHider) big enough for the game to lay out every column of its tables. The player's own values are
	/// kept beside the shadow bundle and put back by <see cref="Stop"/>.
	/// </summary>
	private static readonly (string Key, string Type, string Value)[] HiddenDisplay =
	[
		("WindowedModeKey", "-int", "1"),
		("SelectedResolutionKey", "-string", "{\"Width\":1920,\"Height\":1080}"),
		("Screenmanager Fullscreen mode", "-int", "3"),
		("Screenmanager Resolution Width", "-int", "1920"),
		("Screenmanager Resolution Height", "-int", "1080"),
		("Screenmanager Resolution Use Native", "-int", "0"),
	];

	private static string DisplayBackup => Path.Combine(Path.GetDirectoryName(AppBundle)!, "display-settings.backup.json");

	/// <summary>The game's preferences domain: the bundle's own identifier, not a written name.</summary>
	private static string PrefsDomain => Plist("Print :CFBundleIdentifier", Path.Combine(AppBundle, "Contents/Info.plist"));

	private static void SmallWindow()
	{
		var domain = PrefsDomain;
		if (domain.Length == 0) return;
		// The player's values are kept once: a second start must not keep our own small window as theirs.
		if (!File.Exists(DisplayBackup))
		{
			var kept = HiddenDisplay.ToDictionary(k => k.Key, k => Defaults(["read", domain, k.Key]) is var (ok, value) && ok ? value : null);
			File.WriteAllText(DisplayBackup, System.Text.Json.JsonSerializer.Serialize(kept));
		}
		foreach (var (key, type, value) in HiddenDisplay) Defaults(["write", domain, key, type, value]);
	}

	/// <summary>Puts the player's own display settings back once the game is closed.</summary>
	private static void RestoreDisplay()
	{
		var domain = PrefsDomain;
		if (domain.Length == 0 || !File.Exists(DisplayBackup)) return;
		var kept = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(DisplayBackup)) ?? [];
		foreach (var (key, type, _) in HiddenDisplay)
		{
			if (!kept.TryGetValue(key, out var value)) continue;
			Defaults(value == null ? ["delete", domain, key] : ["write", domain, key, type, value]);
		}
		File.Delete(DisplayBackup);
	}

	private static (bool Ok, string Output) Defaults(string[] args)
	{
		try
		{
			using var defaults = Process.Start(new ProcessStartInfo("/usr/bin/defaults", args)
			{ RedirectStandardOutput = true, RedirectStandardError = true })!;
			var output = defaults.StandardOutput.ReadToEnd().Trim();
			defaults.WaitForExit(5000);
			return (defaults.ExitCode == 0, output);
		}
		catch (Exception) { return (false, ""); }
	}

	/// <summary>
	/// Brings a running game to a loaded save, which is what every other command needs.
	/// </summary>
	public static async Task<bool> LoadSave(Bridge fm)
	{
		if (await fm.InGame())
		{
			Console.WriteLine(L("A save is already loaded."));
			return true;
		}

		if (!await Menu(fm))
		{
			Console.WriteLine(L("Waiting for the main menu…"));
			if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
			{
				Console.Error.WriteLine(L("The game did not reach the main menu."));
				return false;
			}
		}

		Console.WriteLine(L("Loading the last save…"));
		var answer = await fm.Get("loadlast");
		if (answer.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(answer.Trim());
			return false;
		}

		if (!await WaitFor(() => fm.InGame(), TimeSpan.FromMinutes(3)))
		{
			Console.Error.WriteLine(L("The save did not load."));
			return false;
		}

		Console.WriteLine(L("Ready."));
		return true;
	}

	private static void Spawn(string script, string log, bool debug)
	{
		// A windowed launch is what makes the window minimisable later; fullscreen would put the
		// game on its own Space instead.
		var windowed = debug ? "" : " -screen-fullscreen 0 -screen-width 1920 -screen-height 1080";

		// Detached: the game must outlive this command, and its own output goes to a file so a
		// crash before BepInEx logs anything is still readable.
		var steamApp = Path.Combine(GameDirectory, "fm.app");
		var shadowRoot = Path.GetDirectoryName(AppBundle)!;
		// FMCLI_HIDDEN: the plugin makes the game's window transparent from inside (WindowHider).
		var hidden = debug ? "" : "FMCLI_HIDDEN=1 ";
		var shell = $"{hidden}FM26_BEP=\"{shadowRoot}\" exec \"{script}\" \"{steamApp}\"{windowed} >> \"{log}\" 2>&1";
		Process.Start(new ProcessStartInfo("/bin/sh", ["-c", shell])
		{
			WorkingDirectory = Path.GetDirectoryName(script)!,
			UseShellExecute = false,
			RedirectStandardOutput = false,
		});
	}

	/// <summary>
	/// Marks the bundle as an accessory app, which is the only way to keep it out of the Dock.
	/// The original plist is kept beside it, so this is undoable by hand.
	/// </summary>
	private static void DropFromDock()
	{
		var plist = Path.Combine(AppBundle, "Contents/Info.plist");
		if (!File.Exists(plist)) return;

		if (Plist($"Print :LSUIElement", plist) == "true") return;

		var backup = plist + ".backup";
		if (!File.Exists(backup)) File.Copy(plist, backup);

		Plist("Add :LSUIElement bool true", plist);
	}

	private static string Plist(string command, string plist)
	{
		try
		{
			using var buddy = Process.Start(new ProcessStartInfo("/usr/libexec/PlistBuddy",
				["-c", command, plist])
			{ RedirectStandardOutput = true, RedirectStandardError = true })!;

			var output = buddy.StandardOutput.ReadToEnd().Trim();
			buddy.WaitForExit(5000);
			return output;
		}
		catch (Exception) { return ""; }
	}

	/// <summary>
	/// Takes the window off the screen. Moving it instead does not work — macOS clamps the
	/// position back so part of it stays reachable.
	/// </summary>
	private static void Minimise()
	{
		var script = $"""
			tell application "System Events" to tell process "{AppName}"
				repeat with w in windows
					try
						set value of attribute "AXMinimized" of w to true
					end try
				end repeat
			end tell
			""";

		var error = Osascript(script);
		if (!string.IsNullOrEmpty(error))
			Console.WriteLine(L("(the window could not be minimised — allow the terminal in System Settings › Privacy › Accessibility)"));
	}

	private static string Osascript(string script)
	{
		try
		{
			using var osascript = Process.Start(new ProcessStartInfo("/usr/bin/osascript", ["-e", script])
			{ RedirectStandardError = true, RedirectStandardOutput = true })!;

			var error = osascript.StandardError.ReadToEnd().Trim();
			osascript.WaitForExit(10000);
			return error;
		}
		catch (Exception ex) { return ex.Message; }
	}

	/// <summary>
	/// Closes the game: first through its own quit, which lets it shut down cleanly; a game that
	/// was only killed could hang on its way out, holding the port. Ended by
	/// force when it does not go. True once the bridge has stopped answering.
	/// </summary>
	public static async Task<bool> Stop(Bridge fm)
	{
		try
		{
			await fm.Get("quit");
			// With a game loaded it asks first (`QuitGameDialog`); the caller has already settled saving.
			// The answer is the button that sends `QuitGame`, pressed once the dialog has drawn it.
			// Without a game the bridge simply stops answering.
			await WaitFor(async () =>
			{
				if (!await Up(fm)) return true;
				var quit = (await fm.GetJson("dialog")).EnumerateArray()
					.Where(d => d.TryGetProperty("Type", out var type) && type.GetString() == "QuitGameDialog")
					.SelectMany(d => d.GetProperty("Buttons").EnumerateArray())
					.FirstOrDefault(b => b.TryGetProperty("EventID", out var id) && id.GetString() == "QuitGame");
				if (quit.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
				await fm.Get("dialog", ("press", quit.GetProperty("Slot").GetString() ?? ""));
				return true;
			}, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(0.25));
		}
		catch (FmUnavailable) { }
		// The bridge closes as the game starts to quit; the process follows a few seconds later.
		var gone = (await WaitFor(async () => !await Up(fm), TimeSpan.FromSeconds(20))
		    && await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(20)));
		if (!gone)
		{
			await Pkill([]);
			gone = await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(15));
		}
		if (!gone)
		{
			await Pkill(["-9"]);
			gone = await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(10));
		}
		// Written once the game is gone: it saves its settings on the way out.
		if (gone) RestoreDisplay();
		return gone;
	}

	/// <summary>
	/// A game process that is still running. One stuck on its way out (state "E", not even
	/// killable) is not counted: it is gone for every purpose but the process table, and once
	/// counted, every later stop reported that the game had not closed.
	/// </summary>
	private static async Task<bool> Alive()
	{
		using var pgrep = Process.Start(new ProcessStartInfo("/usr/bin/pgrep", ["-f", AppBundle]) { UseShellExecute = false, RedirectStandardOutput = true });
		if (pgrep == null) return false;
		var pids = (await pgrep.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		await pgrep.WaitForExitAsync();
		foreach (var pid in pids)
		{
			using var ps = Process.Start(new ProcessStartInfo("/bin/ps", ["-o", "stat=", "-p", pid]) { UseShellExecute = false, RedirectStandardOutput = true });
			if (ps == null) continue;
			var state = (await ps.StandardOutput.ReadToEndAsync()).Trim();
			await ps.WaitForExitAsync();
			if (state.Length > 0 && !state.Contains('E')) return true;
		}
		return false;
	}

	private static async Task Pkill(string[] flags)
	{
		using var kill = Process.Start(new ProcessStartInfo("/usr/bin/pkill", [.. flags, "-f", AppBundle]) { UseShellExecute = false });
		if (kill != null) await kill.WaitForExitAsync();
	}

	// A hung game can hold the process with no bridge: it still has to be stoppable.
	public static async Task<bool> Running(Bridge fm) => await Up(fm) || await Alive();

	private static async Task<bool> Up(Bridge fm)
	{
		if (!await fm.Answers()) return false;
		try { return (await fm.Get("health")).Length > 0; }
		catch (FmUnavailable) { return false; }
	}

	/// <summary>The menu is built a little after the bridge answers, and loading needs it.</summary>
	private static async Task<bool> Menu(Bridge fm)
	{
		try
		{
			var health = await fm.Get("health");
			// Only the node count: the health line also carries the game and plugin versions now,
			// and their digits would overflow the count.
			var line = health.Split('\n').FirstOrDefault(l => l.StartsWith("descendants:", StringComparison.Ordinal));
			var digits = new string((line ?? "").Where(char.IsDigit).ToArray());
			return int.TryParse(digits, out var nodes) && nodes > 150;
		}
		catch (FmUnavailable) { return false; }
	}

	/// <summary>Asks <paramref name="condition"/> again every <paramref name="every"/> (3 s unless said) until it holds or time is up.</summary>
	private static async Task<bool> WaitFor(Func<Task<bool>> condition, TimeSpan timeout, TimeSpan? every = null)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await condition()) return true;
			await Task.Delay(every ?? TimeSpan.FromSeconds(3));
		}
		return false;
	}
}
