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
	internal const string AppName = "Football Manager 27";

	/// <summary>
	/// The bundle the launcher starts — a shadow of the Steam one, assembled for
	/// BepInEx. Its Info.plist is ours to patch; Steam's copy is left alone.
	/// </summary>
	internal static string AppBundle =>
		Environment.GetEnvironmentVariable("FM_APP")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fm27_bep/fm.app");

	internal static string GameDirectory =>
		Environment.GetEnvironmentVariable("FM_HOME")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/Application Support/Steam/steamapps/common", AppName);

	public static async Task<bool> Start(Bridge fm, bool debug, bool load = true)
	{
		// An already-running game is attached to rather than refused: the save may still need
		// loading, which is the part worth doing.
		if (await Up(fm))
		{
			Console.WriteLine(L("FM is already open — connecting to it."));
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

		// A game already open without our bridge (opened by hand) is not started beside: two games
		// on one save can each write it, and the game itself does not stop a second copy.
		if (await OpenByHand())
		{
			Console.Error.WriteLine(L("FM is already open (not by fm). Close it first, then: fm start"));
			return false;
		}

		// Display settings a game of ours left behind (it died without `fm stop`, or the machine went
		// down with it) are the player's again before anything else.
		await PutBackDisplay();
		if (!debug)
		{
			DropFromDock();
			SmallWindow();
		}

		var log = Path.Combine(Path.GetTempPath(), "fm-cli-launch.log");
		var spawned = DateTime.UtcNow;
		var launch = Spawn(script, log, debug);
		Console.WriteLine(L($"Starting FM…{(debug ? L($"  (log: {log})") : "")}"));

		// A game update: the launcher's first launch only generates BepInEx's copy of the game's code and
		// goes no further, so it is seen through and the game started again.
		if (GameUpdated(out var generated))
		{
			Console.WriteLine(L("The game was updated; preparing for the new version, this takes a minute or two…"));
			// The sign is BepInEx writing its copy during this launch; a copy older than the launch
			// is not one (a marker left behind would be taken as done at once).
			var ready = await WaitFor(() => Task.FromResult(launch.HasExited || File.GetLastWriteTimeUtc(generated) > spawned), TimeSpan.FromMinutes(6));
			if (launch.HasExited || !ready)
			{
				await Pkill(["-9"]);
				Console.Error.WriteLine(L($"The game could not be prepared for its new version. Log: {log}"));
				return false;
			}
			await Pkill(["-9"]);
			// The launch's shell puts the player's display back once its game is gone; it is waited for
			// (not just the game), and the hidden window set again, or the second launch reads theirs.
			// A game still up is never started beside: two copies on one save.
			if (!await WaitFor(async () => launch.HasExited && !await Alive(), TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1)))
			{
				Console.Error.WriteLine(L($"The game could not be prepared for its new version. Log: {log}"));
				return false;
			}
			if (!debug) SmallWindow();
			launch = Spawn(script, log, debug);
		}

		// The launch's shell ends with its game, so its ending before the menu is the launch having failed.
		await WaitFor(async () => launch.HasExited || await Menu(fm), TimeSpan.FromMinutes(4));
		if (launch.HasExited || !await Menu(fm))
		{
			Console.Error.WriteLine(L($"The game did not reach the main menu. Log: {log}"));
			return false;
		}

		await GameFolders.Learn(fm);
		return !load || await LoadSave(fm);
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
		// Every way a game of ours ends puts the kept copy back and drops it (`Stop`, the launch's own
		// shell, <see cref="PutBackDisplay"/>), so what is there now is the player's.
		var now = HiddenDisplay.ToDictionary(k => k.Key, k => Defaults(["read", domain, k.Key]) is var (ok, value) && ok ? value : null);
		if (!File.Exists(DisplayBackup)) File.WriteAllText(DisplayBackup, System.Text.Json.JsonSerializer.Serialize(now));
		foreach (var (key, type, value) in HiddenDisplay) Defaults(["write", domain, key, type, value]);
	}

	/// <summary>
	/// The player's display settings put back when no game of ours runs but they are still kept aside:
	/// the game died and its launch shell with it (a reboot, a killed session). Otherwise the game
	/// opened by hand from Steam would come up in our hidden window's size.
	/// </summary>
	public static async Task PutBackDisplay()
	{
		if (!File.Exists(DisplayBackup) || await Alive()) return;
		RestoreDisplay(forget: true);
	}

	/// <summary>
	/// The same put-back as shell lines, run by the launch's shell once its game has ended, however it
	/// ended (a crash, `kill -9`, memory pressure): only while the kept copy is still there (`Stop` may
	/// have done it) and no game of ours runs (an update relaunches).
	/// </summary>
	private static string PutBackLines()
	{
		var domain = PrefsDomain;
		if (domain.Length == 0 || !File.Exists(DisplayBackup)) return "";
		var kept = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(DisplayBackup)) ?? [];
		static string Q(string s) => "'" + s.Replace("'", "'\\''") + "'";
		var writes = HiddenDisplay.Where(k => kept.ContainsKey(k.Key)).Select(k => kept[k.Key] is { } value
			? $"/usr/bin/defaults write {Q(domain)} {Q(k.Key)} {k.Type} {Q(value)}"
			: $"/usr/bin/defaults delete {Q(domain)} {Q(k.Key)}");
		// The bundle as a pattern that does not match its own text: the shell's command line carries
		// it, and a plain path would make every `pgrep -f` / `pkill -f` of the game take the shell too.
		var bundle = string.Concat(AppBundle.Select(c => char.IsLetterOrDigit(c) || c == '/' ? c.ToString() : c is ']' or '\\' or '^' ? $"\\{c}" : $"[{c}]"));
		return $"if [ -f {Q(DisplayBackup)} ] && ! /usr/bin/pgrep -f {Q(bundle)} >/dev/null; then "
			+ string.Join("; ", writes) + $"; rm -f {Q(DisplayBackup)}; fi";
	}

	/// <summary>
	/// Puts the player's own display settings back once the game is closed. Not earlier: written back
	/// as soon as the bridge answered, the game read them again around the menu / save load and went
	/// full screen on a Space of its own, a black screen with the window transparent (2 Nov 2026).
	/// </summary>
	private static void RestoreDisplay(bool forget)
	{
		var domain = PrefsDomain;
		if (domain.Length == 0 || !File.Exists(DisplayBackup)) return;
		var kept = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(DisplayBackup)) ?? [];
		foreach (var (key, type, _) in HiddenDisplay)
		{
			if (!kept.TryGetValue(key, out var value)) continue;
			Defaults(value == null ? ["delete", domain, key] : ["write", domain, key, type, value]);
		}
		if (forget) File.Delete(DisplayBackup);
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

	private static string UpdatedFile => Path.Combine(GameDirectory, "fm.app", "Contents/Frameworks/GameAssembly.dylib");

	/// <summary>The game's code is newer than BepInEx's copy of it: the launcher's own test, on its own marker.</summary>
	private static bool GameUpdated(out string generated)
	{
		var interop = Path.Combine(Path.GetDirectoryName(AppBundle)!, "game", "BepInEx", "interop");
		// Written last; the launcher tests FM.UI.dll, written just before it.
		generated = Path.Combine(interop, "assembly-hash.txt");
		var marker = Path.Combine(interop, "FM.UI.dll");
		return File.Exists(UpdatedFile) && (!File.Exists(marker) || File.GetLastWriteTimeUtc(UpdatedFile) > File.GetLastWriteTimeUtc(marker));
	}

	private static Process Spawn(string script, string log, bool debug)
	{
		// A windowed launch is what makes the window minimisable later; fullscreen would put the
		// game on its own Space instead.
		var windowed = debug ? "" : " -screen-fullscreen 0 -screen-width 1920 -screen-height 1080";

		// Detached: the game must outlive this command, and its own output goes to a file so a
		// crash before BepInEx logs anything is still readable.
		var steamApp = Path.Combine(GameDirectory, "fm.app");
		var shadowRoot = Path.GetDirectoryName(AppBundle)!;
		// FMCLI_HIDDEN: the plugin makes the game's window transparent from inside (WindowHider).
		// The plugin closes a copy opened by hand while it runs and says why in this note (SecondCopyGuard).
		var notice = L("fm is playing FM in the background, so this copy was closed. To play by hand: fm stop").Replace("'", "");
		var hidden = debug ? "" : $"FMCLI_HIDDEN=1 FMCLI_SECOND_COPY_NOTICE='{notice}' ";
		// Not exec'd: the shell outlives the game to put the player's display back, whatever ended it.
		var after = debug ? "" : PutBackLines();
		// The shell's own output goes to the log too: it outlives this command, and holding a pipe of
		// the caller's open would keep `fm start | …` from ever ending.
		var shell = $"exec >> \"{log}\" 2>&1; {hidden}FM26_BEP=\"{shadowRoot}\" \"{script}\" \"{steamApp}\"{windowed}"
			+ (after.Length > 0 ? $"; {after}" : "");
		return Process.Start(new ProcessStartInfo("/bin/sh", ["-c", shell])
		{
			WorkingDirectory = Path.GetDirectoryName(script)!,
			UseShellExecute = false,
			RedirectStandardOutput = false,
		})!;
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
	/// Closes the game: first through its own quit, which lets it shut down cleanly; a game that
	/// was only killed could hang on its way out, holding the port. Ended by
	/// force when it does not go. True once the bridge has stopped answering.
	/// </summary>
	public static async Task<bool> Stop(Bridge fm)
	{
		// The game's own fatal-error window holds its main thread: nothing it is asked is ever
		// answered, and only ending the process closes it.
		if (await FatalErrorUp())
		{
			await Pkill(["-9"]);
			var ended = await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(10));
			if (ended) RestoreDisplay(forget: true);
			return ended;
		}
		// A frozen game takes a request and never answers it: it is not asked, it is ended below.
		if (await fm.Answers())
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
		catch (Exception e) when (e is FmUnavailable or FmBusy) { }
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
		if (gone) RestoreDisplay(forget: true);
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

	/// <summary>
	/// Whether the game sits in its own fatal-error window ("a serious error occurred and the
	/// application will now close", put up by the game's logic): its main thread is then inside
	/// AppKit's modal alert. Told by that function's name in a one-second sample of the process,
	/// the same in every language; never by the window's text.
	/// </summary>
	public static async Task<bool> FatalErrorUp()
	{
		foreach (var pid in await LivePids())
		{
			try
			{
				using var sample = Process.Start(new ProcessStartInfo("/usr/bin/sample", [pid, "1", "-mayDie", "-file", "/dev/stdout"])
				{ UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true });
				if (sample == null) continue;
				var report = await sample.StandardOutput.ReadToEndAsync();
				await sample.WaitForExitAsync();
				if (MainThreadInAlert(report)) return true;
			}
			catch (Exception) { }
		}
		return false;
	}

	/// <summary>The main thread's call tree of a `sample` report reaches `-[NSAlert runModal]`.</summary>
	private static bool MainThreadInAlert(string report)
	{
		var lines = report.Split('\n');
		var main = Array.FindIndex(lines, l => l.Contains("Thread_", StringComparison.Ordinal) && l.Contains("com.apple.main-thread", StringComparison.Ordinal));
		if (main < 0) return false;
		for (var i = main + 1; i < lines.Length; i++)
		{
			// The next thread's tree starts at the same depth as the main thread's own line.
			if (lines[i].Contains("Thread_", StringComparison.Ordinal)) break;
			if (lines[i].Contains("[NSAlert runModal]", StringComparison.Ordinal)) return true;
		}
		return false;
	}

	private static async Task<List<string>> LivePids()
	{
		using var pgrep = Process.Start(new ProcessStartInfo("/usr/bin/pgrep", ["-f", AppBundle]) { UseShellExecute = false, RedirectStandardOutput = true });
		if (pgrep == null) return [];
		var pids = (await pgrep.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
		await pgrep.WaitForExitAsync();
		return pids;
	}

	private static async Task Pkill(string[] flags)
	{
		using var kill = Process.Start(new ProcessStartInfo("/usr/bin/pkill", [.. flags, "-f", AppBundle]) { UseShellExecute = false });
		if (kill != null) await kill.WaitForExitAsync();
	}

	/// <summary>
	/// A running game not started by us: a process of the game's executable (named by the bundle's
	/// own CFBundleExecutable) that is neither ours nor stuck on its way out.
	/// </summary>
	private static async Task<bool> OpenByHand()
	{
		var executable = Plist("Print :CFBundleExecutable", Path.Combine(AppBundle, "Contents/Info.plist"));
		if (executable.Length == 0) return false;
		using var ps = Process.Start(new ProcessStartInfo("/bin/ps", ["-axo", "stat=,comm="]) { UseShellExecute = false, RedirectStandardOutput = true });
		if (ps == null) return false;
		var lines = (await ps.StandardOutput.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
		await ps.WaitForExitAsync();
		return lines.Select(l => l.Trim()).Any(l => l.Split(' ', 2) is [var state, var path]
			&& !state.Contains('E') && Path.GetFileName(path.Trim()) == executable && !path.Contains(AppBundle, StringComparison.Ordinal));
	}

	// A hung game can hold the process with no bridge: it still has to be stoppable.
	public static async Task<bool> Running(Bridge fm) => await Up(fm) || await Alive();

	private static async Task<bool> Up(Bridge fm)
	{
		if (!await fm.Answers()) return false;
		try { return (await fm.Get("health")).Length > 0; }
		catch (Exception e) when (e is FmUnavailable or FmBusy) { return false; }
	}

	/// <summary>
	/// The menu is built a little after the bridge answers, and loading needs it. Asked briefly first:
	/// until the new plugin writes its port, the port file can still name one a dead game holds, which
	/// takes the connection and never answers (3 Nov 2026: start sat there for good).
	/// </summary>
	private static async Task<bool> Menu(Bridge fm)
	{
		if (!await fm.Answers()) return false;
		try
		{
			var health = await fm.Get("health");
			// Only the node count: the health line also carries the game and plugin versions now,
			// and their digits would overflow the count.
			var line = health.Split('\n').FirstOrDefault(l => l.StartsWith("descendants:", StringComparison.Ordinal));
			var digits = new string((line ?? "").Where(char.IsDigit).ToArray());
			return int.TryParse(digits, out var nodes) && nodes > 150;
		}
		catch (Exception e) when (e is FmUnavailable or FmBusy) { return false; }
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
