using System.Text.Json;

namespace FmCli;

/// <summary>
/// Where the game keeps the user's files: saves under "games", tactics under "tactics". The game
/// says where (the plugin's /folders, Unity's persistent data path), which differs by platform and
/// store; the answer is kept in ~/.config/fm-cli/user-folder so the commands that run with the game
/// closed (saves) know it too. Before the game has ever answered, the platform's usual place;
/// FM_USER_DIR overrides both.
/// </summary>
internal static class GameFolders
{
	private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
	private static string Cache => Path.Combine(Home, ".config", "fm-cli", "user-folder");

	public static string User
	{
		get
		{
			if (Environment.GetEnvironmentVariable("FM_USER_DIR") is { Length: > 0 } set) return set;
			try
			{
				if (File.Exists(Cache) && File.ReadAllText(Cache).Trim() is { Length: > 0 } kept && Directory.Exists(kept)) return kept;
			}
			catch (IOException) { }
			// Until the game has said (the folder holding its games\config.xml): on macOS Unity's data folder,
			// ~/Library/Application Support/<company>/<product>; on Windows FM's own, Documents\<company>\<product>
			// (Unity's AppData\LocalLow holds no saves there).
			return OperatingSystem.IsWindows()
				? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sports Interactive", "Football Manager 27")
				: Path.Combine(Home, "Library", "Application Support", "Sports Interactive", "Football Manager 27");
		}
	}

	public static string Saves => Path.Combine(User, "games");
	public static string Tactics => Path.Combine(User, "tactics");

	/// <summary>Asks the running game where its folder is and keeps the answer.</summary>
	public static async Task Learn(Bridge fm)
	{
		try
		{
			var folders = await fm.GetJson("folders");
			if (folders.ValueKind != JsonValueKind.Object || !folders.TryGetProperty("User", out var user)) return;
			var path = user.GetString();
			if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
			Directory.CreateDirectory(Path.GetDirectoryName(Cache)!);
			if (!File.Exists(Cache) || File.ReadAllText(Cache).Trim() != path) File.WriteAllText(Cache, path + "\n");
		}
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy or IOException) { }
	}
}
