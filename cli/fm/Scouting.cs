using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The recruitment focuses (Recruitment › Recruitment Focuses, FINDINGS §8.109): what the scouts
/// have been set to look for, who is on it, and the players each focus has turned up. The page
/// shows one focus at a time, chosen in a list that names them only by status, so each is
/// selected in turn and read from the page's own text.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// A focus that has been removed is not gone: it stays in the list, its status as it was
	/// until the day turns, and only its button tells — it has "Restart" (FINDINGS §8.120).
	/// </summary>
	private sealed record Focus(int Index, string Name, string Status, string Assigned, int Matched, int Near, int InProgress, bool Running = true)
	{
		public string State => Status == "In Progress" && !Running ? "Durduruldu" : Status;
	}

	private static async Task<int> Scout(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		// A focus's status is read as a flag, not as the game's words (GameWords).
		await GameWords.Ask(Fm, "In Progress", "Complete");

		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 10, every: 0.5))
		{
			Console.WriteLine(L("Scout odağı yok."));
			return 0;
		}
		var count = list.Value.GetProperty("Choices").GetArrayLength();

		var words = Words(args).ToList();
		var focuses = new List<Focus>();
		for (var index = 0; index < count; index++)
			if (await FocusAt(index) is { } focus) focuses.Add(focus);

		if (words.Count == 0)
		{
			if (json) Console.WriteLine(JsonOut.Of(focuses));
			else
			{
				Table.Write(["#", L("ODAK"), L("DURUM"), L("ATANAN"), L("EŞLEŞEN"), L("YAKIN"), L("SÜREN")], focuses.Select(f => new[]
				{
					(f.Index + 1).ToString(), f.Name, f.State, f.Assigned, f.Matched.ToString(), f.Near.ToString(), f.InProgress.ToString(),
				}).ToList());
				Console.WriteLine(L("\nBir odağın bulduğu oyuncular: scout <#|ad>   (--near: yakın eşleşenler, --progress: incelenenler)"));
			}
			return 0;
		}

		Focus wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= focuses.Count) wanted = focuses[number - 1];
		else
		{
			if (Choose(focuses.Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), "Odak") is not { } name) return 64;
			wanted = focuses.First(f => f.Name == name);
		}
		if (await FocusAt(wanted.Index) is not { } shown)
		{
			Console.Error.WriteLine(L($"{wanted.Name}: odak açılmadı."));
			return 1;
		}

		// Which results: those that fit, unless asked otherwise — or unless none fits yet.
		var tab = args.Contains("--near") ? "nearly-matched-tab" : args.Contains("--progress") ? "in-progress-tab"
			: args.Contains("--matched") || shown.Matched > 0 ? "matched-tab" : shown.Near > 0 ? "nearly-matched-tab" : "in-progress-tab";
		var title = tab switch { "matched-tab" => "eşleşenler", "nearly-matched-tab" => "yakın eşleşenler", _ => "incelenenler" };
		var expected = tab switch { "matched-tab" => shown.Matched, "nearly-matched-tab" => shown.Near, _ => shown.InProgress };

		var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == tab);
		// Clicked, not queued: the tab's own handler is what swaps the table.
		if (button.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(button, "Pointer")));

		List<string[]> rows = [];
		var previous = "?";
		await Until(async () =>
		{
			rows = await FocusRows();
			var key = string.Join("|", rows.Select(r => r[0]));
			var settled = key == previous && (rows.Count > 0 || expected == 0);
			previous = key;
			return settled;
		}, seconds: 8, every: 0.6);

		// A long table only lays out the rows in view; the rest come as it is scrolled.
		if (rows.Count > 0 && rows.Count < expected)
		{
			var seen = rows.Select(r => r[0]).ToHashSet();
			foreach (var share in new[] { "0.5", "1" })
			{
				var moved = await Fm.Get("scroll", ("anchor", rows[^1][7]), ("to", share));
				if (moved.StartsWith("error", StringComparison.Ordinal)) break;
				await Task.Delay(700);
				foreach (var row in await FocusRows())
					if (seen.Add(row[0])) rows.Add(row);
				if (rows.Count >= expected) break;
			}
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Focus = shown, Showing = tab[..^4],
				Players = rows.Select(r => new { Name = r[0], Nation = r[1], Age = r[2], Position = r[3], Role = r[4], Club = r[5], League = r[6], Value = r[7], Grade = r[8], Recommendation = r[9] }),
			}));
			return 0;
		}

		Console.WriteLine($"{shown.Name} · {shown.State} · {shown.Assigned} — {title} ({expected})");
		if (rows.Count == 0) Console.WriteLine(L("Bu sekmede oyuncu yok."));
		else Table.Write([L("OYUNCU"), L("ÜLKE"), L("YAŞ"), L("MEVKİ"), L("ROL"), L("KULÜP"), L("LİG"), L("DEĞER"), L("NOT"), L("TAVSİYE")], rows);
		if (rows.Count < expected) Console.WriteLine(L($"({expected} oyuncunun {rows.Count} tanesi ekranda; liste yalnızca görünen satırları çiziyor.)"));
		return 0;
	}

	/// <summary>The list the focuses are chosen in: the radio group that lists the club's recruitment focuses.</summary>
	private static async Task<JsonElement?> FocusList()
	{
		foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray())
			if (Field(list, "Items") == "ScoutingInfo.RecruitmentFocusesList" && list.GetProperty("Choices").GetArrayLength() > 0) return list;
		return null;
	}

	/// <summary>
	/// Selects a focus and reads it: its name as the page heads it (selectedfocus.Name, with the date
	/// a finished one carries), and from the focus's own data its status, who is assigned, and how
	/// many players are matched, nearly matched and still being looked at (FINDINGS §8.169).
	/// </summary>
	private static async Task<Focus> FocusAt(int index)
	{
		if (await FocusList() is not { } list) return null;
		if (Field(list, "Selected") != index.ToString())
		{
			await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", index.ToString()));
			await Until(async () => await FocusList() is { } now && Field(now, "Selected") == index.ToString(), seconds: 4, every: 0.3);
		}

		var all = (await Fm.Pull("Human.ScoutingInfo.RecruitmentFocusesList",
			"IsFocusComplete,ScoutCount,AnalystCount,RecommendedPlayerPoolCount,NearMissPlayerPoolCount,CurrentPlayerPoolCount", list: true)).EnumerateArray().ToList();
		if (index >= all.Count) return null;
		var data = all[index];
		static int Count(JsonElement e, string name) => int.TryParse(Field(e, name), out var n) ? n : 0;
		var scouts = Count(data, "ScoutCount");
		var analysts = Count(data, "AnalystCount");
		var assigned = string.Join(" · ", new[] { scouts > 0 ? $"{scouts} gözlemci" : "", analysts > 0 ? $"{analysts} analist" : "" }.Where(x => x.Length > 0));

		Focus read = null;
		var previous = "?";
		await Until(async () =>
		{
			var name = (await FocusTexts()).FirstOrDefault(t => t.Path == "selectedfocus.Name").Text;
			if (name == null) return false;
			read = new Focus(index, name, Field(data, "IsFocusComplete") == "True" ? "Complete" : "In Progress", assigned,
				Count(data, "RecommendedPlayerPoolCount"), Count(data, "NearMissPlayerPoolCount"), Count(data, "CurrentPlayerPoolCount"),
				(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().All(b => Field(b, "Label") != "Restart Recruitment Focus"));
			var key = read.ToString();
			var settled = key == previous;
			previous = key;
			return settled;
		}, seconds: 6, every: 0.5);
		return read;
	}

	/// <summary>The focus panel, each text with its data path, from the focus's name.</summary>
	private static async Task<List<(string Text, string Path)>> FocusTexts() =>
		(await Fm.GetJson("texts", ("anchorpath", "selectedfocus.Name"), ("up", "10"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	/// <summary>
	/// The players in the results table on screen, by each text's data path: name (binding.Name),
	/// nation, age (binding), positions (binding.PositionCombinedString), best role, club (Club.Name),
	/// league (club.division.Name), value (binding.TransferValueRangeString), grade and the scouts'
	/// word for him (gradinginfo.ShortGradeString). A status flag before the name goes with it.
	/// </summary>
	private static async Task<List<string[]>> FocusRows()
	{
		var rows = new List<string[]>();
		var flags = new List<string>();
		string[] row = null;
		string last = null;
		foreach (var (text, path) in await FocusTexts())
		{
			switch (path)
			{
				case "StatusFlag.StatusTitleShort": flags.Add(text); break;
				case "binding.Name":
					row = [text + (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : ""), "", "", "", "", "", "", "", "", ""];
					flags.Clear();
					rows.Add(row);
					break;
				case "binding" when row != null && row[2] == "": row[2] = text; break;
				case "binding.PositionCombinedString" when row != null: row[3] = text; break;
				case "Club.Name" when row != null: row[5] = text; break;
				case "club.division.Name" when row != null: row[6] = text; break;
				case "binding.TransferValueRangeString" when row != null: row[7] = text; break;
				case "gradinginfo.ShortGradeString" when row != null: row[9] = text; break;
				case "" when row != null:
					// Drawn without a path: the nation after the name, the role after the positions, the grade after the value.
					if (last == "binding.Name") row[1] = text;
					else if (last == "binding.PositionCombinedString") row[4] = text;
					else if (last == "binding.TransferValueRangeString") row[8] = text;
					break;
			}
			last = path;
		}
		return rows;
	}
}
