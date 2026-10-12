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
	private const string AppName = "Football Manager 26";

	/// <summary>
	/// The bundle the launcher actually execs — a shadow of the Steam one, assembled for
	/// BepInEx. Its Info.plist is ours to patch; Steam's copy is left alone.
	/// </summary>
	private static string AppBundle =>
		Environment.GetEnvironmentVariable("FM_APP")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "fm26_bep/fm.app");

	private static string GameDirectory =>
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
			Console.WriteLine($"FM zaten açık — ona bağlanıyorum{(debug ? "" : ", pencereyi küçültüyorum")}.");
			if (!debug) Minimise();
			return !load || await LoadSave(fm);
		}

		var script = Path.Combine(GameDirectory, "run_bepinex_arm64.sh");
		if (!File.Exists(script))
		{
			Console.Error.WriteLine($"BepInEx başlatıcısı bulunamadı: {script}");
			Console.Error.WriteLine("Oyun başka yerdeyse FM_HOME ile göster.");
			return false;
		}

		if (!debug) DropFromDock();

		var log = Path.Combine(Path.GetTempPath(), "fm-cli-launch.log");
		Spawn(script, log, debug);
		Console.WriteLine($"FM başlatılıyor…{(debug ? $"  (log: {log})" : "")}");

		if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
		{
			Console.Error.WriteLine($"Oyun ana menüye gelmedi. Log: {log}");
			return false;
		}

		if (!debug) Minimise();

		return !load || await LoadSave(fm);
	}

	/// <summary>
	/// Brings a running game to a loaded save, which is what every other command needs.
	/// </summary>
	public static async Task<bool> LoadSave(Bridge fm)
	{
		if (await fm.InGame())
		{
			Console.WriteLine("Kayıt zaten yüklü.");
			return true;
		}

		if (!await Menu(fm))
		{
			Console.WriteLine("Ana menü bekleniyor…");
			if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
			{
				Console.Error.WriteLine("Oyun ana menüye gelmedi.");
				return false;
			}
		}

		Console.WriteLine("Son kayıt yükleniyor…");
		var answer = await fm.Get("continue");
		if (answer.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(answer.Trim());
			return false;
		}

		if (!await WaitFor(() => fm.InGame(), TimeSpan.FromMinutes(3)))
		{
			Console.Error.WriteLine("Kayıt yüklenmedi.");
			return false;
		}

		Console.WriteLine("Hazır.");
		return true;
	}

	private static void Spawn(string script, string log, bool debug)
	{
		// A windowed launch is what makes the window minimisable later; fullscreen would put the
		// game on its own Space instead.
		var windowed = debug ? "" : " -screen-fullscreen 0 -screen-width 640 -screen-height 360";

		// Detached: the game must outlive this command, and its own output goes to a file so a
		// crash before BepInEx logs anything is still readable.
		var shell = $"exec \"{script}\"{windowed} >> \"{log}\" 2>&1";
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
			Console.WriteLine("(pencere küçültülemedi — Sistem Ayarları › Gizlilik › Erişilebilirlik'te terminale izin ver)");
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
	/// was only killed could hang on its way out, holding the port (FINDINGS §8.143). Ended by
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

	public static Task<bool> Running(Bridge fm) => Up(fm);

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
			var digits = new string(health.Where(char.IsDigit).ToArray());
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
