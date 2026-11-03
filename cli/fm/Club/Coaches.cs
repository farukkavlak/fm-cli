using System.Text.Json;

namespace FmCli;

/// <summary>
/// Coach assignments (Squad › Training › Coach Assignments): which coach leads
/// which of the nine training areas, how heavy each area's load is and how good its lead coach.
/// The page is read as a form: a coach's name and role, then for each area a switch and the
/// stars he is worth there (shown only where he is assigned), in the order of the headings.
/// </summary>
internal static partial class Program
{
	/// <summary>The nine areas, each by the name the game's records give it (CoachAssignmentReference).</summary>
	private static readonly (string Heading, string Turkish, string[] Words, string Record)[] CoachAreas =
	[
		("Att Tact", L("Attacking tactical"), ["hucum taktik", "att tact", "attacking tactical"], "AttackingTacticalCategory"),
		("Att Tech", L("Attacking technical"), ["hucum teknik", "att tech", "attacking technical"], "AttackingTechnicalCategory"),
		("Def Tact", L("Defending tactical"), ["savunma taktik", "def tact", "defending tactical"], "DefendingTacticalCategory"),
		("Def Tech", L("Defending technical"), ["savunma teknik", "def tech", "defending technical"], "DefendingTechnicalCategory"),
		("Poss Tact", L("Possession tactical"), ["topla taktik", "poss tact", "possession tactical"], "PossessionTacticalCategory"),
		("Poss Tech", L("Possession technical"), ["topla teknik", "poss tech", "possession technical"], "PossessionTechnicalCategory"),
		("Glk", L("Goalkeeping"), ["kaleci", "glk", "goalkeeping"], "GoalkeepingCategory"),
		("Fit", L("Condition"), ["kondisyon", "fit", "fitness"], "FitnessCategory"),
		("SPC", L("Set pieces"), ["duran top", "spc", "set pieces"], "SetPiecesCategory"),
	];

	/// <summary>An area's load as the record numbers it (0 none … 4 very heavy), in the CLI's words.</summary>
	private static string WorkloadWord(int load) => load switch
	{
		0 => L("No load"),
		1 => L("Light"),
		2 => L("Average"),
		3 => L("Heavy"),
		_ => L("Very heavy"),
	};

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
			Console.Error.WriteLine(L("The coaching assignments page did not open."));
			return 1;
		}

		var words = Words(args).ToList();
		// The assistant's share-out and Remove All, each by the event its button sends.
		if ((args.Contains("--auto"), args.Contains("--clear")) is (true, _) or (_, true))
		{
			var (wanted, missing, done) = args.Contains("--auto")
				? ("EventFmxAssistantAssignCoaches", L("There is no button to leave the assignments to the assistant."), L("The assistant shared out the assignments."))
				: ("EventFmxClearCoaches", L("There is no button to remove all the assignments."), L("Every assignment removed."));
			var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.Where(b => Field(b, "Events").Contains(wanted, StringComparison.Ordinal) && Field(b, "Enabled") == "True")
				.Cast<JsonElement?>().FirstOrDefault();
			if (button == null)
			{
				Console.Error.WriteLine(missing);
				return 1;
			}
			var before = Assignments(page);
			await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")));
			// The page is rebuilt with the new assignments (about half a second). When they are what is
			// already there nothing changes and nothing says so; the wait then ends at its limit.
			await Until(async () => (page = await ReadCoaches()) is { Rows.Count: > 0 } && Assignments(page) != before, seconds: 3, every: 0.25);
			page = await ReadCoaches();
			// Read back: after Remove All no coach may be left in an area he could leave.
			if (args.Contains("--clear") && page is { } after && after.Rows.Any(r => r.Cells.Any(c => c.On && c.Enabled)))
			{
				Console.Error.WriteLine(L("Told to remove every assignment, but some are still there."));
				return 1;
			}
			Console.WriteLine(done);
		}
		else if (words.Count > 0)
		{
			// The coach is the words that name one; the area is what is left.
			var rows = page!.Value.Rows;
			var coach = rows.FirstOrDefault(r => words.Any(w => r.Name.Contains(w, StringComparison.OrdinalIgnoreCase)));
			if (coach == null)
			{
				Console.Error.WriteLine(L($"Coach not found. Coaches: {string.Join(", ", rows.Select(r => r.Name))}"));
				return 1;
			}
			var rest = string.Join(" ", words.Where(w => !coach.Name.Contains(w, StringComparison.OrdinalIgnoreCase))).ToLowerInvariant();
			var area = Array.FindIndex(CoachAreas, a => a.Words.Contains(rest) || a.Heading.Equals(rest, StringComparison.OrdinalIgnoreCase));
			if (area < 0)
			{
				Console.Error.WriteLine(L($"Unknown area: '{rest}'. Areas: {string.Join(", ", CoachAreas.Select(a => a.Turkish.ToLowerInvariant()))}"));
				return 1;
			}

			var cell = coach.Cells[area];
			var on = !args.Contains("--off");
			if (!cell.Enabled)
			{
				Console.Error.WriteLine(L($"{coach.Name} ({coach.Role}) cannot be assigned to {CoachAreas[area].Turkish.ToLowerInvariant()}."));
				return 1;
			}
			if (cell.On == on)
				Console.WriteLine(L($"{coach.Name} {(on ? L("is already assigned") : L("is not assigned anyway"))} in {CoachAreas[area].Turkish.ToLowerInvariant()}."));
			else
			{
				var set = await Fm.Get("setcontrol", ("ptr", cell.Pointer), ("value", on ? "1" : "0"));
				if (set.StartsWith("error", StringComparison.Ordinal)) return Report(set);
				// The switch turns at once, the coach's stars and the area's load a moment later when the
				// page is rebuilt: done once his stars show where he now leads and are gone where he left.
				await Until(async () => await ReadCoaches() is { } after
					&& after.Rows.FirstOrDefault(r => r.Name == coach.Name)?.Cells[area] is { } now && now.On == on && now.HalfStars > 0 == on,
					seconds: 5, every: 0.25);
				Console.WriteLine($"{coach.Name} {CoachAreas[area].Turkish.ToLowerInvariant()} {(on ? L("— assigned") : L("— removed"))}.");
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
		string[] shortNames = [L("ATT-TAC"), L("ATT-TEC"), L("DEF-TAC"), L("DEF-TEC"), L("POS-TAC"), L("POS-TEC"), L("GK"), L("FIT"), L("SPC")];
		var headers = new[] { L("COACH") }.Concat(shortNames).ToArray();
		var table = rows.Select(r => new[] { r.Name }
			.Concat(r.Cells.Select(c => c.On ? $"✓ {Stars(c.HalfStars)}".Trim() : c.Enabled ? "·" : "")).ToArray()).ToList();
		table.Add(new[] { L("workload") }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? areas[i].Load : "")).ToArray());
		table.Add(new[] { L("lead coach") }.Concat(CoachAreas.Select((_, i) => i < areas.Count ? Stars(areas[i].HalfStars) is { Length: > 0 } st ? st : L("none") : "")).ToArray());
		Table.Write(headers, table);
		Console.WriteLine(L("ATT attacking · DEF defending · POS possession · TAC tactical · TEC technical · FIT fitness · SPC set pieces"));

		var empty = CoachAreas.Where((_, i) => !rows.Any(r => r.Cells[i].On)).Select(a => a.Turkish.ToLowerInvariant()).ToList();
		if (empty.Count > 0) Console.WriteLine(L($"Area with no coach: {string.Join(", ", empty)}"));
		Console.WriteLine(L("To assign: coaches <coach> <area>   (to remove: --off;  to leave it to the assistant: --auto)"));
	}

	/// <summary>
	/// The page as its form reads: the coaches with their nine switches, then each area's load and lead.
	/// Every cell is known by the area its record names (the switch's "Of"), and every load and lead
	/// by the area its block is remapped to ("Remap"), so neither the columns' order nor their headings count.
	/// </summary>
	private static async Task<(List<CoachRow> Rows, List<(string Load, int HalfStars)> Areas)?> ReadCoaches()
	{
		JsonElement form;
		// The grid around the first coach's name (bound to Person.Name), up to the page's block.
		try { form = await Fm.GetJson("form", ("anchorpath", "Person.Name"), ("up", "14"), ("most", "1")); }
		catch (FmError) { return null; }
		if (form.ValueKind != JsonValueKind.Array) return null;

		var items = form.EnumerateArray().ToList();
		var rows = new List<CoachRow>();
		var i = items.FindIndex(x => Field(x, "Path") == "Person.Name");
		if (i < 0) return null;

		// A row is a coach's name and job (Person.Name, Person.JobString), then for each area a switch and
		// his stars there (none shown where he is not assigned). While the page is being built the grid
		// comes in pieces, so it counts only when every row has all nine areas, each named by its record.
		while (i < items.Count && Field(items[i], "Path") == "Person.Name")
		{
			if (i + 1 >= items.Count || Field(items[i + 1], "Kind") != "text") return null;
			var name = Field(items[i], "Text");
			var role = Field(items[i + 1], "Text");
			i += 2;
			var cells = new Dictionary<string, CoachCell>();
			while (i + 1 < items.Count && Field(items[i], "Kind") == "checkbox")
			{
				var box = items[i];
				var stars = items[i + 1];
				if (Field(stars, "Kind") != "stars" || Field(box, "Of") is not { Length: > 0 } of) return null;
				cells[of] = new CoachCell(Field(box, "Value") == "True", Field(box, "Enabled") == "True", Field(box, "Pointer"),
					int.TryParse(Field(stars, "Shown"), out var s) ? s : 0);
				i += 2;
			}
			if (CoachAreas.Any(a => !cells.ContainsKey(a.Record))) return null;
			rows.Add(new CoachRow(name, role, CoachAreas.Select(a => cells[a.Record]).ToArray()));
		}

		// Under the grid, each area's load and its lead coach's rating: from the page's record, which
		// names each area (the blocks that show them follow no heading the CLI can read in every language).
		// The rating is the game's own scale; a star control on the page showing the same value says
		// how many stars it is.
		JsonElement figures;
		try { figures = await Fm.GetJson("coachareas"); }
		catch (FmError) { return null; }
		var byArea = figures.EnumerateArray().ToDictionary(f => Field(f, "Area"));
		if (CoachAreas.Any(a => !byArea.ContainsKey(a.Record))) return null;
		var shown = items.Where(x => Field(x, "Kind") == "stars").GroupBy(x => Field(x, "Value"))
			.ToDictionary(g => g.Key, g => int.TryParse(Field(g.First(), "Shown"), out var h) ? h : 0);
		var areas = CoachAreas.Select(a =>
		{
			var area = byArea[a.Record];
			var load = int.TryParse(Field(area, "Workload"), out var w) ? WorkloadWord(w) : "";
			return (load, shown.TryGetValue(Field(area, "Lead"), out var half) ? half : 0);
		}).ToList();
		return (rows, areas);
	}
}
