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
			["top"] = ("Priority-Tab", L("Top")), ["standard"] = ("Standard-Tab", L("Standard")),
			["ongoing"] = ("Ongoing-Tab", L("Ongoing")), ["standart"] = ("Standard-Tab", L("Standard")),
			["surekli"] = ("Ongoing-Tab", L("Ongoing")),
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
		// The club's focuses by record, read from its data (a form left open does not hide them).
		var known = await FocusIds();
		Focus editing = null;
		if (edit)
		{
			if (int.TryParse(which, out var number) && number >= 1 && number <= existing.Count) editing = existing[number - 1];
			else if (Choose(existing.Select(f => f.Name).Distinct().ToList(), which, L("Focus")) is { } wanted) editing = existing.First(f => f.Name == wanted);
			else return 64;
			if (await FocusAt(editing.Index) is not { } shown || shown.Id != editing.Id
				|| ((await EditButton(editing.Id) is { } pencil ? await Fm.Get("click", ("ptr", pencil)) : null) ?? "error").StartsWith("error", StringComparison.Ordinal)
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
			// "Any Position" by the field it writes; the pitch's position buttons by the event they
			// send (each hands the game its position's id); the role list by the field it writes.
			var form = await FocusForm("Next");
			if (FormControl(form, "checkbox", "anyposition") is { } any && Field(any, "Value") == "True")
				await Fm.Get("setcontrol", ("ptr", Field(any, "Pointer")), ("value", "false"));
			List<JsonElement> spots = [];
			await Until(async () => (spots = await PositionButtons()).Any(b => Field(b, "Enabled") == "True"), seconds: 5, every: 0.4);
			var labels = spots.Where(b => Field(b, "Enabled") == "True").Select(b => Field(b, "Label")).ToList();
			await GameWords.Ask(Fm, labels.Distinct().ToArray());
			if (FocusPosition(labels, position) is not { } label)
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"Position: no '{position}'. Options: {string.Join(" · ", labels.Distinct())}"));
				return 64;
			}
			await Fm.Get("click", ("ptr", Field(spots.First(b => Field(b, "Label") == label && Field(b, "Enabled") == "True"), "Pointer")));
			// Step one is still on screen and the game has taken the position: the step's own
			// button, which was offering nothing, is open (FocusSteps + the form read).
			await Until(async () => (form = await FocusForm("Next")).Count > 0 && await FormStep() == "next", seconds: 5, every: 0.4);
			chosen.Add(label);
			if (role != null)
			{
				var roles = FormControl(form, "dropdown", "rolevalue");
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
			if (await FocusArea(pointer, one) is not { } place)
			{
				await AbandonFocus();
				return 64;
			}
			// In once the focus counts one area more (its own AreaCount).
			var before = await FocusAreaCount();
			var said = await Fm.Get("select", ("ptr", pointer), ("path", place.Path), ("fire", "1"));
			if (said.StartsWith("error", StringComparison.Ordinal) || !await Until(async () => await FocusAreaCount() is { } now && now != before, seconds: 5, every: 0.4))
			{
				await AbandonFocus();
				Console.Error.WriteLine(L($"The area could not be added: {place.Text}{(said.StartsWith("error", StringComparison.Ordinal) ? " — " + said : "")}"));
				return 1;
			}
			chosen.Add(place.Text);
		}
		foreach (var (current, typed) in new[] { (true, ability), (false, potential) })
		{
			if (typed == null) continue;
			if (!await SetStars(current, StarSteps(typed)!.Value))
			{
				await AbandonFocus();
				Console.Error.WriteLine(current ? L($"Minimum ability: could not be set to {typed} stars.") : L($"Minimum potential: could not be set to {typed} stars."));
				return 1;
			}
			chosen.Add($"{(current ? L("ability") : L("potential"))} ≥ {StarSteps(typed)!.Value / 2.0:0.#}★");
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
		// FM27 asks once more ("confirm this Focus?"), a window known by the event it sends; FM26
		// took the form down at once. Answered with the window's own primary answer.
		if (await Until(async () => (await Fm.GetJson("dialog")).EnumerateArray().Any(d => Field(d, "Type") == FocusConfirmDialog)
				|| await FormStep() == null, seconds: 6, every: 0.3)
			&& (await Fm.GetJson("dialog")).EnumerateArray().Any(d => Field(d, "Type") == FocusConfirmDialog))
		{
			await Fm.Get("dialog", ("press", "primary"));
			await Until(async () => !(await Fm.GetJson("dialog")).EnumerateArray().Any(d => Field(d, "Type") == FocusConfirmDialog), seconds: 5, every: 0.2);
		}
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
		List<string> fresh = [];
		var made = await Until(async () => await FormStep() == null && (fresh = (await FocusIds()).Except(known).ToList()).Count > 0, seconds: 12, every: 0.7);
		if (made) after = await ScoutFocuses();
		if (!made)
		{
			Console.Error.WriteLine(L("The focus does not look set up; look: scout"));
			return 1;
		}
		// The new one: the record whose id was not there before.
		var added = after.FirstOrDefault(f => fresh.Contains(f.Id));
		if (added == null)
		{
			Console.Error.WriteLine(L("The focus does not look set up; look: scout"));
			return 1;
		}
		Console.WriteLine(L($"Focus set up: {added.Name} · {tabWord}{(chosen.Count > 0 ? " · " + string.Join(" · ", chosen) : "")}{(ages != null ? L($" · aged {ages.Groups[1].Value}–{ages.Groups[2].Value}") : "")} · {added.Assigned}"));
		Console.WriteLine(L($"To see what it finds: scout \"{added.Name}\""));
		return 0;
	}

	private static async Task<List<Focus>> ScoutFocuses()
	{
		var focuses = new List<Focus>();
		JsonElement? list = null;
		if (!await Until(async () => (list = await FocusList()) != null, seconds: 8, every: 0.5)) return focuses;
		var ids = await FocusIds();
		for (var index = 0; index < list!.Value.GetProperty("Choices").GetArrayLength(); index++)
			if (await FocusAt(index, ids) is { } focus) focuses.Add(focus);
		await GameWords.Ask(Fm, focuses.Select(f => f.Name).Distinct().ToArray());
		return focuses;
	}

	/// <summary>The window the game asks "complete this focus?" in, known by its type.</summary>
	private const string RemoveDialog = "RecruitmentFocusRemove";

	/// <summary>
	/// Stopping a focus: FM27 calls it completing it, and it leaves the list of focuses in
	/// progress for the completed ones.
	/// </summary>
	private static async Task<int> ScoutRemove(string[] args)
	{
		var restart = args[1] is "restart" or "yeniden";
		var words = Words(args).Skip(1).ToList();
		if (restart) return await ScoutRestart(words);
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: scout remove <#|name> [--yes]   (stops the focus)"));
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
		else if (Choose(focuses.Select(f => f.Name).Distinct().ToList(), string.Join(" ", words), L("Focus")) is { } name) wanted = focuses.First(f => f.Name == name);
		else return 64;

		var go = args.Contains("--yes") || (Ui.Interactive && Ui.Ask(L($"Stop the focus '{wanted.Name}'? ({wanted.Matched} matching, {wanted.Near} near)"), [L("Yes"), L("No")]) == 0);
		if (!go)
		{
			if (!Ui.Interactive) Console.WriteLine(L($"'{wanted.Name}' ({wanted.Matched} matching, {wanted.Near} near) is taken off the scouting priorities. To confirm: scout remove {wanted.Index + 1} --yes"));
			return 0;
		}
		// The row's remove button, by the focus beside it; the game asks in a window of its own type.
		if (await RemoveButton(wanted.Id) is not { } bin)
		{
			Console.Error.WriteLine(L($"The game does not offer removing '{wanted.Name}': there is no remove button on the focus page or in the assignments panel."));
			return 1;
		}
		await Fm.Get("click", ("ptr", bin));
		if (!await Until(async () => (await Fm.GetJson("dialog")).EnumerateArray().Any(d => Field(d, "Type") == RemoveDialog), seconds: 5, every: 0.3))
		{
			Console.Error.WriteLine(L("The game did not ask about stopping; look: scout"));
			return 1;
		}
		await Fm.Get("dialog", ("press", "primary"));
		// Stopped once its record has left the club's focuses in progress.
		var stopped = await Until(async () => (await FocusIds()).All(id => id != wanted.Id), seconds: 10, every: 0.5);
		if (!stopped)
		{
			Console.Error.WriteLine(L($"'{wanted.Name}' still looks running; look: scout"));
			return 1;
		}
		Console.WriteLine(L($"Focus stopped: {wanted.Name}  (the game moves it to the completed focuses)"));
		return 0;
	}

	/// <summary>
	/// Sets a completed focus going again: the game makes a new focus in progress of it and keeps
	/// the completed one. The page's two lists are one data list whose kind the tabs set
	/// (ScoutingInfo.RecruitmentFocusesListType: 1 in progress, 2 completed); each completed row's
	/// restart button sends its focus's id, and is found by that focus beside it.
	/// </summary>
	private static async Task<int> ScoutRestart(List<string> words)
	{
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: scout restart <#|name>   (restarts a stopped or finished focus)"));
			return 64;
		}
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentFocuses);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		async Task<bool> Showing(string tab, string kind)
		{
			if (Field(await Fm.Pull("Human.ScoutingInfo", "RecruitmentFocusesListType"), "RecruitmentFocusesListType") == kind) return true;
			var button = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == tab && Field(b, "Enabled") == "True");
			if (button.ValueKind != JsonValueKind.Object) return false;
			await Fm.Get("click", ("ptr", Field(button, "Pointer")));
			return await Until(async () => Field(await Fm.Pull("Human.ScoutingInfo", "RecruitmentFocusesListType"), "RecruitmentFocusesListType") == kind, seconds: 5, every: 0.3);
		}
		if (!await Showing("ButtonInProgress", "1")) return Report("error: the focus page's lists did not answer");
		var known = await FocusIds();
		if (!await Showing("ButtonCompleted", "2")) return Report("error: the completed focuses did not open");

		// The completed ones, from their rows' restart buttons and the focus beside each.
		List<(string Pointer, string Name, string Id)> done = [];
		await Until(async () =>
		{
			done = [];
			foreach (var b in (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				         .Where(b => Field(b, "Events").Contains("EventFmxRecruitmentFocusRestart", StringComparison.Ordinal) && Field(b, "Enabled") == "True" && Field(b, "Source").Length > 0))
			{
				var focus = await Fm.Pull($"{Field(b, "Source")}.focusitem", "Name,UniqueId", shown: "Name");
				if (Field(focus, "Name").Length > 0) done.Add((Field(b, "Pointer"), Field(focus, "Name"), Field(focus, "UniqueId")));
			}
			return done.Count > 0;
		}, seconds: 6, every: 0.5);
		if (done.Count == 0)
		{
			await Showing("ButtonInProgress", "1");
			Console.WriteLine(L("No completed focuses to restart."));
			return 0;
		}
		await GameWords.Ask(Fm, done.Select(d => d.Name).Distinct().ToArray());
		(string Pointer, string Name, string Id) wanted;
		if (int.TryParse(words[0], out var number) && number >= 1 && number <= done.Count) wanted = done[number - 1];
		else if (Choose(done.Select(d => d.Name).Distinct().ToList(), string.Join(" ", words), L("Focus")) is { } name) wanted = done.First(d => d.Name == name);
		else
		{
			await Showing("ButtonInProgress", "1");
			return 64;
		}
		await Fm.Get("click", ("ptr", wanted.Pointer));
		// Restarted once a new record is among the focuses in progress.
		await Showing("ButtonInProgress", "1");
		if (!await Until(async () => (await FocusIds()).Except(known).Any(), seconds: 10, every: 0.5))
		{
			Console.Error.WriteLine(L($"'{wanted.Name}' does not look restarted; look: scout"));
			return 1;
		}
		Console.WriteLine(L($"Focus restarted: {wanted.Name}  (the old one stays in the list as stopped)"));
		return 0;
	}
}
