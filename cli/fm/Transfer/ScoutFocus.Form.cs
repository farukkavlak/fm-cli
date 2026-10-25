using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> ScoutNewForm(string[] args)
	{
		string position = Option(args, "--position"), role = Option(args, "--role"), name = Option(args, "--name"),
			age = Option(args, "--age"), area = Option(args, "--area"), priority = Option(args, "--priority"),
			ability = Option(args, "--ability"), potential = Option(args, "--potential"), by = Option(args, "--by"), analysts = Option(args, "--analysts");
		// `scout edit <#|ad>` goes through the same form, opened on a focus there already; what is
		// not named is left as it stands.
		var edit = args[1] is "edit" or "duzenle";
		var which = string.Join(" ", Words(args).Skip(1));
		if (edit && (which.Length == 0 || (area ?? age ?? name ?? priority ?? ability ?? potential ?? by ?? analysts) == null || position != null || role != null))
		{
			Console.Error.WriteLine(L("usage: scout edit <#|name> [--area \"<area to add>, …\"] [--age 18-23] [--ability <stars>] [--potential <stars>] [--by \"<scout>, …\"] [--analysts \"<analyst>, …\"] [--name <name>] [--priority top|standard|ongoing]\n") +
				L("  the position and role do not change here; for that set up a new focus: scout new"));
			return 64;
		}
		if (!edit && position == null && area == null)
		{
			Console.Error.WriteLine(L("usage: scout new [--position <position>] [--role <role>] [--area \"<area>[, <area>…]\"] [--age 18-23] [--ability <stars>] [--potential <stars>] [--by \"<scout>[, <scout>…]\"] [--analysts \"<analyst>[, …]\"] [--name <name>] --priority top|standard|ongoing\n") +
				L("  at least one position or area is needed. Areas: scout areas [path]   e.g. --area \"Turkey, Region>Europe>Northern Europe\""));
			return 64;
		}
		if (name is { Length: > 25 })
		{
			Console.Error.WriteLine(L("A focus name can be at most 25 letters."));
			return 64;
		}
		var ages = age == null ? null : Regex.Match(age, @"^(\d{2})\s*-\s*(\d{2})$");
		if (ages is { Success: false })
		{
			Console.Error.WriteLine(L("Age range: --age 18-23"));
			return 64;
		}
		foreach (var (flag, typed) in new[] { ("--ability", ability), ("--potential", potential) })
			if (typed != null && StarSteps(typed) == null)
			{
				Console.Error.WriteLine(L($"{flag}: a number from 0.5 to 5 in half-star steps (e.g. {flag} 3.5)."));
				return 64;
			}
		// The priority tabs as the game names them; what is written on them is the game's word
		// ("Obscure" for the ongoing one), so it is only ever shown, never matched.
		var tabs = new Dictionary<string, (string Tab, string Word)>
		{
			["top"] = ("Priority-Tab", "Top"), ["standard"] = ("Standard-Tab", "Standard"),
			["ongoing"] = ("Ongoing-Tab", "Ongoing"), ["standart"] = ("Standard-Tab", "Standard"),
			["surekli"] = ("Ongoing-Tab", "Ongoing"),
		};
		if (priority != null && !tabs.ContainsKey(Fold(priority)))
		{
			Console.Error.WriteLine(L("Priority: --priority top | standard | ongoing"));
			return 64;
		}
		if (priority == null && !edit)
		{
			if (Ui.Ask(L("The focus's priority?"), [L("Top — at once, with two people"), L("Standard — one scout, slower"), L("Ongoing — continuous")]) is not { } asked)
			{
				if (!Ui.Interactive) Console.Error.WriteLine(L("No priority given: --priority top | standard | ongoing"));
				return 64;
			}
			priority = new[] { "top", "standard", "ongoing" }[asked];
		}

		if (await NotNow() is { } refused) return refused;
		// The focuses as they stand, so the one this makes can be told from them afterwards.
		var page = await OpenPage(Screen.RecruitmentFocuses);
		if (page.StartsWith("error", StringComparison.Ordinal)) return Report(page);
		var existing = await ScoutFocuses();
		Focus editing = null;
		if (edit)
		{
			if (int.TryParse(which, out var number) && number >= 1 && number <= existing.Count) editing = existing[number - 1];
			else if (Choose(existing.Select(f => f.Name).Distinct().ToList(), which, L("Focus")) is { } wanted) editing = existing.First(f => f.Name == wanted);
			else return 64;
			if (await FocusAt(editing.Index) is not { } shown || shown.Name != editing.Name
				|| ((await ClickOptional(EditFocusIcon, seconds: 5)) ?? "").StartsWith("error", StringComparison.Ordinal)
				|| !await Until(async () => await FormStep() == "next", seconds: 8, every: 0.5))
			{
				Console.Error.WriteLine(L($"The game did not open editing for '{editing.Name}'."));
				return 1;
			}
		}
		else if (!await OpenFocusForm()) return 1;

		// Step one: the position, and the role in it.
		var chosen = new List<string>();
		if (position != null)
		{
			var form = await FocusForm("Next");
			if (form.FirstOrDefault(i => Field(i, "Kind") == "checkbox") is { ValueKind: JsonValueKind.Object } any && Field(any, "Value") == "True")
				await Fm.Get("setcontrol", ("ptr", Field(any, "Pointer")), ("value", "false"));
			await Until(async () => (form = await FocusForm("Next")).Any(i => Field(i, "Text") == "GK" && Field(i, "Enabled") == "True"), seconds: 5, every: 0.4);
			var labels = form.Where(i => Field(i, "Kind") == "button" && Regex.IsMatch(Field(i, "Text"), @"^(GK|DM|[A-Z]{1,2} \([A-Z]+\))$")).Select(i => Field(i, "Text")).ToList();
			if (FocusPosition(labels, position) is not { } label)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"Position: no '{position}'. Options: {string.Join(" · ", labels.Distinct())}"));
				return 64;
			}
			await Fm.Get("click", ("ptr", Field(FormButton(form, label)!.Value, "Pointer")));
			// Step one is still on screen and the game has taken the position: the step's own
			// button, which was offering nothing, is open (FocusSteps + the form read).
			await Until(async () => (form = await FocusForm("Next")).Count > 0 && await FormStep() == "next", seconds: 5, every: 0.4);
			chosen.Add(label);
			if (role != null)
			{
				var roles = FormAfter(form, "Role (In Possession)", "dropdown");
				var choices = roles?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
				if (Choose(choices, role, L("Role")) is not { } picked)
				{
					await AbandonFocus();
					return 64;
				}
				await Fm.Get("select", ("ptr", Field(roles!.Value, "Pointer")), ("choice", picked), ("fire", "1"));
				chosen.Add(picked);
			}
		}
		else if (role != null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("A role is chosen together with a position: --position <position> --role <role>"));
			return 64;
		}
		// Onto the second step: the step's own button going forward — the event and the payload it
		// sends say which way it goes, the word on it does not (FocusSteps).
		var (stepOn, _) = await FocusSteps();
		if (stepOn == null)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Next step of the form not found."));
			return 1;
		}
		await Fm.Get("click", ("ptr", stepOn));

		// Step two: name, ages, areas, priority. That it is on screen is the step buttons' own
		// answer — the one going back is there, the one going on is not (FocusSteps) — with the
		// form read taken in the same breath so it is never read half drawn.
		List<JsonElement> second = [];
		if (!await Until(async () => (second = await FocusForm("Back")).Count > 0 && await FormStep() == "back", seconds: 8, every: 0.5))
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("The form's second step did not open."));
			return 1;
		}
		if (name != null)
		{
			var box = (await Fm.GetJson("textboxes")).EnumerateArray().FirstOrDefault(b => Field(b, "Enabled") == "True");
			if (box.ValueKind == JsonValueKind.Object) await Fm.Get("type", ("ptr", Field(box, "Pointer")), ("text", name));
		}
		if (ages != null)
		{
			// The lists are told by the paths they write (the lower one narrows the upper's
			// choices), and each answer is the list showing the age just chosen — the game's own
			// confirmation that the click landed, not a pause long enough for it.
			foreach (var (binds, value) in new[] { ("minagevalue", ages.Groups[1].Value), ("maxagevalue", ages.Groups[2].Value) })
			{
				second = await FocusForm("Back");
				var list = FormControl(second, "dropdown", binds);
				if (list is not { } control || !control.GetProperty("Choices").EnumerateArray().Any(c => c.GetString() == value))
				{
					await AbandonFocus();
					Console.Error.WriteLine(L($"Age: {value} cannot be chosen (between 15 and 50, the lower bound below the upper)."));
					return 64;
				}
				await Fm.Get("select", ("ptr", Field(control, "Pointer")), ("choice", value!), ("fire", "1"));
				if (!await Until(async () => FormControl(await FocusForm("Back"), "dropdown", binds) is { } now && FormShown(now) == value, seconds: 4, every: 0.2))
				{
					await AbandonFocus();
					Console.Error.WriteLine(L($"Age: {value} selected but the list does not show it."));
					return 1;
				}
			}
		}
		foreach (var one in (area ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
		{
			second = await FocusForm("Back");
			if (FormBound(second, "possiblescoutingareasdropdownselectedoption") is not { } list)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L("Region list not found in the focus form."));
				return 1;
			}
			var pointer = Field(list, "Pointer");
			if (await FocusArea(pointer, one) is not { } path)
			{
				await AbandonFocus();
				return 64;
			}
			var before = FormHeadingBefore(second, "possiblescoutingareasdropdownselectedoption");
			var said = await Fm.Get("select", ("ptr", pointer), ("names", path), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal) || !await Until(async () =>
				FormHeadingBefore(await FocusForm("Back"), "possiblescoutingareasdropdownselectedoption") is { Length: > 0 } now && now != before, seconds: 5, every: 0.4))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"The area could not be added: {path.Replace(">", " › ")}{(said.StartsWith("error", StringComparison.Ordinal) ? " — " + said : "")}"));
				return 1;
			}
			chosen.Add(path.Split('>')[^1]);
		}
		foreach (var (heading, typed) in new[] { ("Minimum Current Ability", ability), ("Minimum Potential Ability", potential) })
		{
			if (typed == null) continue;
			if (!await SetStars(heading, StarSteps(typed)!.Value))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"{heading}: could not be set to {typed} stars."));
				return 1;
			}
			chosen.Add($"{(heading.Contains("Current") ? L("ability") : L("potential"))} ≥ {StarSteps(typed)!.Value / 2.0:0.#}★");
		}
		var (tabName, tabWord) = priority == null ? (null, null) : tabs[Fold(priority)];
		if (tabName != null)
		{
			second = await FocusForm("Back");
			// The button itself: the click is queued in the game's own order, and Confirm is
			// queued after it, so nothing has to be waited out between the two.
			if (second.FirstOrDefault(i => Field(i, "Kind") == "button" && Field(i, "Name") == tabName) is not { ValueKind: JsonValueKind.Object } tabButton)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"No {tabWord} priority tab in the focus form."));
				return 1;
			}
			await Fm.Get("click", ("ptr", Field(tabButton, "Pointer")));
			if (edit) chosen.Add(tabWord);
		}
		if (by != null)
		{
			if (await AssignFocusScouts(by) is not { } scouts)
			{
				await AbandonFocus();
				return 64;
			}
			chosen.Add(string.Join(" + ", scouts));
		}
		if (analysts != null)
		{
			if (await AssignFocusScouts(analysts, analysts: true) is not { } picked)
			{
				await AbandonFocus();
				return 64;
			}
			chosen.Add(L($"analyst: {string.Join(" + ", picked)}"));
		}

		// The form's Confirm, as the game draws it and the event it sends: its word is the game's,
		// and "Confirm" is a word this language's table also puts on other buttons (the dialog that
		// follows draws its own primary differently). Pressed by its pointer, once it is enabled;
		// there is nothing honest to press if it is not there.
		var formConfirm = (await Fm.GetJson("buttons", ("json", "1"), ("gen", "1"))).EnumerateArray()
			.FirstOrDefault(b => Field(b, "Name") == "button-primary-leading-icon-label"
				&& Field(b, "Events").Contains("RecruitmentFocusEditConfirmationDialog", StringComparison.Ordinal)
				&& Field(b, "Enabled") == "True");
		if (formConfirm.ValueKind != JsonValueKind.Object)
		{
			await AbandonFocus();
			Console.Error.WriteLine(L("Confirm button of the focus form not found."));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(formConfirm, "Pointer")));
		// The form is gone once neither step button is there any more (FormStep) — the game takes
		// the whole form down in the same instant it commits (measured: both the buttons and the
		// form read go empty 0.04–0.27s after Confirm, as the list grows by one).
		if (edit)
		{
			if (!await Until(async () => await FormStep() == null, seconds: 12, every: 0.7))
			{
				Console.Error.WriteLine(L("The form did not close; the change may not have gone through. Look: scout"));
				return 1;
			}
			if (ages != null) chosen.Add(L($"aged {ages.Groups[1].Value}–{ages.Groups[2].Value}"));
			Console.WriteLine(L($"Focus updated: {name ?? editing!.Name} · {string.Join(" · ", chosen)}\n(the game leaves the old one in the list as 'Complete')"));
			return 0;
		}
		// Made once the form is gone and the list of focuses is one longer.
		List<Focus> after = [];
		var made = await Until(async () => await FormStep() == null && (after = await ScoutFocuses()).Count > existing.Count, seconds: 12, every: 0.7);
		if (!made)
		{
			Console.Error.WriteLine(L("The focus does not look set up; look: scout"));
			return 1;
		}
		// The new one: running, and of a name there is now one more of.
		var added = after.FirstOrDefault(f => f.Running && after.Count(a => a.Name == f.Name) > existing.Count(e => e.Name == f.Name)) ?? after[^1];
		Console.WriteLine(L($"Focus set up: {added.Name} · {tabWord}{(chosen.Count > 0 ? " · " + string.Join(" · ", chosen) : "")}{(ages != null ? L($" · aged {ages.Groups[1].Value}–{ages.Groups[2].Value}") : "")} · {added.Assigned}"));
		Console.WriteLine(L($"To see what it finds: scout \"{added.Name}\""));
		return 0;
	}

	private static async Task<List<Focus>> ScoutFocuses()
	{
		var focuses = new List<Focus>();
		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 8, every: 0.5)) return focuses;
		for (var index = 0; index < list!.Value.GetProperty("Choices").GetArrayLength(); index++)
			if (await FocusAt(index) is { } focus) focuses.Add(focus);
		return focuses;
	}

	/// <summary>What the page's two buttons' actions send (InteropEventIDs): the game names them,
	/// so the button is found by that and not by what it says. The label is the game's own string
	/// and this language's table may not carry its English (§8.209).</summary>
	private const string RemoveEvent = "53";  // EventFmxRecruitmentFocusRemove

	private const string RestartEvent = "54"; // EventFmxRecruitmentFocusRestart

	/// <summary>
	/// The button on the recruitment-focus page whose action sends the given event, pressed by its
	/// pointer; an error line when this page does not offer it (a focus that is running has no
	/// Restart, one already stopped no Remove).
	/// </summary>
	private static async Task<string?> PressPageButton(string sent) =>
		await SpeakToActionEvent(sent) is { } button
			? await Fm.Get("click", ("ptr", Field(button, "Pointer")))
			: $"error: no button on the loaded screens sends the recruitment-focus event {sent}";

	/// <summary>
	/// Stopping a focus ("Remove Recruitment Focus": it leaves the scouting priorities but stays
	/// listed), or setting a stopped or finished one going again ("Restart": a new focus is made
	/// of it, the old entry kept).
	/// </summary>
	private static async Task<int> ScoutRemove(string[] args)
	{
		var restart = args[1] is "restart" or "yeniden";
		var words = Words(args).Skip(1).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(restart ? L("usage: scout restart <#|name>   (restarts a stopped or finished focus)") : L("usage: scout remove <#|name> [--yes]   (stops the focus)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var focuses = await ScoutFocuses();
		if (focuses.Count == 0)
		{
			Console.WriteLine(L("No scouting focuses."));
			return 0;
		}
		Focus wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= focuses.Count) wanted = focuses[number - 1];
		else
		{
			// Of two with a name (a stopped one and its restart) the one this can be done to is meant.
			var able = focuses.Where(f => f.Running != restart).ToList();
			if (Choose((able.Count > 0 ? able : focuses).Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), L("Focus")) is not { } name) return 64;
			wanted = (able.Count > 0 ? able : focuses).First(f => f.Name == name);
		}
		if (await FocusAt(wanted.Index) is not { } shown || shown.Name != wanted.Name)
		{
			Console.Error.WriteLine(L($"{wanted.Name}: the focus did not open."));
			return 1;
		}
		if (shown.Running == restart)
		{
			Console.Error.WriteLine(restart
				? shown.Status == "In Progress" ? L($"'{shown.Name}' is already running.") : L($"The game does not offer 'Restart Recruitment Focus' for '{shown.Name}' ({shown.Status}).")
				: L($"'{shown.Name}' is already stopped ({shown.State}). To restart it: scout restart {shown.Index + 1}"));
			return 1;
		}

		if (restart)
		{
			var started = await PressPageButton(RestartEvent);
			if ((started ?? "").StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"The game does not offer 'Restart Recruitment Focus' for '{shown.Name}'."));
				return 1;
			}
			if (!await Until(async () => await FocusList() is { } list && list.GetProperty("Choices").GetArrayLength() > focuses.Count, seconds: 8, every: 0.5))
			{
				Console.Error.WriteLine(L($"'{shown.Name}' does not look restarted; look: scout"));
				return 1;
			}
			Console.WriteLine(L($"Focus restarted: {shown.Name}  (the old one stays in the list as stopped)"));
			return 0;
		}

		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L($"Stop the focus '{shown.Name}'? ({shown.Matched} matching, {shown.Near} near)"), [L("Yes"), L("No")]) == 0);
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine(L($"'{shown.Name}' ({shown.Matched} matching, {shown.Near} near) is taken off the scouting priorities. To confirm: scout remove {shown.Index + 1} --yes"));
			return 0;
		}
		if (await Assignments() is not { } assignments)
		{
			Console.Error.WriteLine(L("The number of assignments could not be read on the page; look: scout"));
			return 1;
		}
		var clicked = await PressPageButton(RemoveEvent);
		// The page does not always have the button; the assignments panel lists
		// each scout's focuses with a remove icon beside them.
		if ((clicked ?? "").StartsWith("error", StringComparison.Ordinal) && !await RemoveInAssignments(shown.Name))
		{
			Console.Error.WriteLine(L($"The game does not offer removing '{shown.Name}': there is no remove button on the focus page or in the assignments panel."));
			return 1;
		}
		JsonElement dialogs = default;
		if (!await Until(async () => (dialogs = await Fm.GetJson("dialog")).GetArrayLength() > 0, seconds: 5, every: 0.4)
			|| DialogAnswer(dialogs[0], true) is not { } confirm)
		{
			Console.Error.WriteLine(L("The game did not ask about stopping; look: scout"));
			return 1;
		}
		await Fm.Get("click", ("ptr", Field(confirm, "Pointer")));
		// The page cannot be brought forward again while the confirmation is still up; it goes
		// as soon as the answer is taken.
		await Until(async () => (await Fm.GetJson("dialog")).GetArrayLength() == 0, seconds: 5, every: 0.2);
		await OpenPage(Screen.RecruitmentFocuses);
		// Stopped once the page counts one assignment fewer: the entry itself stays listed, and
		// until the day turns nothing on it says it has stopped.
		var stopped = await Until(async () => await Assignments() is { } now && now < assignments, seconds: 15, every: 0.7);
		if (!stopped)
		{
			Console.Error.WriteLine(L($"'{shown.Name}' still looks running; look: scout"));
			return 1;
		}
		Console.WriteLine(L($"Focus stopped: {shown.Name}  (it stays in the list; to restart: scout restart)"));
		return 0;
	}
}
