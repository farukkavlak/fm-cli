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
///   2. A windowed launch, because a fullscreen game owns a Space that cannot be minimised.
///   3. Minimising the window, since macOS clamps an off-screen move back into view and
///      hiding the app the way ⌘H does leaves a Unity window on screen regardless.
///
/// Data keeps flowing while minimised, which is what makes this worth doing.
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
	/// Brings the game forward. FM only handles some Continue and UI events while it is the
	/// active app: a `next` or `press` sent while the terminal is front can queue and wait
	/// for a frame that never comes.
	/// </summary>
	public static void Activate() => Osascript($"tell application \"{AppName}\" to activate");

	/// <summary>
	/// Hides the game again after <see cref="Activate"/>, which gives the front back to the app the
	/// user was in: activating showed the window over whatever they were doing, command after command.
	/// </summary>
	public static void Background() => Osascript($"tell application \"System Events\" to set visible of process \"{AppName}\" to false");

	public static async Task<bool> Start(Bridge fm, bool debug, bool load = true)
	{
		// An already-running game is attached to rather than refused: the save may still need
		// loading, which is the part worth doing.
		if (await Up(fm))
		{
			Console.WriteLine(L($"FM is already open — connecting to it{(debug ? "" : L(", minimising the window"))}."));
			if (!debug) Minimise();
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

		if (!debug) DropFromDock();

		var log = Path.Combine(Path.GetTempPath(), "fm-cli-launch.log");
		Spawn(script, log, debug);
		Console.WriteLine(L($"Starting FM…{(debug ? L($"  (log: {log})") : "")}"));

		if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
		{
			Console.Error.WriteLine(L($"The game did not reach the main menu. Log: {log}"));
			return false;
		}

		if (!debug) Minimise();
		await GameFolders.Learn(fm);

		return !load || await LoadSave(fm);
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
		var windowed = debug ? "" : " -screen-fullscreen 0 -screen-width 640 -screen-height 360";

		// Detached: the game must outlive this command, and its own output goes to a file so a
		// crash before BepInEx logs anything is still readable.
		var steamApp = Path.Combine(GameDirectory, "fm.app");
		var shadowRoot = Path.GetDirectoryName(AppBundle)!;
		var shell = $"FM26_BEP=\"{shadowRoot}\" exec \"{script}\" \"{steamApp}\"{windowed} >> \"{log}\" 2>&1";
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
			// With a game loaded it asks "Quit Game And Exit?"; the caller has already settled saving.
			for (var i = 0; i < 10; i++)
			{
				await Task.Delay(500);
				if ((await fm.Get("dialog")).Contains("Quit Game")) { await fm.Get("dialog", ("press", "primary")); break; }
			}
		}
		catch (FmUnavailable) { }
		// The bridge closes as the game starts to quit; the process follows a few seconds later.
		if (await WaitFor(async () => !await Up(fm), TimeSpan.FromSeconds(20))
		    && await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(20))) return true;

		await Pkill([]);
		if (await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(15))) return true;
		await Pkill(["-9"]);
		return await WaitFor(async () => !await Alive(), TimeSpan.FromSeconds(10));
	}

	private static async Task<bool> Alive()
	{
		using var pgrep = Process.Start(new ProcessStartInfo("/usr/bin/pgrep", ["-f", AppBundle]) { UseShellExecute = false, RedirectStandardOutput = true });
		if (pgrep == null) return false;
		await pgrep.WaitForExitAsync();
		return pgrep.ExitCode == 0;
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

	private static async Task<bool> WaitFor(Func<Task<bool>> condition, TimeSpan timeout)
	{
		var deadline = DateTime.UtcNow + timeout;
		while (DateTime.UtcNow < deadline)
		{
			if (await condition()) return true;
			await Task.Delay(TimeSpan.FromSeconds(3));
		}
		return false;
	}
}
