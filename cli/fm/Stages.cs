using System.Text.Json;

namespace FmCli;

/// <summary>
/// A competition stage by stage (FINDINGS §8.105): the rounds of a cup with their ties, the table
/// of a league stage. The page is Portal › Stages, and its first list reaches every competition
/// in the game's world, not only the club's own — another country's league is read here too.
/// </summary>
internal static partial class Program
{
	private static readonly string CompetitionsFile = Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "competitions.json");

	private static async Task<int> Stages(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var words = Words(args).ToList();
		var all = args.Contains("--all");

		var opened = await OpenPage("Stages", "Portal");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (!await Until(async () => await StageLists() != null, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine("Stages sayfası açılmadı.");
			return 1;
		}

		if (args.Contains("--list"))
		{
			var every = await Competitions(refresh: args.Contains("--refresh"));
			if (json) Console.WriteLine(JsonOut.Of(every.Select(path => new { Name = path[^1], Path = path[1..^1] })));
			else Table.Write(["TURNUVA", "YER"], every.Select(path => new[] { path[^1], string.Join(" › ", path[1..^1]) }).ToList());
			return 0;
		}

		// Which competition: the one named, or the one the page opens on — the next match's.
		if (words.Count > 0)
		{
			var every = await Competitions(refresh: false);
			var path = CompetitionNamed(every, words[0], quiet: true);
			if (path == null)
			{
				// Leagues are added and removed; a name the kept list lacks is looked for afresh.
				every = await Competitions(refresh: true);
				path = CompetitionNamed(every, words[0], quiet: false);
			}
			if (path == null) return 64;

			// The page's own name for it can be shorter than the menu's ("FA Cup" for "English FA
			// Cup"), so that the choice took is told by the page being rebuilt: its lists are
			// new ones.
			var before = (await StageLists()).Value.Competition;
			if (!path[^1].EndsWith(Field(before, "Label"), StringComparison.Ordinal))
			{
				var was = Signature((await StageLists()).Value);
				var picked = await PickIn(false, ("names", string.Join(">", path)));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
				await Until(async () => await StageLists() is { } now && Signature(now) != was, seconds: 10, every: 0.4);
				await Task.Delay(1200);
			}
		}

		var page = (await StageLists()).Value;
		var competition = Field(page.Competition, "Label");
		var stages = await StageNames(page.Stage);
		var current = int.TryParse(Field(page.Stage, "Selected"), out var at) ? at : -1;
		var top = page.Stage.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()).ToList();
		var showing = current >= 0 && current < top.Count ? stages.FirstOrDefault(s => s[0] == top[current].TrimEnd(' ', '›')) : null;

		List<List<string>> wanted;
		if (all) wanted = stages;
		else if (words.Count > 1)
		{
			var typed = Fold(string.Join(" ", words.Skip(1)));
			var hits = stages.Where(s => Fold(string.Join(" ", s)) == typed).ToList();
			if (hits.Count == 0) hits = stages.Where(s => Fold(string.Join(" ", s)).Contains(typed)).ToList();
			if (hits.Count != 1)
			{
				Console.Error.WriteLine(hits.Count == 0 ? $"{competition}: '{string.Join(" ", words.Skip(1))}' diye bir aşama yok. Aşamalar:" : "Birden çok aşamaya uyuyor:");
				foreach (var s in hits.Count == 0 ? stages : hits) Console.Error.WriteLine($"  {string.Join(" › ", s)}");
				return 64;
			}
			wanted = hits;
		}
		else wanted = [null];

		var read = new List<object>();
		foreach (var stage in wanted)
		{
			if (stage != null)
			{
				var picked = await PickIn(true, stage.Count == 1 ? ("choice", stage[0]) : ("names", string.Join(">", stage)));
				if (picked.StartsWith("error", StringComparison.Ordinal)) return Report(picked);
				await Task.Delay(1200);
			}
			var name = stage != null ? string.Join(" › ", stage) : showing != null ? string.Join(" › ", showing) : Field((await StageLists()).Value.Stage, "Label");
			var (ties, table, note) = await StageContent();

			// With no stage named, a cup opens on a round that is yet to be drawn; the last one
			// that has been is what there is to see, and the draw's date goes under it.
			// Rounds are drawn in order, so the last drawn one is searched for by halves rather
			// than walked back to: each look is a second or two.
			string ahead = null;
			if (stage == null && note != null && showing != null)
			{
				var dates = new Dictionary<int, string> { [stages.IndexOf(showing)] = note };
				var best = -1;
				for (int low = 0, high = stages.IndexOf(showing) - 1; low <= high;)
				{
					var middle = (low + high) / 2;
					var stepped = await PickIn(true, stages[middle].Count == 1 ? ("choice", stages[middle][0]) : ("names", string.Join(">", stages[middle])));
					if (stepped.StartsWith("error", StringComparison.Ordinal)) break;
					await Task.Delay(1200);
					var (t, tb, n) = await StageContent(whole: false);
					if (n == null)
					{
						(best, low) = (middle, middle + 1);
					}
					else
					{
						dates[middle] = n;
						high = middle - 1;
					}
				}
				if (best >= 0)
				{
					// The looks took only what was in view; the round is now read whole.
					await PickIn(true, stages[best].Count == 1 ? ("choice", stages[best][0]) : ("names", string.Join(">", stages[best])));
					await Task.Delay(1200);
					(ties, table, note) = await StageContent();
					name = string.Join(" › ", stages[best]);
					if (dates.TryGetValue(best + 1, out var draw)) ahead = $"Sıradaki tur {string.Join(" › ", stages[best + 1])}: kura {draw}";
				}
			}

			if (json)
			{
				read.Add(new { Stage = name, Note = note, Ties = ties, Table = table });
				continue;
			}

			Console.WriteLine($"\n{competition} · {name}");
			if (note != null) Console.WriteLine($"Kura henüz çekilmedi; çekiliş {note}.");
			else if (ties != null) WriteTies(ties.Value);
			else if (table != null && Ui.Rich) Standings.Draw(table.Value, (await Fm.OwnTeam()).Name);
			else if (table != null) WriteStandings(table.Value);
			else Console.WriteLine("Bu aşamada gösterilecek bir şey yok.");
			if (ahead != null && note == null) Console.WriteLine($"\n{ahead}");
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Competition = competition, Stages = stages.Select(s => string.Join(" › ", s)), Read = read }));
			return 0;
		}

		if (!all)
		{
			Console.WriteLine($"\nAşamalar: {string.Join(" · ", stages.Select(s => string.Join(" › ", s)))}");
			Console.WriteLine($"Birini görmek için: stages \"{competition}\" \"<aşama>\"   ·   hepsi: --all");
		}
		return 0;
	}

	/// <summary>
	/// The page's two lists: the competition (the one whose only choice is the "Competitions"
	/// submenu) and, right after it, the stage. They are found afresh each time — choosing a
	/// competition rebuilds the page and its controls with it.
	/// </summary>
	private static async Task<(JsonElement Competition, JsonElement Stage)?> StageLists()
	{
		var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
		for (var i = 0; i + 1 < lists.Count; i++)
		{
			var choices = lists[i].GetProperty("Choices");
			if (choices.GetArrayLength() != 1 || choices[0].GetString() != "Competitions ›") continue;
			if (Field(lists[i], "Label").Length == 0) continue;
			return (lists[i], lists[i + 1]);
		}
		return null;
	}

	private static string Signature((JsonElement Competition, JsonElement Stage) lists) =>
		Field(lists.Competition, "Label") + "|" + Field(lists.Competition, "Pointer") + "|" + Field(lists.Stage, "Pointer") + "|" + lists.Stage.GetProperty("Choices").GetRawText();

	/// <summary>
	/// Chooses in the competition list or the stage list. The page is rebuilt when it is opened
	/// and when its competition changes, and a list found a moment before can be gone by the
	/// time it is chosen in; it is then found again.
	/// </summary>
	private static async Task<string> PickIn(bool stage, (string, string) what)
	{
		var answer = "error: Stages sayfasının listeleri bulunamadı";
		for (var attempt = 0; attempt < 4; attempt++)
		{
			if (await StageLists() is { } lists)
			{
				answer = await Fm.Get("select", ("ptr", Field(stage ? lists.Stage : lists.Competition, "Pointer")), what);
				if (!answer.StartsWith("error: no dropdown", StringComparison.Ordinal)) return answer;
			}
			await Task.Delay(700);
		}
		return answer;
	}

	/// <summary>The stages in the stage list, submenus opened ("Main Path › First Qualifying Round"); the xG table is not one.</summary>
	private static async Task<List<List<string>>> StageNames(JsonElement list)
	{
		var stages = new List<List<string>>();
		foreach (var choice in list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString()))
		{
			if (choice == "xG Table") continue;
			if (!choice.EndsWith(" ›", StringComparison.Ordinal))
			{
				stages.Add([choice]);
				continue;
			}
			var parent = choice[..^2];
			foreach (var child in (await Fm.GetJson("menu", ("ptr", Field(list, "Pointer")), ("names", parent))).EnumerateArray())
				stages.Add([parent, child.GetString().TrimEnd(' ', '›')]);
		}
		return stages;
	}

	/// <summary>
	/// What the stage on screen holds, once it has stopped changing: the date of its draw (a round
	/// not drawn yet), its ties, or its table — the last of the tables bound, the page's own coming after
	/// the portal tile's.
	/// </summary>
	private static async Task<(JsonElement? Ties, JsonElement? Table, string Note)> StageContent(bool whole = true)
	{
		JsonElement ties = default, tables = default;
		var last = "";
		await Until(async () =>
		{
			ties = await Fm.GetJson("ties");
			tables = await Fm.GetJson("table", ("groups", "1"));
			var now = ties.GetRawText() + tables.GetRawText();
			var settled = now == last;
			last = now;
			return settled;
		}, seconds: 8, every: 0.6);

		if (ties.GetArrayLength() > 0) return (whole ? await EveryTie(ties) : ties, null, null);

		var stage = Field((await StageLists()).Value.Stage, "Label");
		var note = (await Fm.GetJson("texts", ("anchor", stage), ("up", "12"))).EnumerateArray()
			.Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "")
			.FirstOrDefault(t => t.StartsWith("This round will be drawn", StringComparison.Ordinal));
		if (note != null) return (null, null, note["This round will be drawn on ".Length..]);

		var table = tables.EnumerateArray().LastOrDefault(t => t.GetArrayLength() >= 2);
		return table.ValueKind == JsonValueKind.Array ? (null, table, null) : (null, null, null);
	}

	/// <summary>
	/// A long round only lays out the ties in view, so the list is walked top to bottom and what
	/// each stop shows is added in the order it first appears.
	/// </summary>
	private static async Task<JsonElement> EveryTie(JsonElement first)
	{
		static string Key(JsonElement tie) => Field(tie, "Home") + "|" + Field(tie, "Away");
		async Task<string> Scroll(JsonElement shown, string to)
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

		// "0 of 1636.6": how far down, of how far there is to go.
		var where = await Scroll(first, null);
		var extent = where?.Split(" of ") is { Length: 2 } parts && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var e) ? e : 0;
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
		Table.Write(twoLegged ? ["EV", "SKOR", "DEPLASMAN", "TOPLAM"] : ["EV", "SKOR", "DEPLASMAN"], rows);
	}

	/// <summary>
	/// Every competition in the list's menu as the path to it ("Competitions", "Europe",
	/// "Türkiye", "Men's", "Turkish Cup"), walked once and kept: the walk is a call a submenu.
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

		var list = Field((await StageLists()).Value.Competition, "Pointer");
		var found = new List<string[]>();
		async Task Walk(List<string> path, int depth)
		{
			if (depth > 6) return;
			JsonElement children;
			try { children = await Fm.GetJson("menu", ("ptr", list), ("names", string.Join(">", path))); }
			catch (Exception e) when (e is FmError or JsonException) { return; }
			if (children.ValueKind != JsonValueKind.Array) return;
			foreach (var child in children.EnumerateArray().Select(c => c.GetString()))
			{
				if (child.EndsWith(" ›", StringComparison.Ordinal)) await Walk([.. path, child[..^2]], depth + 1);
				else found.Add([.. path, child]);
			}
		}
		await Walk(["Competitions"], 0);

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

		Console.Error.WriteLine(hits.Count == 0 ? $"'{typed}' diye bir turnuva yok (hepsi: stages --list)." : $"'{typed}' birden çok turnuvaya uyuyor:");
		foreach (var hit in hits.Take(20)) Console.Error.WriteLine($"  {hit[^1]}   ({string.Join(" › ", hit[1..^1])})");
		return null;

		static bool Senior(string[] path) => path.Contains("Men's") && !path.Any(p => p.StartsWith("Under", StringComparison.Ordinal));
	}
}
