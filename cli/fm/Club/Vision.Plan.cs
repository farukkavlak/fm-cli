using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The meeting's 5 Year Plan tab: each season of the plan with its objectives, and adding an objective.
///
/// Held by identity only:
/// - the meeting's data, found by its node `visionobjectiveyear` (the season the plan shows) beside `selectedtab`;
/// - the tabs by the value each writes to `selectedtab`: the plan's is `Board-Vision-Button`'s, the objectives'
///   the other one, read back by the objectives' rows being drawn;
/// - a season's button by the value it writes to `visionobjectiveyear` (`const.Three` holds 3), its name by the
///   text whose data node (`Under`) has that ID;
/// - a season's objectives by the game's own list (`ClubVisionMeetingObjectivesForYear`), the rows drawn for it
///   by their data path inside `#FiveYearPlan`; read once the season is the one asked for and the rows are the list;
/// - the add list by the event it sends (EventFmxAddClubVisionObjective), its entries as the game's menu lists them;
///   an add read back by the edit session's `CanUndo` or the season's list changing.
/// A weight is shown, not changed: the game has no event for it (the stepper beside `#ImportanceText` only draws it).
/// </summary>
internal static partial class Program
{
	private const string VisionAddEvent = "EventFmxAddClubVisionObjective";
	private const string VisionYearPath = "Club.VisionObjectiveYearString";
	private const string VisionYearNode = "visionobjectiveyear";

	private sealed record VisionSeason(string Name, List<VisionGoal> Goals);

	/// <summary>The meeting page's own data path (where `visionobjectiveyear` sits), or null.</summary>
	private static async Task<string?> VisionData()
	{
		var found = await Fm.Get("find", ("name", VisionYearNode));
		var line = found.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("..", StringComparison.Ordinal) && l.EndsWith($".{VisionYearNode}", StringComparison.Ordinal));
		return line?[2..^(VisionYearNode.Length + 1)];
	}

	/// <summary>A number the game holds at a data path (`(DynamicNumber) 3`), or null.</summary>
	private static async Task<int?> NumberAt(string path)
	{
		var value = await Fm.Get("get", ("path", path));
		return Regex.Match(value, @"\(DynamicNumber\) (-?\d+)") is { Success: true } m ? int.Parse(m.Groups[1].Value) : null;
	}

	/// <summary>A datastore button's value: a node beside the meeting's data ("tab2") or a constant ("const.Three").</summary>
	private static Task<int?> WrittenValue(string data, string source) =>
		NumberAt(source.StartsWith("const.", StringComparison.Ordinal) ? source : $"{data}.{source}");

	/// <summary>The meeting's buttons that write one of its values, with the number each writes.</summary>
	private static async Task<List<(JsonElement Button, int Value)>> VisionWriters(string data, string destination)
	{
		var found = new List<(JsonElement, int)>();
		foreach (var b in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray())
		{
			var writes = Field(b, "Writes");
			if (!writes.StartsWith($"{destination}=", StringComparison.Ordinal)) continue;
			if (await WrittenValue(data, writes[(destination.Length + 1)..]) is { } value) found.Add((b.Clone(), value));
		}
		return found;
	}

	/// <summary>Shows the meeting's tab: the plan (true) or the objectives (false). False when it could not.</summary>
	private static async Task<bool> VisionTab(string data, bool plan)
	{
		var tabs = await VisionWriters(data, "selectedtab");
		var planTab = tabs.FirstOrDefault(t => Field(t.Button, "Name") == "Board-Vision-Button");
		if (planTab.Button.ValueKind != JsonValueKind.Object) return false;
		var (button, wanted) = plan ? planTab : tabs.FirstOrDefault(t => t.Value != planTab.Value);
		if (button.ValueKind != JsonValueKind.Object) return false;
		if (await NumberAt($"{data}.selectedtab") != wanted)
		{
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (!await Until(async () => await NumberAt($"{data}.selectedtab") == wanted, seconds: 5, every: 0.2)) return false;
		}
		// The tab is the one meant when its own rows are drawn whole: the seasons' buttons for the plan,
		// the objectives' rows (outside #FiveYearPlan) for the other.
		return plan
			? await Until(async () => (await VisionWriters(data, VisionYearNode)).Count > 0, seconds: 5, every: 0.2)
			: await Until(async () => await VisionGoals() is { Count: > 0 } goals && VisionDrawn(goals), seconds: 8, every: 0.3);
	}

	/// <summary>The plan's seasons in the game's order: the ID each button writes, its name as the game shows it.</summary>
	private static async Task<List<(JsonElement Button, int Id, string Name)>> VisionSeasons(string data)
	{
		var shown = await Fm.GetJson("shown", ("under", "Season-Selection"));
		var names = new Dictionary<int, string>();
		foreach (var t in shown.ValueKind == JsonValueKind.Array ? shown.EnumerateArray() : Enumerable.Empty<JsonElement>())
			if (Field(t, "Path") == VisionYearPath && Field(t, "Under") is { Length: > 0 } under && await NumberAt($"{under}.ID") is { } id)
				names[id] = Field(t, "Text");
		return (await VisionWriters(data, VisionYearNode))
			.Select(w => (w.Button, w.Value, names.GetValueOrDefault(w.Value, ""))).ToList();
	}

	/// <summary>The game's list of objectives for the season the plan shows: each one's text, or "" while it is being filled.</summary>
	private static async Task<List<string>?> VisionYearList(string data)
	{
		var list = await Fm.GetJson("pullbound", ("name", "ClubVisionMeetingObjectivesForYear"), ("under", data),
			("props", "ClubVisionMeetingObjectiveIdentifier.DisplayValue"), ("gen", Environment.TickCount64.ToString()));
		return list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(e => Field(e, "ClubVisionMeetingObjectiveIdentifier.DisplayValue")).ToList() : null;
	}

	/// <summary>
	/// One season's objectives: shown by its button unless it already is, read when the meeting shows that
	/// season, the game's list for it is filled, and the rows drawn are that list. Null when they never agree.
	/// </summary>
	private static async Task<List<VisionGoal>?> VisionSeasonGoals(string data, JsonElement button, int id)
	{
		if (await NumberAt($"{data}.{VisionYearNode}") != id)
		{
			await Fm.Get("press", ("ptr", Field(button, "Pointer")));
			if (!await Until(async () => await NumberAt($"{data}.{VisionYearNode}") == id, seconds: 5, every: 0.1)) return null;
		}
		List<VisionGoal> goals = [];
		var agree = await Until(async () =>
		{
			if (await VisionYearList(data) is not { } list || list.Any(t => t.Length == 0)) return false;
			goals = await VisionGoals(plan: true);
			return VisionDrawn(goals) && goals.Select(g => g.Text).Order().SequenceEqual(list.Order());
		}, seconds: 5, every: 0.2);
		return agree ? goals : null;
	}

	/// <summary>Every season of the plan with its objectives; leaves the plan's tab showing. Null when it could not be read.</summary>
	private static async Task<List<VisionSeason>?> VisionPlan(string data)
	{
		if (!await VisionTab(data, plan: true)) return null;
		var seasons = new List<VisionSeason>();
		foreach (var (button, id, name) in await VisionSeasons(data))
		{
			if (await VisionSeasonGoals(data, button, id) is not { } goals) return null;
			seasons.Add(new VisionSeason(name, goals));
		}
		return seasons.Count > 0 ? seasons : null;
	}

	/// <summary>The add list's entries, as the game's menu lists them: (menu path, group, objective).</summary>
	private static async Task<(string Pointer, List<(string Path, string Group, string Text)> Entries)?> VisionAddable()
	{
		var list = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => VisionSends(b, VisionAddEvent));
		if (list.ValueKind != JsonValueKind.Object) return null;
		var pointer = Field(list, "Pointer");
		if (Field(list, "Enabled") != "True") return (pointer, []);
		var tree = await Fm.GetJson("menu", ("ptr", pointer), ("tree", "1"));
		if (tree.ValueKind != JsonValueKind.Array) return (pointer, []);
		var groups = tree.EnumerateArray().Where(r => r.GetProperty("Folder").GetBoolean()).ToDictionary(r => Field(r, "Path"), r => Field(r, "Text"));
		var entries = tree.EnumerateArray().Where(r => !r.GetProperty("Folder").GetBoolean())
			.Select(r => (Field(r, "Path"), groups.GetValueOrDefault(Field(r, "Path") is var p && p.Contains(',') ? p[..p.LastIndexOf(',')] : "", ""), Field(r, "Text")))
			.ToList();
		return (pointer, entries);
	}

	private static async Task<int> VisionPlanCommand(bool json, List<string> words)
	{
		if (await VisionData() is not { } data)
		{
			Console.Error.WriteLine(L("The meeting's plan could not be found on screen."));
			return 1;
		}
		var plan = await VisionPlan(data);
		if (plan == null)
		{
			Console.Error.WriteLine(L("The meeting's 5 Year Plan could not be read."));
			return 1;
		}
		var addable = await VisionAddable();
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Seasons = plan.Select(s => new { Season = s.Name, Goals = s.Goals.Select(g => new { g.Text, g.Weight, g.Importance, Required = !g.Removable }) }),
				CanAdd = addable?.Entries.Select((e, i) => new { No = i + 1, e.Group, e.Text }) ?? Enumerable.Empty<object>(),
			}));
			return 0;
		}
		Console.WriteLine(L("Club vision: the 5 Year Plan"));
		foreach (var season in plan)
		{
			Console.WriteLine($"\n{season.Name}");
			if (season.Goals.Count == 0) Console.WriteLine(L("  (no objective)"));
			foreach (var goal in season.Goals)
				Console.WriteLine($"  {goal.Text}{(goal.Weight.Length > 0 ? $" — {goal.Weight}" : "")}{(goal.Removable ? "" : L(" (cannot be removed)"))}");
		}
		if (addable is { Entries.Count: > 0 } add)
		{
			Console.WriteLine(L("\nObjectives that can be added:"));
			for (var i = 0; i < add.Entries.Count; i++)
				Console.WriteLine($"  {i + 1}  {(add.Entries[i].Group.Length > 0 ? $"{add.Entries[i].Group} › " : "")}{add.Entries[i].Text}");
			Console.WriteLine(L("To add one: vision add <no>"));
		}
		else Console.WriteLine(L("\nThe board offers no objective to add."));
		return 0;
	}

	private static async Task<int> VisionAdd(List<string> words)
	{
		// The add list opens once the plan shows a season: the plan is read first, which shows each in turn.
		if (await VisionData() is not { } data || await VisionPlan(data) == null)
		{
			Console.Error.WriteLine(L("The meeting's 5 Year Plan could not be read."));
			return 1;
		}
		if (await VisionAddable() is not { } addable || addable.Entries.Count == 0)
		{
			Console.Error.WriteLine(L("The board offers no objective to add."));
			return 1;
		}
		var wanted = words.ElementAtOrDefault(1) ?? "";
		var index = int.TryParse(wanted, out var n) && n >= 1 && n <= addable.Entries.Count ? n - 1
			: wanted.Length > 0 ? addable.Entries.FindIndex(e => e.Text.Contains(wanted, StringComparison.OrdinalIgnoreCase)) : -1;
		if (index < 0)
		{
			Console.Error.WriteLine(L("usage: vision add <no | part of the objective>   (for the numbers: vision plan)"));
			return 64;
		}
		var entry = addable.Entries[index];
		async Task<bool> CanUndo() => Field(await Fm.Pull($"{data}.editsession", "CanUndo"), "CanUndo") == "True";
		var undoBefore = await CanUndo();
		var listBefore = string.Join("\n", await VisionYearList(data) ?? []);
		// The entry is chosen into the list's binding, then the list sends its event with it.
		var chosen = await Fm.Get("select", ("ptr", addable.Pointer), ("path", entry.Path));
		if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		var sent = await Fm.Get("press", ("ptr", addable.Pointer));
		if (sent.StartsWith("error", StringComparison.Ordinal)) return Report(sent);
		var took = await Until(async () => (!undoBefore && await CanUndo())
			|| (await VisionYearList(data) is { } now && !now.Any(t => t.Length == 0) && string.Join("\n", now) != listBefore), seconds: 5, every: 0.2);
		if (!took)
		{
			Console.Error.WriteLine(L($"The game did not add '{entry.Text}'; for the plan as it stands: vision plan"));
			return 1;
		}
		Console.WriteLine(L($"Added: {entry.Text}. To put it to the board: vision suggest   ·   to undo: vision undo   ·   the plan: vision plan"));
		return 0;
	}
}
