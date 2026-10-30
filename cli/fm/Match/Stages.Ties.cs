using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// A long round only lays out the ties in view, so the list is walked top to bottom and what
	/// each stop shows is added in the order it first appears.
	/// </summary>
	private static async Task<JsonElement> EveryTie(JsonElement first)
	{
		static string Key(JsonElement tie) => Field(tie, "Home") + "|" + Field(tie, "Away");
		async Task<string?> Scroll(JsonElement shown, string? to)
		{
			// The list is moved by a text that is in it; a club's name can also be elsewhere on
			// the screen, so the first one that answers as a list is used.
			foreach (var tie in shown.EnumerateArray())
			{
				if (Field(tie, "Home").Length == 0) continue;
				var query = new List<(string, string)> { ("anchor", Field(tie, "Home")) };
				if (to != null) query.Add(("to", to));
				var answer = await Fm.Get("scroll", [.. query]);
				if (!answer.StartsWith("error", StringComparison.Ordinal)) return answer;
			}
			return null;
		}

		// "0 of 1636.6": how far down, of how far there is to go. The game writes the mark its
		// language uses ("1401,8" in Turkish), so its own reader parses it.
		var where = await Scroll(first, null);
		var extent = where?.Split(" of ") is { Length: 2 } parts ? GameNumbers.Read(parts[1].Trim()) ?? 0 : 0;
		if (extent <= 0) return first;

		var seen = new HashSet<string>();
		var every = new List<JsonElement>();
		var shown = first;
		for (var step = 0; step <= 10; step++)
		{
			if (await Scroll(shown, (step / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)) == null) break;
			var last = "";
			await Until(async () =>
			{
				shown = await Fm.GetJson("ties");
				var now = shown.GetRawText();
				var settled = now == last;
				last = now;
				return settled;
			}, seconds: 3, every: 0.2);
			foreach (var tie in shown.EnumerateArray())
				if (seen.Add(Key(tie))) every.Add(tie.Clone());
		}
		await Scroll(shown, "0");
		return JsonSerializer.SerializeToElement(every);
	}

	/// <summary>
	/// One line a tie. The small text beside a team is its division in a domestic cup and its
	/// aggregate in a two-legged tie; "p" and "e" beside the score say the side they are on went
	/// through on penalties or in extra time.
	/// </summary>
	private static void WriteTies(JsonElement ties)
	{
		static string Side(JsonElement tie, string side)
		{
			var extra = Field(tie, side + "Extra");
			var note = Field(tie, side + "Note") switch { "p" => " (pen.)", "e" => " (uz.)", "" => "", var other => $" ({other})" };
			var division = extra.Length > 0 && !extra.All(char.IsDigit) ? $" [{extra}]" : "";
			return Field(tie, side) + division + note;
		}

		var twoLegged = ties.EnumerateArray().Any(t => Field(t, "HomeExtra") is { Length: > 0 } e && e.All(char.IsDigit));
		var rows = ties.EnumerateArray().Select(tie =>
		{
			var score = Field(tie, "Score") is "V" or "" ? "-" : Field(tie, "Score");
			var row = new List<string> { Side(tie, "Home"), score, Side(tie, "Away") };
			if (twoLegged) row.Add(Field(tie, "HomeExtra").Length > 0 ? $"{Field(tie, "HomeExtra")} - {Field(tie, "AwayExtra")}" : "");
			return row.ToArray();
		}).ToList();
		Table.Write(twoLegged ? [L("HOME"), L("SCORE"), L("AWAY"), L("TOTAL")] : [L("HOME"), L("SCORE"), L("AWAY")], rows);
	}

	/// <summary>
	/// Every competition in the list's menu as the path to it ("Competitions", "Europe",
	/// "Turkey", "Men's", "Turkish Cup"), walked once and kept: the walk is a call a submenu.
	/// </summary>
	private static async Task<List<string[]>> Competitions(bool refresh)
	{
		if (!refresh && File.Exists(CompetitionsFile))
		{
			try
			{
				var kept = JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(CompetitionsFile));
				if (kept is { Count: > 0 }) return kept;
			}
			catch (JsonException) { }
		}

		// The walk is made in the Stages page's list of competitions, opened for it if need be.
		if (await StageLists() == null)
		{
			await OpenPage(Screen.Stages);
			if (!await Until(async () => await StageLists() != null, seconds: 10, every: 0.5)) return [];
		}
		var list = Field((await StageLists())!.Value.Competition, "Pointer");
		var found = new List<string[]>();
		async Task Walk(List<string> path, int depth)
		{
			if (depth > 6) return;
			JsonElement children;
			try { children = await Fm.GetJson("menu", ("ptr", list), ("names", string.Join(">", path))); }
			catch (Exception e) when (e is FmError or JsonException) { return; }
			if (children.ValueKind != JsonValueKind.Array) return;
			foreach (var child in children.EnumerateArray().Select(c => c.GetString() ?? ""))
			{
				if (child.EndsWith(" ›", StringComparison.Ordinal)) await Walk([.. path, child[..^2]], depth + 1);
				else found.Add([.. path, child]);
			}
		}
		await Walk([], 0);

		if (found.Count > 0)
		{
			Directory.CreateDirectory(Path.GetDirectoryName(CompetitionsFile)!);
			File.WriteAllText(CompetitionsFile, JsonSerializer.Serialize(found));
		}
		return found;
	}

	/// <summary>
	/// The competition a typed name means: its exact name first, then the one name it is part
	/// of. Several men's senior ones matching is said, not guessed between; a youth or women's
	/// namesake does not stand in the way of the senior men's one.
	/// </summary>
	private static string[] CompetitionNamed(List<string[]> every, string typed, bool quiet)
	{
		var wanted = Fold(typed);
		var hits = every.Where(p => Fold(p[^1]) == wanted).ToList();
		if (hits.Count == 0) hits = every.Where(p => Fold(p[^1]).Contains(wanted)).ToList();
		if (hits.Count > 1 && hits.Where(Senior).ToList() is { Count: > 0 } senior) hits = senior;
		if (hits.Count == 1) return hits[0];
		if (quiet) return null;

		Console.Error.WriteLine(hits.Count == 0 ? L($"There is no competition called '{typed}' (all of them: stages --list).") : L($"'{typed}' matches more than one competition:"));
		foreach (var hit in hits.Take(20)) Console.Error.WriteLine($"  {hit[^1]}   ({string.Join(" › ", hit[1..^1])})");
		return null;

		static bool Senior(string[] path) => path.Contains("Men's") && !path.Any(p => p.StartsWith("Under", StringComparison.Ordinal));
	}
}
