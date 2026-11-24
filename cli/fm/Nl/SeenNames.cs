using System.Text.Json;

namespace FmCli.Nl;

/// <summary>
/// Names read with their ids on a page a sentence cannot open itself (the shortlist), kept in
/// ~/.config/fm-cli/seen-names.json for the sentences that come after. Each source is replaced
/// whole when it is read again, and held to the club and save it was read in.
/// </summary>
internal static class SeenNames
{
	private sealed record Entry(string Club, List<SaveName> Names);

	private static string File =>
		Environment.GetEnvironmentVariable("FM_SEEN_NAMES") is { Length: > 0 } other ? other
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "seen-names.json");

	private static Dictionary<string, Entry> Read()
	{
		try { return JsonSerializer.Deserialize<Dictionary<string, Entry>>(System.IO.File.ReadAllText(File)) ?? []; }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return []; }
	}

	/// <summary>What <paramref name="source"/> shows now, in the club and save <paramref name="club"/> names.</summary>
	public static void Keep(string source, string club, List<SaveName> names)
	{
		if (club.Length == 0) return;
		var all = Read();
		all[$"{source}@{club}"] = new Entry(club, names);
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File)!);
			System.IO.File.WriteAllText(File, JsonSerializer.Serialize(all));
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	/// <summary>Every name kept in this club and save.</summary>
	public static List<SaveName> At(string club) =>
		club.Length == 0 ? [] : [.. Read().Values.Where(e => e.Club == club).SelectMany(e => e.Names)];
}
