using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The scouting team as the assignments panel shows it: each scout's eye for ability and
	/// potential, how much of the world he knows, and the focuses he is working on.
	/// </summary>
	private static async Task<int> ScoutStaff(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement open = default;
		await Until(async () => (open = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Label").StartsWith("Assignments (", StringComparison.Ordinal))).ValueKind == JsonValueKind.Object, seconds: 8, every: 0.5);
		if (open.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The assignments panel was not found."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(open, "Pointer")));

		async Task<List<JsonElement>> Panel()
		{
			List<JsonElement> items = [];
			var last = "";
			await Until(async () =>
			{
				// The panel's heading is the game's word ("Active Assignments (4)"), translated with
				// the rest of the UI; the scouts it lists and the focuses it shows are bound by
				// their data paths in every language (§8.335). The staff list fills in over a
				// frame or two, so it is read until two reads name the same men.
				items = (await Fm.GetJson("texts", ("anchorpath", "Scout.Name"), ("up", "13"), ("paths", "1"))).EnumerateArray().ToList();
				var staff = string.Join("|", items.Where(i => Field(i, "Path") == "binding.Name").Select(Shown));
				var settled = staff.Length > 0 && staff == last && items.Any(i => Field(i, "Path") == "Scout.Name");
				last = staff;
				return settled;
			}, seconds: 6, every: 0.4);
			return items;
		}

		static string Shown(JsonElement item) => Field(item, "Text");
		var first = await Panel();
		var staff = new List<(string Name, string Job)>();
		for (var i = 0; i + 1 < first.Count; i++)
			if (Field(first[i], "Path") == "binding.Name" && Field(first[i + 1], "Path") == "binding.JobString"
				&& staff.All(m => m.Name != Shown(first[i])))
				staff.Add((Shown(first[i]), Shown(first[i + 1])));

		if (staff.Count == 0)
		{
			// Seen once after several focus edits in a session: the panel's open event never resolved
			// and only a restart brought it back (27 Ekim 2026). An empty list would read as no scouts.
			Console.Error.WriteLine(L("The assignments panel did not open. If it stays so: fm stop --save, then fm start."));
			return 1;
		}
		var rows = new List<(string Name, string Job, string Ability, string Potential, string Regions, string Nations, List<string> Focuses)>();
		foreach (var (name, job) in staff)
		{
			await Fm.Get("rowselect", ("text", name));
			List<JsonElement> panel = [];
			var last = "";
			// The right-hand side is his once the panel names him there and the focuses under
			// him have stopped changing: the name arrives a frame before the list does.
			await Until(async () =>
			{
				panel = await Panel();
				var at = panel.FindIndex(i => Field(i, "Path") == "Scout.Name" && Shown(i) == name);
				if (at < 0) { last = ""; return false; }
				var own = string.Join("|", panel.Skip(at).Where(i => Field(i, "Path") is "Scout.Name" or "focusitem.Name").Select(Shown));
				var settled = own == last;
				last = own;
				return settled;
			}, seconds: 5, every: 0.4);
			var at = panel.FindIndex(i => Field(i, "Path") == "Scout.Name" && Shown(i) == name);
			var ownItems = at >= 0 ? panel.Skip(at + 1).ToList() : [];
			var own = ownItems.Select(Shown).ToList();
			string After(string label) => own.IndexOf(label) is var i && i >= 0 && i + 1 < own.Count ? own[i + 1] : "";
			// "Regions (7)" and "Nations (13)" are the game's own words; Turkish shortens the
			// second to "Nation (13)", so only the part both languages share is matched.
			static string Count(string label) => Regex.Match(label ?? "", @"\((\d+)\)") is { Success: true } m ? m.Groups[1].Value : "-";
			var focuses = ownItems.Where(i => Field(i, "Path") == "focusitem.Name").Select(Shown).ToList();
			rows.Add((name, job, After("Judging Player Ability"), After("Judging Player Potential"),
				Count(own.FirstOrDefault(t => t.StartsWith("Region", StringComparison.Ordinal))), Count(own.FirstOrDefault(t => t.StartsWith("Nation", StringComparison.Ordinal))), focuses));
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, Job = GameWords.Of(r.Job), JudgingAbility = GameWords.Of(r.Ability), JudgingPotential = GameWords.Of(r.Potential), r.Regions, r.Nations, Focuses = r.Focuses.Select(GameWords.Of) })));
			return 0;
		}
		Table.Write([L("SCOUT"), L("JOB"), L("JUDGING ABILITY"), L("JUDGING POTENTIAL"), L("AREA"), L("NATION"), L("FOCUSES")],
			rows.Select(r => new[] { r.Name, r.Job, r.Ability, r.Potential, r.Regions, r.Nations, r.Focuses.Count > 0 ? string.Join(", ", r.Focuses) : "-" }).ToList());
		Console.WriteLine(L($"\n{(first.Count > 0 ? Shown(first[0]) : Field(open, "Label"))}. To send one to a player: scout player <name> --by <scout> --for <length>"));
		return 0;
	}

	/// <summary>The asset of the "Create a New Recruitment Focus" form (FM27), which its button opens.</summary>
	private const string CreateFocusPanel = "0ee5aea7e53ea5f45b841c12509bf108";

	/// <summary>
	/// A focus's edit button: one of the list's rows' buttons that open the focus form, the one
	/// whose row holds that focus's record (focusitem.UniqueId beside the button). An empty id is
	/// the new-focus button, the one with no focus beside it.
	/// </summary>
	private static Task<string?> EditButton(string id) => RowButton(b => Field(b, "Opens") == CreateFocusPanel, id);

	/// <summary>A focus row's remove button (it asks the game's "complete this focus?" window).</summary>
	private static Task<string?> RemoveButton(string id) => RowButton(b => Field(b, "Events") == "TriggerDialog", id);

	private static async Task<string?> RowButton(Func<JsonElement, bool> kind, string id)
	{
		var rows = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => kind(b) && Field(b, "Enabled") == "True" && Field(b, "Source").Length > 0).ToList();
		foreach (var row in rows)
		{
			string held;
			try { held = Field(await Fm.Pull($"{Field(row, "Source")}.focusitem", "UniqueId"), "UniqueId"); }
			catch (FmError) { held = ""; }
			if (held == id) return Field(row, "Pointer");
		}
		return null;
	}

	private static async Task<bool> OpenFocusForm(bool again = false)
	{
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return false; }
		// FM27's "Create New Focus" is a plain OpenPanel button, told from the page's others by the
		// panel it opens, and from the rows' edit buttons (the same panel) by having no focus beside
		// it; drawn a moment after the page, and clicked (its action is code).
		var clicked = "";
		await Until(async () => !(clicked = await EditButton("") is { } create
				? await Fm.Get("click", ("ptr", create))
				: "error: no button opens the new-focus form").StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L("The game will not open a new focus now: no new-focus button on the focus page or the tasks panel."));
			return false;
		}
		// A form left half-filled (a run that failed, a page opened over it) comes back as it was
		// left, on its second step: it is thrown away and a clean one opened.
		var leftover = false;
		(string Next, string Back) steps = default;
		if (await Until(async () => (steps = await FocusSteps()) is { Next: not null } || (leftover = steps.Back != null), seconds: 8, every: 0.5))
		{
			if (!leftover) return true;
			await AbandonFocus();
			// The form is gone once its step buttons are: the game takes it down with the
			// confirmation, so the reopening waits on that rather than on a pause.
			await Until(async () => await FormStep() == null, seconds: 6, every: 0.2);
			return !again && await OpenFocusForm(again: true);
		}
		Console.Error.WriteLine(L("The focus form did not open."));
		return false;
	}

	/// <summary>The areas a focus can be pointed at, one level of the form's list at a time.</summary>
	private static async Task<int> ScoutAreas(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (!await OpenFocusForm()) return 1;
		var (nextStep, _) = await FocusSteps();
		if (nextStep == null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Next step of the form not found."));
			return 1;
		}
		await Fm.Get("click", ("ptr", nextStep));
		List<JsonElement> form = [];
		await Until(async () => FormBound(form = await FocusForm("Back"), "possiblescoutingareasdropdownselectedoption") != null, seconds: 8, every: 0.5);
		if (FormBound(form, "possiblescoutingareasdropdownselectedoption") is not { } areas)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("The area list was not found."));
			return 1;
		}
		// One level of the list, walked by the texts as shown; the top level's entries may also be
		// named by their kind (Competition, Nation, Region, Continent), told by the records under them.
		var path = string.Join(" ", Words(args).Skip(1)).Replace(" > ", ">");
		var tree = await AreaTree(Field(areas, "Pointer"));
		await AbandonFocus();
		var parts = path.Split('>', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		AreaRow? at = null;
		if (parts.Length > 0 && (at = TopArea(tree, parts[0])) != null)
			foreach (var part in parts.Skip(1))
				at = Under(tree, at!.Path).FirstOrDefault(r => Fold(r.Text) == Fold(part));
		var list = parts.Length > 0 && at == null ? [] : Under(tree, at?.Path);
		if (json) Console.WriteLine(JsonOut.Of(list.Select(r => new { r.Text, r.Folder, Kind = r.Folder ? "" : r.Kind switch { ContinentKind => "continent", RegionKind => "region", NationKind => "nation", CompetitionKind => "competition", _ => "" } })));
		else if (list.Count == 0) Console.WriteLine(L($"There is nothing under '{path}'; the top of the list: scout areas"));
		else
		{
			foreach (var row in list) Console.WriteLine(row.Folder ? $"{row.Text} ›" : row.Text);
			Console.WriteLine(L($"\nThose ending in › have more under them: scout areas \"{(path.Length > 0 ? path + ">" : "")}<name>\""));
		}
		return 0;
	}

	private static async Task<int> ScoutNew(string[] args)
	{
		try { return await ScoutNewForm(args); }
		catch (FmError)
		{
			// Whatever went wrong, the form is not left open behind the next command.
			if (await FormStep() != null) await AbandonFocus();
			throw;
		}
	}
}
