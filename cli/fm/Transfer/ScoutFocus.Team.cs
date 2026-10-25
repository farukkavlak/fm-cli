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
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Job, JudgingAbility = r.Ability, JudgingPotential = r.Potential, r.Regions, r.Nations, r.Focuses })));
			return 0;
		}
		Table.Write([L("SCOUT"), L("JOB"), L("JUDGING ABILITY"), L("JUDGING POTENTIAL"), L("AREA"), L("NATION"), L("FOCUSES")],
			rows.Select(r => new[] { r.Name, r.Job, r.Ability, r.Potential, r.Regions, r.Nations, r.Focuses.Count > 0 ? string.Join(", ", r.Focuses) : "-" }).ToList());
		Console.WriteLine(L($"\n{(first.Count > 0 ? Shown(first[0]) : Field(open, "Label"))}. To send one to a player: scout player <name> --by <scout> --for <length>"));
		return 0;
	}

	/// <summary>How many assignments are running, as the focus page's "Assignments (n)" button counts them.</summary>
	private static async Task<int?> Assignments() =>
		(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Select(b => Regex.Match(Field(b, "Label"), @"^Assignments \((\d+)\)$"))
			.FirstOrDefault(m => m.Success) is { } found ? int.Parse(found.Groups[1].Value) : null;

	/// <summary>
	/// The menu icon the focus page's "open a new focus" action is drawn with. Its label is the
	/// game's words, and every one of these buttons carries ActionID 0, so the action reference's
	/// own menu icon is what tells it from the recommended, edit and remove buttons (§8.334).
	/// </summary>
	private const string CreateFocusIcon = "929682755";

	/// <summary>The menu icon of the action that opens the selected focus for editing.</summary>
	private const string EditFocusIcon = "464150106";

	/// <summary>
	/// The optional button whose action is drawn with this menu icon: the label is translated,
	/// the icon is not. These are the buttons of the messages panel; the same action is offered
	/// there from the focus page and from the assignments panel.
	/// </summary>
	private static async Task<JsonElement?> OptionalButton(string icon)
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Type") == "SIButton" && Field(b, "Name") == "StandardButton"
			            && Field(b, "Events") == "SpeakToAction" && Field(b, "Action").Length > 0 && Field(b, "Enabled") == "True")
			.ToList();
		if (buttons.Count == 0) return null;
		var icons = await PullEach(buttons.Select(b => (Field(b, "Action"), "MenuIconID")).ToList());
		return buttons.Zip(icons, (b, i) => (Button: b, Icon: Field(i, "MenuIconID")))
			.Where(t => t.Icon == icon).Select(t => (JsonElement?)t.Button).LastOrDefault();
	}

	/// <summary>Presses that optional button until it answers; a redrawn list leaves its pointer stale.</summary>
	private static async Task<string?> ClickOptional(string icon, double seconds = 8)
	{
		var answer = "";
		await Until(async () => !(answer = await OptionalButton(icon) is { } button
			? await Fm.Get("click", ("ptr", Field(button, "Pointer")))
			: $"error: no optional button carries the menu icon {icon}").StartsWith("error", StringComparison.Ordinal), seconds: seconds, every: 0.5);
		return answer;
	}

	private static async Task<bool> OpenFocusForm(bool again = false)
	{
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) { Report(opened); return false; }
		// The button is drawn a moment after the page; clicked, not queued — its action is code.
		var clicked = await ClickOptional(CreateFocusIcon);
		if ((clicked ?? "").StartsWith("error", StringComparison.Ordinal))
		{
			// The page does not always offer it; the assignments panel behind
			// "Assignments (n)" has the same create action and opens the same form.
			var assignments = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Label").StartsWith("Assignments (", StringComparison.Ordinal));
			if (assignments.ValueKind == JsonValueKind.Object)
			{
				await Fm.Get("click", ("ptr", Field(assignments, "Pointer")));
				clicked = await ClickOptional(CreateFocusIcon);
			}
		}
		if ((clicked ?? "").StartsWith("error", StringComparison.Ordinal))
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
		var path = string.Join(" ", Words(args).Skip(1)).Replace(" > ", ">");
		var list = path.Length == 0
			? areas.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList()
			: (await Fm.GetJson("menu", ("ptr", Field(areas, "Pointer")), ("names", path))) is { ValueKind: JsonValueKind.Array } under
				? under.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : [];
		await AbandonFocus();
		if (json) Console.WriteLine(JsonOut.Of(list));
		else if (list.Count == 0) Console.WriteLine(L($"There is nothing under '{path}' (write the path with English names: scout areas \"Nation>Europe\")."));
		else
		{
			foreach (var item in list) Console.WriteLine(item);
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
