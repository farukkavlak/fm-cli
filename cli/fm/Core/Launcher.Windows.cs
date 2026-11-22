using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Windows (T49, T54): BepInEx sits in the game's own folder (its doorstop, winhttp.dll, beside the game's
/// executable), so the game is started directly and BepInEx comes up with it. The game is found in a Steam
/// library (Steam's path from the registry, its libraries from libraryfolders.vdf), its executable by Unity's
/// own layout (&lt;name&gt;.exe beside &lt;name&gt;_Data), Steam's app id from the library's app manifest (its
/// installdir is the game's folder) and handed over as SteamAppId, so nothing is written into Steam's folder.
/// The window is hidden from inside the game (the plugin's WindowHider, FMCLI_HIDDEN=1).
///
/// Written ahead of a Windows machine: untested until T81.
/// </summary>
internal static partial class Launcher
{
	[SupportedOSPlatform("windows")]
	private static string WinGameDirectory()
	{
		foreach (var library in WinSteamLibraries())
		{
			var dir = Path.Combine(library, "steamapps", "common", AppName);
			if (WinExecutable(dir) != null) return dir;
		}
		return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam", "steamapps", "common", AppName);
	}

	/// <summary>Steam's own folder (the registry's SteamPath) and every library its libraryfolders.vdf lists.</summary>
	[SupportedOSPlatform("windows")]
	private static List<string> WinSteamLibraries()
	{
		var found = new List<string>();
		try
		{
			using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
			if (key?.GetValue("SteamPath") is string steam && steam.Length > 0)
			{
				steam = steam.Replace('/', '\\');
				found.Add(steam);
				var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
				if (File.Exists(vdf))
					foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
						found.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
			}
		}
		catch (Exception) { }
		return found.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	/// <summary>The game's executable by Unity's layout: the .exe whose name has a &lt;name&gt;_Data folder beside it.</summary>
	internal static string? WinExecutable(string dir)
	{
		if (!Directory.Exists(dir)) return null;
		return Directory.EnumerateFiles(dir, "*.exe")
			.FirstOrDefault(exe => Directory.Exists(Path.Combine(dir, Path.GetFileNameWithoutExtension(exe) + "_Data")));
	}

	/// <summary>Steam's id for the game: the app manifest in its library whose installdir is the game's folder.</summary>
	private static string? WinSteamAppId(string dir)
	{
		try
		{
			var steamapps = Path.GetDirectoryName(Path.GetDirectoryName(dir));
			if (steamapps == null) return null;
			var folder = Path.GetFileName(dir);
			foreach (var manifest in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
			{
				var text = File.ReadAllText(manifest);
				if (Regex.Match(text, "\"installdir\"\\s+\"([^\"]+)\"") is { Success: true } install
				    && string.Equals(install.Groups[1].Value, folder, StringComparison.OrdinalIgnoreCase)
				    && Regex.Match(text, "\"appid\"\\s+\"(\\d+)\"") is { Success: true } id)
					return id.Groups[1].Value;
			}
		}
		catch (Exception) { }
		return null;
	}

	/// <summary>BepInEx is in the game's folder: its core and the doorstop that loads it.</summary>
	private static bool WinBepInExInstalled(string dir) =>
		Directory.Exists(Path.Combine(dir, "BepInEx", "core")) && File.Exists(Path.Combine(dir, "winhttp.dll"));

	private static string WinProcessName => WinExecutable(GameDirectory) is { } exe ? Path.GetFileNameWithoutExtension(exe) : "fm";

	private static List<Process> WinGames() => Process.GetProcessesByName(WinProcessName).Where(p =>
	{
		try { return !p.HasExited; } catch (Exception) { return false; }
	}).ToList();

	private static bool WinSteamRunning() => Process.GetProcessesByName("steam").Length > 0;

	private static void WinKill(bool force)
	{
		foreach (var game in WinGames())
		{
			try
			{
				// Without force, its main window is asked to close first, as its own close button does.
				if (!force && game.CloseMainWindow()) continue;
				game.Kill(entireProcessTree: true);
			}
			catch (Exception) { }
		}
	}

	private static async Task<bool> WinStart(Bridge fm, bool debug, bool load)
	{
		var dir = GameDirectory;
		var exe = WinExecutable(dir);
		if (exe == null)
		{
			Console.Error.WriteLine(L($"The game was not found in {dir}. If it is elsewhere, point to it with FM_HOME."));
			return false;
		}
		if (!WinBepInExInstalled(dir))
		{
			Console.Error.WriteLine(L($"BepInEx is not installed in {dir}. To install it: fm doctor --install"));
			return false;
		}
		// A game already open without our bridge (opened by hand) is not started beside: two games on one save.
		if (WinGames().Count > 0)
		{
			Console.Error.WriteLine(L("FM is already open (not by fm). Close it first, then: fm start"));
			return false;
		}
		if (!WinSteamRunning())
		{
			Console.Error.WriteLine(L("Steam is not running. Open Steam and sign in, then: fm start"));
			return false;
		}

		var start = new ProcessStartInfo(exe)
		{
			WorkingDirectory = dir,
			UseShellExecute = false,
		};
		// A windowed launch: the plugin hides that window from inside the game.
		if (!debug)
			foreach (var arg in new[] { "-screen-fullscreen", "0", "-screen-width", "1920", "-screen-height", "1080" }) start.ArgumentList.Add(arg);
		if (WinSteamAppId(dir) is { } appId)
		{
			start.Environment["SteamAppId"] = appId;
			start.Environment["SteamGameId"] = appId;
		}
		if (!debug)
		{
			start.Environment["FMCLI_HIDDEN"] = "1";
			start.Environment["FMCLI_SECOND_COPY_NOTICE"] = L("fm is playing FM in the background, so this copy was closed. To play by hand: fm stop");
		}
		var firstStart = !Directory.Exists(Path.Combine(dir, "BepInEx", "interop")) || !Directory.EnumerateFiles(Path.Combine(dir, "BepInEx", "interop"), "*.dll").Any();
		Process launch;
		try { launch = Process.Start(start)!; }
		catch (Exception e)
		{
			Console.Error.WriteLine(L($"The game could not be started: {e.Message}"));
			return false;
		}
		var log = Path.Combine(dir, "BepInEx", "LogOutput.log");
		Console.WriteLine(firstStart
			? L("First start: BepInEx reads the game once, this takes a minute or two…")
			: L($"Starting FM…{(debug ? L($"  (log: {log})") : "")}"));

		// BepInEx reads the game in the same launch on Windows: the menu is waited for, longer on a first start.
		await WaitFor(async () => launch.HasExited || await Menu(fm), TimeSpan.FromMinutes(firstStart ? 8 : 4));
		if (launch.HasExited || !await Menu(fm))
		{
			Console.Error.WriteLine(L($"The game did not reach the main menu. Log: {log}"));
			return false;
		}
		await GameFolders.Learn(fm);
		return !load || await LoadSave(fm);
	}
}
