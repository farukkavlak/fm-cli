using System.Text.Json;

namespace FmCli;

/// <summary>
/// Coach assignments (Squad › Training › Coach Assignments, FINDINGS §8.147): which coach leads
/// which of the nine training areas, how heavy each area's load is and how good its lead coach.
/// The page is read as a form: a coach's name and role, then for each area a switch and the
/// stars he is worth there (shown only where he is assigned), in the order of the headings.
/// </summary>
internal static partial class Program
{
	private static readonly (string Heading, string Turkish, string[] Words)[] CoachAreas =
	[
		("Att Tact", "Hücum taktik", ["hücum taktik", "hucum taktik", "att tact", "attacking tactical"]),
		("Att Tech", "Hücum teknik", ["hücum teknik", "hucum teknik", "att tech", "attacking technical"]),
		("Def Tact", "Savunma taktik", ["savunma taktik", "def tact", "defending tactical"]),
		("Def Tech", "Savunma teknik", ["savunma teknik", "def tech", "defending technical"]),
		("Poss Tact", "Topla taktik", ["topla taktik", "poss tact", "possession tactical"]),
		("Poss Tech", "Topla teknik", ["topla teknik", "poss tech", "possession technical"]),
		("Glk", "Kaleci", ["kaleci", "glk", "goalkeeping"]),
		("Fit", "Kondisyon", ["kondisyon", "fit", "fitness"]),
		("SPC", "Duran top", ["duran top", "spc", "set pieces"]),
	];

	private sealed record CoachCell(bool On, bool Enabled, string Pointer, int HalfStars);
	private sealed record CoachRow(string Name, string Role, CoachCell[] Cells);

	private static async Task<int> Coaches(string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenSubpage(Screen.Training, Screen.CoachAssignments, async () => await ReadCoaches() is { Rows.Count: > 0 });
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		(List<CoachRow> Rows, List<(string Load, int HalfStars)> Areas)? page = null;
		if (!await Until(async () => (page = await ReadCoaches()) is { Rows.Count: > 0 }, seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine("Antrenör görevleri sayfası açılmadı.");
			return 1;
		}

		var words = Words(args).ToList();
		if (args.Contains("--auto"))
		{
			var ask = await Labelled("Ask Assistant to Assign");
			if (ask == null)
			{
				Console.Error.WriteLine("Görevleri yardımcıya bırakma düğmesi yok.");
				return 1;
			}
			await Fm.Get("press", ("ptr", Field(ask.Value, "Pointer")));
			await Task.Delay(1500);
			Console.WriteLine("Yardımcı görevleri dağıttı.");
			page = await ReadCoaches();
		}
		else if (words.Count > 0)
		{
			// The coach is the words that name one; the area is what is left.
			var rows = page.Value.Rows;
			var coach = rows.FirstOrDefault(r => words.Any(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)));
			if (coach == null)
			{
				Console.Error.WriteLine($"Antrenör bulunamadı. Antrenörler: {string.Join(", ", rows.Select(r => r.Name))}");
				return 1;
			}
			var rest = string.Join(" ", words.Where(w => !coach.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToLowerInvariant();
			var area = Array.FindIndex(CoachAreas, a => a.Words.Contains(rest) || a.Heading.Equals(rest, StringComparison.OrdinalIgnoreCase));
			if (area < 0)
			{
				Console.Error.WriteLine($"Alan bilinmiyor: '{rest}'. Alanlar: {string.Join(", ", CoachAreas.Select(a => a.Turkish.ToLowerInvariant()))}");
				return 1;
			}

			var cell = coach.Cells[area];
			var on = !args.Contains("--off");
			if (!cell.Enabled)
			{
				Console.Error.WriteLine($"{coach.Name} ({coach.Role}) {CoachAreas[area].Turkish.ToLowerInvariant()} alanına atanamaz.");
				return 1;
			}
			if (cell.On == on)
				Console.WriteLine($"{coach.Name} {CoachAreas[area].Turkish.ToLowerInvariant()} alanında {(on ? "zaten görevli" : "zaten görevli değil")}.");
			else
			{
				var set = await Fm.Get("setcontrol", ("ptr", cell.Pointer), ("value", on ? "1" : "0"));
				if (set.StartsWith("error", StringComparison.Ordinal)) return Report(set);
				await Task.Delay(800);
				Console.WriteLine($"{coach.Name} {CoachAreas[area].Turkish.ToLowerInvariant()} {(on ? "alanına atandı" : "görevinden alındı")}.");
				page = await ReadCoaches();
			}
		}

		if (page is not { } now) return 1;
		if (args.Contains("--json"))
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Coaches = now.Rows.Select(r => new { r.Name, r.Role, Areas = CoachAreas.Select((a, i) => new { Area = a.Heading, r.Cells[i].On, r.Cells[i].Enabled, Stars = r.Cells[i].HalfStars / 2.0 }) }),
				Areas = CoachAreas.Select((a, i) => new { Area = a.Heading, Workload = i < now.Areas.Count ? now.Areas[i].Load : "", LeadStars = i < now.Areas.Count ? now.Areas[i].HalfStars / 2.0 : 0 }),
			}));
			return 0;
		}

		ShowCoaches(now.Rows, now.Areas);
		return 0;
	}

	private static void ShowCoaches(List<CoachRow> rows, List<(string Load, int HalfStars)> areas)
	{
		static string Stars(int half) => half <= 0 ? "" : new string('★', half / 2) + (half % 2 == 1 ? "½" : "");

		// A coach to a row and an area to a column, as on the page: ✓ and his stars where he leads it,
		// · where he could, nothing where his job does not allow it.
		string[] shortNames = ["HÜC-TAK", "HÜC-TEK", "SAV-TAK", "SAV-TEK", "TOP-TAK", "TOP-TEK", "KALECİ", "KOND", "DURAN"];
		var headers = new[] { "ANTRENÖR" }.Concat(shortNames).ToArray();
		var table = rows.Select(r => new[] { r.Name }
			.Concat(r.Cells.Select(c => c.On ? $"✓ {Stars(c.HalfStars)}".Trim() : c.Enabled ? "·" : "")).ToArray()).ToList();
		table.Add(new[] { "iş yükü" }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? areas[i].Load : "")).ToArray());
		table.Add(new[] { "baş antrenör" }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? Stars(areas[i].HalfStars) is { Length: > 0 } st ? st : "yok" : "")).ToArray());
		Table.Write(headers, table);
		Console.WriteLine("HÜC hücum · SAV savunma · TOP topla oynama · TAK taktik · TEK teknik · KOND kondisyon · DURAN duran top");

		var empty = CoachAreas.Where((_, i) => !rows.Any(r => r.Cells[i].On)).Select(a => a.Turkish.ToLowerInvariant()).ToList();
		if (empty.Count > 0) Console.WriteLine($"Antrenörü olmayan alan: {string.Join(", ", empty)}");
		Console.WriteLine("Atamak için: coaches <antrenör> <alan>   (kaldırmak: --off;  yardımcıya bırakmak: --auto)");
	}

	/// <summary>The page as its form reads: the coaches with their nine switches, then each area's load and lead.</summary>
	private static async Task<(List<CoachRow> Rows, List<(string Load, int HalfStars)> Areas)?> ReadCoaches()
	{
		JsonElement form;
		try { form = await Fm.GetJson("form", ("anchor", "Set Coach Assignments"), ("up", "3")); }
		catch (FmError) { return null; }
		if (form.ValueKind != JsonValueKind.Array) return null;

		var items = form.EnumerateArray().ToList();
		var rows = new List<CoachRow>();
		var areas = new List<(string, int)>();
		var start = items.FindIndex(x => Field(x, "Kind") == "text" && Field(x, "Text") == CoachAreas[^1].Heading);
		if (start < 0) return null;

		var i = start + 1;
		while (i + 1 < items.Count && Field(items[i], "Kind") == "text" && Field(items[i], "Text") != "Coach Workload")
		{
			var name = Field(items[i], "Text");
			var role = Field(items[i + 1], "Text");
			i += 2;
			var cells = new List<CoachCell>();
			while (cells.Count < CoachAreas.Length && i < items.Count && Field(items[i], "Kind") == "checkbox")
			{
				var box = items[i];
				var stars = i + 1 < items.Count && Field(items[i + 1], "Kind") == "stars" ? items[i + 1] : default;
				cells.Add(new CoachCell(Field(box, "Value") == "True", Field(box, "Enabled") == "True", Field(box, "Pointer"),
					stars.ValueKind == JsonValueKind.Object && int.TryParse(Field(stars, "Shown"), out var s) ? s : 0));
				i += stars.ValueKind == JsonValueKind.Object ? 2 : 1;
			}
			if (cells.Count == CoachAreas.Length) rows.Add(new CoachRow(name, role, cells.ToArray()));
		}

		// Under the grid: each area's load in words, then its lead coach's stars (none for an area nobody leads).
		// The load is one of the game's words, shown as the game writes it: in a game set to another
		// language its English can come back as another text translated the same way ("Minor" for "Light").
		var lead = items.FindIndex(i, x => Field(x, "Kind") == "text" && Field(x, "Text") == "Lead Coach Rating");
		for (var j = lead + 1; lead > 0 && j < items.Count && areas.Count < CoachAreas.Length; j++)
		{
			if (Field(items[j], "Kind") != "text") break;
			var half = j + 1 < items.Count && Field(items[j + 1], "Kind") == "stars" && int.TryParse(Field(items[j + 1], "Shown"), out var s) ? s : 0;
			areas.Add((Field(items[j], "Text"), half));
			if (j + 1 < items.Count && Field(items[j + 1], "Kind") == "stars") j++;
		}
		return (rows, areas);
	}
}
