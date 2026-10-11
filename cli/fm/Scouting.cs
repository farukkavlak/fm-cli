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
	/// A focus that has been removed is not gone: it stays in the list, its status as it was,
	/// and only its button tells — "Restart" where a running one has "Remove" (FINDINGS §8.120).
	/// </summary>
	private sealed record Focus(int Index, string Name, string Status, string Assigned, int Matched, int Near, int InProgress, bool Running = true)
	{
		public string State => Status == "In Progress" && !Running ? "Durduruldu" : Status;
	}

	private static async Task<int> Scout(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Recruitment Focuses", "Recruitment");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 10, every: 0.5))
		{
			Console.WriteLine("Scout odağı yok.");
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
				Table.Write(["#", "ODAK", "DURUM", "ATANAN", "EŞLEŞEN", "YAKIN", "SÜREN"], focuses.Select(f => new[]
				{
					(f.Index + 1).ToString(), f.Name, f.State, f.Assigned, f.Matched.ToString(), f.Near.ToString(), f.InProgress.ToString(),
				}).ToList());
				Console.WriteLine("\nBir odağın bulduğu oyuncular: scout <#|ad>   (--near: yakın eşleşenler, --progress: incelenenler)");
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
			Console.Error.WriteLine($"{wanted.Name}: odak açılmadı.");
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
		if (rows.Count == 0) Console.WriteLine("Bu sekmede oyuncu yok.");
		else Table.Write(["OYUNCU", "ÜLKE", "YAŞ", "MEVKİ", "ROL", "KULÜP", "LİG", "DEĞER", "NOT", "TAVSİYE"], rows);
		if (rows.Count < expected) Console.WriteLine($"({expected} oyuncunun {rows.Count} tanesi ekranda; liste yalnızca görünen satırları çiziyor.)");
		return 0;
	}

	/// <summary>The list the focuses are chosen in: the one whose choices are all statuses.</summary>
	private static async Task<JsonElement?> FocusList()
	{
		foreach (var list in (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray())
		{
			var choices = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
			if (choices.Count > 0 && choices.All(c => c is "In Progress" or "Complete" or "Not Started" or "Paused" or "On Hold")) return list;
		}
		return null;
	}

	/// <summary>
	/// Selects a focus and reads its heading: name, status, who is assigned, and how many
	/// players are matched, nearly matched and still being looked at.
	/// </summary>
	private static async Task<Focus> FocusAt(int index)
	{
		if (await FocusList() is not { } list) return null;
		if (Field(list, "Selected") != index.ToString())
		{
			await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("index", index.ToString()));
			await Until(async () => await FocusList() is { } now && Field(now, "Selected") == index.ToString(), seconds: 4, every: 0.3);
		}

		Focus read = null;
		var previous = "?";
		await Until(async () =>
		{
			var texts = await FocusTexts();
			var at = texts.FindIndex(t => t.StartsWith("Assignments (", StringComparison.Ordinal));
			if (at < 2) return false;
			static int In(string text) => Regex.Match(text, @"\((\d+)\)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;
			var assigned = texts.IndexOf("Assigned to:");
			read = new Focus(index, texts[0], texts[1],
				assigned >= 0 ? string.Join(" · ", texts.Skip(assigned + 1).Take(at - assigned - 1)) : "",
				In(texts.FirstOrDefault(t => t.StartsWith("Matched (", StringComparison.Ordinal)) ?? ""),
				In(texts.FirstOrDefault(t => t.StartsWith("Nearly Matched (", StringComparison.Ordinal)) ?? ""),
				In(texts.FirstOrDefault(t => t.StartsWith("In Progress (", StringComparison.Ordinal)) ?? ""),
				(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Label") == "Remove Recruitment Focus"));
			var key = read.ToString();
			var settled = key == previous;
			previous = key;
			return settled;
		}, seconds: 6, every: 0.5);
		return read;
	}

	private static async Task<List<string>> FocusTexts()
	{
		var anchor = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Select(b => Field(b, "Label"))
			.FirstOrDefault(l => l.StartsWith("Assignments (", StringComparison.Ordinal));
		if (anchor == null) return [];
		return (await Fm.GetJson("texts", ("anchor", anchor), ("up", "10"))).EnumerateArray()
			.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();
	}

	/// <summary>
	/// The players in the results table on screen: name, nation, age, positions, best role,
	/// club, league, value, grade and the scouts' word for him.
	/// </summary>
	private static async Task<List<string[]>> FocusRows()
	{
		var texts = await FocusTexts();
		var from = texts.IndexOf("Recommendation");
		var rows = new List<string[]>();
		if (from < 0) return rows;
		// A row begins where a name is followed by a three-letter nation and an age; whatever
		// stands before the name is the row's status flag ("Inj", "Wnt"), which most rows lack —
		// so the cells cannot simply be counted off in tens.
		var cells = texts.Skip(from + 1).ToList();
		var flags = new List<string>();
		for (var at = 0; at < cells.Count; at++)
		{
			var starts = at + 9 < cells.Count && Regex.IsMatch(cells[at + 1], "^[A-Z]{3}$") && int.TryParse(cells[at + 2], out _);
			if (!starts)
			{
				flags.Add(cells[at]);
				continue;
			}
			rows.Add([cells[at] + (flags.Count > 0 ? $" ({string.Join(", ", flags)})" : ""), .. cells.Skip(at + 1).Take(9)]);
			flags.Clear();
			at += 9;
		}
		return rows;
	}
}
