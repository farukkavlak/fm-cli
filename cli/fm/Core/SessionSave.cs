using System.Text.Json;

namespace FmCli;

/// <summary>
/// The prompt's reminder when the game has not been saved for a week of game days; it never
/// saves. The game says when it last saved only as shown text in its own format, and that text
/// is not even kept up to date (`game.LastSaveDate` stayed on the old save after a new one,
/// 23 Nov 2026). What changes with every save, by hand or automatic, is the newest file in the
/// save folder: when it changes, or a save is loaded, the game's own date then (`state`) is
/// noted. A save made before any change was seen has no known date, and then nothing is said.
/// </summary>
internal static partial class Program
{
	private const int UnsavedDays = 7;

	private sealed record SaveSeen(string Team, string Mark, string? Date);

	private static string SaveSeenFile => CacheFile("last-save.json");

	private static SaveSeen? saveSeen;

	/// <summary>Notes a save the game made since the last look; <paramref name="loaded"/>: a save was just loaded, which is one of that day.</summary>
	internal static async Task NoteSave(bool loaded)
	{
		var look = newsBefore;
		if (look == null) return;
		string mark;
		try
		{
			var newest = new DirectoryInfo(SavesFolder).GetFiles("*.fm").MaxBy(f => f.LastWriteTimeUtc);
			if (newest == null) return;
			mark = $"{newest.Name}|{newest.LastWriteTimeUtc.Ticks}";
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return; }

		saveSeen ??= Kept();
		if (loaded) saveSeen = new SaveSeen(look.Team, mark, look.Date);
		else if (saveSeen == null || saveSeen.Team != look.Team) saveSeen = new SaveSeen(look.Team, mark, null);
		else if (saveSeen.Mark != mark) saveSeen = new SaveSeen(look.Team, mark, look.Date);
		else return;
		try { File.WriteAllText(SaveSeenFile, JsonSerializer.Serialize(saveSeen)); } catch { }

		static SaveSeen? Kept()
		{
			try { return File.Exists(SaveSeenFile) ? JsonSerializer.Deserialize<SaveSeen>(File.ReadAllText(SaveSeenFile)) : null; }
			catch { return null; }
		}
	}

	/// <summary>Game days since the last save seen, when known and a week or more; else null.</summary>
	private static int? UnsavedFor(NewsLook look)
	{
		if (saveSeen is not { Date: { } saved } seen || seen.Team != look.Team) return null;
		if (!DateTime.TryParseExact(saved, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var then)
		    || !DateTime.TryParseExact(look.Date, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var now))
			return null;
		var days = (now - then).Days;
		return days >= UnsavedDays ? days : null;
	}
}
