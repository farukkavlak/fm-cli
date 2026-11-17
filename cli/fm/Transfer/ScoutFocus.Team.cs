using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>Scouting › Assignments (FM27): a subpage of the scouting page; it lists the scouting team.</summary>
	private const string AssignmentsPanel = "5ccd19d87218c4a40b10dd2f612884da";

	/// <summary>
	/// The scouting team as the assignments page lists it, each from his own record: his job, his
	/// eye for ability and potential (1–20), how many nations he knows, and how many focuses of each
	/// priority he is on. The page by its GUID (opened under its parent, not by a tile's button: the
	/// focus page's tiles can be hidden, and their button opens Scout Priorities), its list by its
	/// element's name.
	/// </summary>
	private static async Task<int> ScoutStaff(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(AssignmentsPanel);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<string> items = [];
		// The panel's list by the element names it sits in (a level or two above it, past unnamed ones).
		await Until(async () => (await Fm.GetJson("checkrows", ("gen", "staff"))).EnumerateArray()
				.FirstOrDefault(t => t.TryGetProperty("Names", out var names) && names.EnumerateArray().Any(n => n.GetString() == "RecruitmentScoutAssignmentsTable")) is { ValueKind: JsonValueKind.Object } list
			&& (items = list.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").Where(i => i.Length > 0).ToList()).Count > 0, seconds: 8, every: 0.4);
		if (items.Count == 0)
		{
			Console.Error.WriteLine(L("The assignments panel did not open. If it stays so: fm stop --save, then fm start."));
			return 1;
		}
		// The job is worded for the club it is at: the club, the human and his team beside the rows.
		var under = items[0][..items[0].LastIndexOf('.')];
		foreach (var (name, from) in new[] { ("club", "Human.Club"), ("human", "Human"), ("team", "Human.Team") })
			await Fm.Get("pathset", ("to", $"{under}.{name}"), ("from", from));
		const string focuses = "StaffRecruitmentFocuses";
		const string props = $"Name,JobString,JudgingPlayerAbility,JudgingPlayerPotential,ScoutingKnowledgeListCount,{focuses}.TopFocusesCount,{focuses}.StandardFocusesCount,{focuses}.OngoingFocusesCount";
		var people = await PullEach(items.Select(i => (i, props)).ToList(), p => Field(p, "Name").Length > 0 && Field(p, "JobString").Length > 0, shown: "Name,JobString");
		var rows = people.Where(p => Field(p, "Name").Length > 0).Select(p => (
			Name: Field(p, "Name"), Job: Field(p, "JobString"), Ability: Field(p, "JudgingPlayerAbility"), Potential: Field(p, "JudgingPlayerPotential"),
			Nations: Field(p, "ScoutingKnowledgeListCount"),
			Top: Field(p, $"{focuses}.TopFocusesCount"), Standard: Field(p, $"{focuses}.StandardFocusesCount"), Ongoing: Field(p, $"{focuses}.OngoingFocusesCount"))).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Job, JudgingAbility = r.Ability, JudgingPotential = r.Potential, r.Nations, Focuses = new { r.Top, r.Standard, r.Ongoing } })));
			return 0;
		}
		Table.Write([L("SCOUT"), L("JOB"), L("JUDGING ABILITY"), L("JUDGING POTENTIAL"), L("NATIONS KNOWN"), L("FOCUSES")],
			rows.Select(r => new[] { r.Name, r.Job, r.Ability, r.Potential, r.Nations, L($"{r.Top} top · {r.Standard} standard · {r.Ongoing} ongoing") }).ToList());
		Console.WriteLine(L("\nTo send one to a player: scout player <name> --by <scout> --for <length>"));
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
		await Until(async () => FormBound(form = await FocusForm(first: false), "possiblescoutingareasdropdownselectedoption") != null, seconds: 8, every: 0.5);
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
