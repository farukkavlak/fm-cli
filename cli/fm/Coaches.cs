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
		("Att Tact", L("Hücum taktik"), ["hücum taktik", "hucum taktik", "att tact", "attacking tactical"]),
		("Att Tech", L("Hücum teknik"), ["hücum teknik", "hucum teknik", "att tech", "attacking technical"]),
		("Def Tact", L("Savunma taktik"), ["savunma taktik", "def tact", "defending tactical"]),
		("Def Tech", L("Savunma teknik"), ["savunma teknik", "def tech", "defending technical"]),
		("Poss Tact", L("Topla taktik"), ["topla taktik", "poss tact", "possession tactical"]),
		("Poss Tech", L("Topla teknik"), ["topla teknik", "poss tech", "possession technical"]),
		("Glk", L("Kaleci"), ["kaleci", "glk", "goalkeeping"]),
		("Fit", L("Kondisyon"), ["kondisyon", "fit", "fitness"]),
		("SPC", L("Duran top"), ["duran top", "spc", "set pieces"]),
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
			Console.Error.WriteLine(L("Antrenör görevleri sayfası açılmadı."));
			return 1;
		}

		var words = Words(args).ToList();
		if (args.Contains("--auto"))
		{
			var ask = await Labelled("Ask Assistant to Assign");
			if (ask == null)
			{
				Console.Error.WriteLine(L("Görevleri yardımcıya bırakma düğmesi yok."));
				return 1;
			}
			var before = Assignments(page);
			await Fm.Get("press", ("ptr", Field(ask.Value, "Pointer")));
			// The page is rebuilt with the assistant's choice (about half a second). When he would choose
			// what is already there nothing changes and nothing says so; the wait then ends at its limit.
			await Until(async () => (page = await ReadCoaches()) is { Rows.Count: > 0 } && Assignments(page) != before, seconds: 3, every: 0.25);
			Console.WriteLine(L("Yardımcı görevleri dağıttı."));
			page = await ReadCoaches();
		}
		else if (words.Count > 0)
		{
			// The coach is the words that name one; the area is what is left.
			var rows = page.Value.Rows;
			var coach = rows.FirstOrDefault(r => words.Any(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)));
			if (coach == null)
			{
				Console.Error.WriteLine(L($"Antrenör bulunamadı. Antrenörler: {string.Join(", ", rows.Select(r => r.Name))}"));
				return 1;
			}
			var rest = string.Join(" ", words.Where(w => !coach.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToLowerInvariant();
			var area = Array.FindIndex(CoachAreas, a => a.Words.Contains(rest) || a.Heading.Equals(rest, StringComparison.OrdinalIgnoreCase));
			if (area < 0)
			{
				Console.Error.WriteLine(L($"Alan bilinmiyor: '{rest}'. Alanlar: {string.Join(", ", CoachAreas.Select(a => a.Turkish.ToLowerInvariant()))}"));
				return 1;
			}

			var cell = coach.Cells[area];
			var on = !args.Contains("--off");
			if (!cell.Enabled)
			{
				Console.Error.WriteLine(L($"{coach.Name} ({coach.Role}) {CoachAreas[area].Turkish.ToLowerInvariant()} alanına atanamaz."));
				return 1;
			}
			if (cell.On == on)
				Console.WriteLine(L($"{coach.Name} {CoachAreas[area].Turkish.ToLowerInvariant()} alanında {(on ? L("zaten görevli") : L("zaten görevli değil"))}."));
			else
			{
				var set = await Fm.Get("setcontrol", ("ptr", cell.Pointer), ("value", on ? "1" : "0"));
				if (set.StartsWith("error", StringComparison.Ordinal)) return Report(set);
				// The switch turns at once, the coach's stars and the area's load a moment later when the
				// page is rebuilt: done once his stars show where he now leads and are gone where he left.
				await Until(async () => await ReadCoaches() is { } after
					&& after.Rows.FirstOrDefault(r => r.Name == coach.Name)?.Cells[area] is { } now && now.On == on && now.HalfStars > 0 == on,
					seconds: 5, every: 0.25);
				Console.WriteLine($"{coach.Name} {CoachAreas[area].Turkish.ToLowerInvariant()} {(on ? L("alanına atandı") : L("görevinden alındı"))}.");
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

	/// <summary>Who leads what and with how many stars, as one string to tell two reads of the page apart.</summary>
	private static string Assignments((List<CoachRow> Rows, List<(string Load, int HalfStars)> Areas)? page) =>
		page is { } p ? string.Join("|", p.Rows.Select(r => r.Name + ":" + string.Concat(r.Cells.Select(c => c.On ? $"{c.HalfStars}," : "-,")))) : "";

	private static void ShowCoaches(List<CoachRow> rows, List<(string Load, int HalfStars)> areas)
	{
		static string Stars(int half) => half <= 0 ? "" : new string('★', half / 2) + (half % 2 == 1 ? "½" : "");

		// A coach to a row and an area to a column, as on the page: ✓ and his stars where he leads it,
		// · where he could, nothing where his job does not allow it.
		string[] shortNames = [L("HÜC-TAK"), L("HÜC-TEK"), L("SAV-TAK"), L("SAV-TEK"), L("TOP-TAK"), L("TOP-TEK"), L("KALECİ"), L("KOND"), L("DURAN")];
		var headers = new[] { L("ANTRENÖR") }.Concat(shortNames).ToArray();
		var table = rows.Select(r => new[] { r.Name }
			.Concat(r.Cells.Select(c => c.On ? $"✓ {Stars(c.HalfStars)}".Trim() : c.Enabled ? "·" : "")).ToArray()).ToList();
		table.Add(new[] { L("iş yükü") }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? areas[i].Load : "")).ToArray());
		table.Add(new[] { L("baş antrenör") }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? Stars(areas[i].HalfStars) is { Length: > 0 } st ? st : "yok" : "")).ToArray());
		Table.Write(headers, table);
		Console.WriteLine(L("HÜC hücum · SAV savunma · TOP topla oynama · TAK taktik · TEK teknik · KOND kondisyon · DURAN duran top"));

		var empty = CoachAreas.Where((_, i) => !rows.Any(r => r.Cells[i].On)).Select(a => a.Turkish.ToLowerInvariant()).ToList();
		if (empty.Count > 0) Console.WriteLine(L($"Antrenörü olmayan alan: {string.Join(", ", empty)}"));
		Console.WriteLine(L("Atamak için: coaches <antrenör> <alan>   (kaldırmak: --off;  yardımcıya bırakmak: --auto)"));
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

		// While the page is being built the grid comes in pieces: switches before their stars, rows before
		// their coach's name. Every switch has its stars (none shown where he is not assigned), so the grid
		// is whole only when it reads as name, role and nine switch-and-stars cells up to the loads.
		var i = start + 1;
		while (i < items.Count && !(Field(items[i], "Kind") == "text" && Field(items[i], "Text") == "Coach Workload"))
		{
			if (i + 1 >= items.Count || Field(items[i], "Kind") != "text" || Field(items[i + 1], "Kind") != "text") return null;
			var name = Field(items[i], "Text");
			var role = Field(items[i + 1], "Text");
			i += 2;
			var cells = new List<CoachCell>();
			while (cells.Count < CoachAreas.Length && i + 1 < items.Count && Field(items[i], "Kind") == "checkbox")
			{
				var box = items[i];
				var stars = items[i + 1];
				if (Field(stars, "Kind") != "stars") return null;
				cells.Add(new CoachCell(Field(box, "Value") == "True", Field(box, "Enabled") == "True", Field(box, "Pointer"),
					int.TryParse(Field(stars, "Shown"), out var s) ? s : 0));
				i += 2;
			}
			if (cells.Count != CoachAreas.Length) return null;
			rows.Add(new CoachRow(name, role, cells.ToArray()));
		}
		if (i >= items.Count) return null;

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
