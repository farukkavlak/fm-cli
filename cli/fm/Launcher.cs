using System.Diagnostics;

namespace FmCli;

/// <summary>
/// Starts the game and gets it to a playable save.
///
/// Running it truly headless does not work: with -batchmode -nographics the process and the
/// bridge come up, but the UI never boots and so the binding tree is never built — and the whole
/// data path depends on the UI having bound its data. The window therefore has to exist. It can
/// be hidden, which is what happens unless --debug asks to watch it.
/// </summary>
internal static class Launcher
{
	private const string AppName = "Football Manager 26";

	private static string GameDirectory =>
		Environment.GetEnvironmentVariable("FM_HOME")
		?? Path.Combine(
			Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
			"Library/Application Support/Steam/steamapps/common", AppName);

	public static async Task<bool> Start(Bridge fm, bool debug)
	{
		// An already-running game is attached to rather than refused: the save may still need
		// loading, which is the part worth doing.
		if (await Up(fm))
		{
			Console.WriteLine($"FM zaten açık — ona bağlanıyorum{(debug ? "" : ", pencereyi gizliyorum")}.");
			if (!debug) Hide();
			return await LoadSave(fm);
		}

		var script = Path.Combine(GameDirectory, "run_bepinex_arm64.sh");
		if (!File.Exists(script))
		{
			Console.Error.WriteLine($"BepInEx başlatıcısı bulunamadı: {script}");
			Console.Error.WriteLine("Oyun başka yerdeyse FM_HOME ile göster.");
			return false;
		}

		var log = Path.Combine(Path.GetTempPath(), "fm-cli-launch.log");
		Spawn(script, log);
		Console.WriteLine($"FM başlatılıyor…{(debug ? $"  (log: {log})" : "")}");

		if (!await WaitFor(() => Menu(fm), TimeSpan.FromMinutes(4)))
		{
			Console.Error.WriteLine($"Oyun ana menüye gelmedi. Log: {log}");
			return false;
		}

		if (!debug) Hide();

		return await LoadSave(fm);
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

	private static void Spawn(string script, string log)
	{
		// Detached: the game must outlive this command, and its own output goes to a file so a
		// crash before BepInEx logs anything is still readable.
		var shell = $"exec \"{script}\" >> \"{log}\" 2>&1";
		Process.Start(new ProcessStartInfo("/bin/sh", ["-c", shell])
		{
			WorkingDirectory = Path.GetDirectoryName(script)!,
			UseShellExecute = false,
			RedirectStandardOutput = false,
		});
	}

	/// <summary>Hide the window the way ⌘H does, leaving the game running behind everything.</summary>
	private static void Hide()
	{
		try
		{
			Process.Start(new ProcessStartInfo("/usr/bin/osascript",
			[
				"-e",
				$"tell application \"System Events\" to set visible of process \"{AppName}\" to false",
			])
			{ RedirectStandardError = true })?.WaitForExit(5000);
		}
		catch (Exception)
		{
			// Hiding needs accessibility permission; the game still runs if it is refused.
			Console.WriteLine("(pencere gizlenemedi — Sistem Ayarları › Erişilebilirlik izni gerekebilir)");
		}
	}

	private static async Task<bool> Up(Bridge fm)
	{
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
