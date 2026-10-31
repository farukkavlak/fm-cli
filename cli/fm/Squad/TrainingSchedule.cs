using System.Text.Json;

namespace FmCli;

/// <summary>
/// Changing the training schedule. Portal › Calendar has a Training tab that draws
/// the month with every day's three sessions as buttons and, ahead of each week, a card naming the
/// week's schedule ("Tactical", "Control Possession", "Tactical (Modified)"). A session's button
/// opens a picker — a category list and that category's sessions — confirmed with
/// EventFmxConfirmTrainingSession; the week's card opens the same kind of picker for whole
/// schedules (Training Style, Tactical Style, Scenario, Pre-Season › Early/Mid/Late), confirmed
/// with EventFmxConfirmTrainingSchedule. Past days and match days are drawn disabled.
/// </summary>
internal static partial class Program
{
	private sealed record ScheduleSlot(string Pointer, string Name, bool Enabled, bool Match = false);

	private sealed record ScheduleDay(DateTime Day, List<ScheduleSlot> Sessions, ScheduleSlot Week);

	private static readonly string[] WeekdayWords = ["pazar", "pazartesi", "sali", "carsamba", "persembe", "cuma", "cumartesi"];

	private static async Task<int> ScheduleChange(string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		// After "set" / "style"; a day typed in quotes ("14 ekim") is its words all the same.
		var words = Words(args).Skip(1).SelectMany(w => w.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToList();
		var style = args[1] is "style" or "stil";
		var today = await GameDate();

		if (words.Count == 0 || ScheduleDate(words, today, out var used) is not { } day)
		{
			Console.Error.WriteLine(style
				? L("usage: schedule style <day> [<schedule>]   (day: 14, \"14 october\", 2025-10-14, tomorrow, friday)")
				: L("usage: schedule set <day> <1|2|3> [<session>]   (day: 14, \"14 october\", 2025-10-14, tomorrow, friday)"));
			return 64;
		}
		words = words.Skip(used).ToList();
		if (!style && (words.Count == 0 || !int.TryParse(words[0], out var slot) || slot < 1 || slot > 3))
		{
			Console.Error.WriteLine(L("Which session? After the day, 1, 2 or 3 (in the day's order)."));
			return 64;
		}

		// FM27: Training › Overview's month, each session a button that opens the week's editor.
		if (!style && await ChangeSessionInWeek(day, int.Parse(words[0]), string.Join(" ", words.Skip(1))) is { } done) return done;

		var grid = await TrainingCalendar(day);
		if (grid == null) return 1;
		var found = grid.FirstOrDefault(d => d.Day == day);
		if (found == null)
		{
			Console.Error.WriteLine(L($"{DayName(day)} was not found in the calendar."));
			return 1;
		}
		return style ? await ChangeWeek(found, words, day) : await ChangeSession(found, words, day);
	}

	private static async Task<int> ChangeSession(ScheduleDay day, List<string> words, DateTime date)
	{
		var slot = int.Parse(words[0]);
		if (day.Sessions.Count < slot)
		{
			Console.Error.WriteLine(L($"Session {slot} of {DayName(date)} could not be read."));
			return 1;
		}
		var session = day.Sessions[slot - 1];
		if (session.Match)
		{
			Console.Error.WriteLine(L($"{DayName(date)}, session {slot} is the match itself ({session.Name}); it cannot be changed."));
			return 1;
		}
		if (!session.Enabled)
		{
			Console.Error.WriteLine(L($"{DayName(date)}, session {slot} ({session.Name}) cannot be changed: a past day, or a match/travel."));
			return 1;
		}
		var wanted = string.Join(" ", words.Skip(1));

		var picker = await OpenPicker(session.Pointer, "Physical");
		if (picker == null) return 1;
		var (menu, options) = picker.Value;
		var categories = menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var category = Field(menu, "Label");

		string chosen;
		if (wanted.Length == 0)
		{
			if (!Ui.Interactive)
			{
				await ClosePicker();
				Console.WriteLine(L($"{DayName(date)}, session {slot}: {session.Name}"));
				Console.WriteLine(L($"Categories: {string.Join(", ", categories)}"));
				Console.WriteLine($"{category}: {string.Join(", ", options.Select(o => o.Name))}");
				return 0;
			}
			while (true)
			{
				if (Ui.Ask(L($"{DayName(date)}, session {slot} ({session.Name}) — category"), categories, categories.IndexOf(category)) is not { } c)
				{
					await ClosePicker();
					return 0;
				}
				options = await PickerCategory(menu, categories[c], null) ?? [];
				var names = options.Select(o => o.Name).ToList();
				if (Ui.Ask(L($"{categories[c]} — session"), names, names.IndexOf(session.Name), cancel: L("Back")) is { } s)
				{
					chosen = names[s];
					break;
				}
			}
		}
		else
		{
			// Names overlap ("Rest" is in "Transition - Restrict"), so the session is looked for
			// among all of them at once: the catalogue of every category's sessions, read once
			// and kept, and read again when it does not have what was typed.
			var catalogue = SessionCatalogue();
			var hit = FindSession(catalogue, wanted);
			if (hit.Category == null && hit.Close.Count == 0)
			{
				catalogue = [];
				foreach (var c in categories)
					catalogue[c] = (await PickerCategory(menu, c, null) ?? []).Select(o => o.Name).ToList();
				if (catalogue.Values.Sum(v => v.Count) > 0)
				{
					Directory.CreateDirectory(DiffFolder);
					File.WriteAllText(SessionCatalogueFile, JsonSerializer.Serialize(catalogue));
				}
				hit = FindSession(catalogue, wanted);
			}
			if (hit.Category == null)
			{
				await ClosePicker();
				Console.Error.WriteLine(hit.Close.Count > 1
					? L($"'{wanted}' matches more than one session: {string.Join(", ", hit.Close)}")
					: L($"There is no session called '{wanted}'. To see the options: schedule set {date:yyyy-MM-dd} {slot}"));
				return 1;
			}
			chosen = hit.Session;
			options = await PickerCategory(menu, hit.Category, null) ?? [];
			if (options.All(o => o.Name != chosen))
			{
				await ClosePicker();
				Console.Error.WriteLine(L($"'{chosen}' does not appear in the {hit.Category} category."));
				return 1;
			}
		}

		if (chosen == session.Name)
		{
			await ClosePicker();
			Console.WriteLine(L($"{DayName(date)}, session {slot} is already {chosen}."));
			return 0;
		}
		var option = options.First(o => o.Name == chosen);
		if (!await PressAndConfirm(option.Pointer)) return 1;

		var after = await ReadSlot(date, d => d.Sessions.Count >= slot ? d.Sessions[slot - 1].Name : null, chosen);
		if (after != chosen)
		{
			Console.Error.WriteLine(L($"Confirmed, but the calendar shows '{after ?? "?"}' as session {slot}."));
			return 1;
		}
		Console.WriteLine(L($"{DayName(date)}, session {slot}: {session.Name} → {chosen}"));
		SayAbout();
		return 0;
	}

	private static async Task<int> ChangeWeek(ScheduleDay day, List<string> words, DateTime date)
	{
		if (day.Week == null)
		{
			Console.Error.WriteLine(L($"The schedule card of the week of {DayName(date)} could not be read."));
			return 1;
		}
		if (!day.Week.Enabled)
		{
			Console.Error.WriteLine(L($"The week of {DayName(date)} has passed; its schedule cannot be changed."));
			return 1;
		}
		var wanted = string.Join(" ", words);

		var picker = await OpenPicker(day.Week.Pointer, "Tactical Style");
		if (picker == null) return 1;
		var (menu, options) = picker.Value;

		// The kinds of schedule, with pre-season's three stages spelt out.
		List<(string Label, string Choice, string Path)> kinds = [];
		foreach (var c in menu.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? ""))
		{
			if (c.EndsWith(" ›", StringComparison.Ordinal))
				foreach (var stage in new[] { "Early", "Mid", "Late" })
					kinds.Add(($"{c[..^2]} › {stage}", null, $"{c[..^2]}>{stage}"));
			else kinds.Add((c, c, null));
		}

		string chosen;
		if (wanted.Length == 0)
		{
			if (!Ui.Interactive)
			{
				await ClosePicker();
				Console.WriteLine($"{WeekName(date)}: {day.Week.Name}");
				Console.WriteLine(L($"Types: {string.Join(", ", kinds.Select(k => k.Label))}"));
				Console.WriteLine($"{Field(menu, "Label")}: {string.Join(", ", options.Select(o => o.Name))}");
				return 0;
			}
			while (true)
			{
				if (Ui.Ask(L($"{WeekName(date)} ({day.Week.Name}) — schedule type"), kinds.Select(k => k.Label).ToList(), kinds.FindIndex(k => k.Choice == Field(menu, "Label"))) is not { } k)
				{
					await ClosePicker();
					return 0;
				}
				options = await PickerCategory(menu, kinds[k].Choice, kinds[k].Path) ?? [];
				var names = options.Select(o => o.Name).ToList();
				if (Ui.Ask(L($"{kinds[k].Label} — schedule"), names, names.FindIndex(n => day.Week.Name.StartsWith(n, StringComparison.Ordinal)), cancel: L("Back")) is { } s)
				{
					chosen = names[s];
					break;
				}
			}
		}
		else
		{
			// "Tactical Style/Gegenpress" or "Pre-Season Early/Light" names the kind; else every kind is looked through.
			var slash = wanted.IndexOf('/');
			var kindsToTry = kinds;
			if (slash > 0)
			{
				var kindWanted = Fold(wanted[..slash]).Replace("›", "").Replace("  ", " ").Trim();
				kindsToTry = kinds.Where(k => Fold(k.Label).Replace("›", "").Replace("  ", " ").Contains(kindWanted, StringComparison.Ordinal)).ToList();
				wanted = wanted[(slash + 1)..].Trim();
			}
			// The same name is in more than one kind ("Technical", "Tactical"): the season's own
			// kinds are looked through first, pre-season's last, where it has to be asked for by kind.
			kindsToTry = kindsToTry.OrderBy(k => k.Path != null).ThenBy(k => k.Choice is "Training Style" ? 0 : 1).ToList();
			chosen = null;
			var first = true;
			foreach (var kind in kindsToTry)
			{
				// What is showing is tried as it is; the rest are chosen first.
				if (!(first && slash < 0 && kind.Choice == Field(menu, "Label")))
					options = await PickerCategory(menu, kind.Choice, kind.Path) ?? [];
				first = false;
				var hits = options.Where(o => Fold(o.Name) == Fold(wanted)).ToList();
				if (hits.Count == 0 && Matching(options.Select(o => o.Name), n => n, wanted) is [var one]) hits = options.Where(o => o.Name == one).ToList();
				if (hits.Count == 1)
				{
					chosen = hits[0].Name;
					break;
				}
			}
			if (chosen == null)
			{
				await ClosePicker();
				Console.Error.WriteLine(L($"No schedule called '{wanted}' was found. To see the options: schedule style {date:yyyy-MM-dd}"));
				return 1;
			}
		}

		var option = options.First(o => o.Name == chosen);
		if (!await PressAndConfirm(option.Pointer)) return 1;

		// A pre-season schedule's card adds its stage: "Light - Early".
		var after = await ReadSlot(date, d => d.Week?.Name is { } n && n.StartsWith($"{chosen} - ", StringComparison.Ordinal) ? chosen : d.Week?.Name, chosen);
		if (after != chosen)
		{
			Console.Error.WriteLine(L($"Confirmed, but the week's card shows '{after ?? "?"}'."));
			return 1;
		}
		Console.WriteLine($"{WeekName(date)}: {day.Week.Name} → {chosen}");
		SayAbout();
		return 0;
	}

	/// <summary>
	/// Clicks a session or a week's card and waits for its picker: the category list (known by
	/// one of its choices) and the option buttons after it, up to the first text.
	/// </summary>
	private static async Task<(JsonElement Menu, List<ScheduleSlot> Options)?> OpenPicker(string pointer, string oneChoice)
	{
		var clicked = await Fm.Get("click", ("ptr", pointer));
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Report(clicked);
			return null;
		}
		(JsonElement, List<ScheduleSlot>)? picker = null;
		await Until(async () => (picker = await ReadPicker(oneChoice)) is { Item2.Count: > 0 }, seconds: 8, every: 0.3);
		if (picker is not { Item2.Count: > 0 })
		{
			Console.Error.WriteLine(L("The choice window did not open."));
			return null;
		}
		return picker;
	}

	/// <summary>
	/// The open picker read by its description's data path, not by the word "Confirm": a
	/// session's description is bound under <c>binding.Description</c>, a week schedule's under
	/// <c>currentschedule.Description</c>; neither is a label the game translates.
	/// </summary>
	private static async Task<List<JsonElement>> PickerForm()
	{
		foreach (var path in new[] { "binding.Description", "currentschedule.Description" })
		{
			var form = (await Fm.GetJson("form", ("anchorpath", path), ("up", "18"), ("buttons", "1")))
				.EnumerateArray().ToList();
			if (form.Count > 0) return form;
		}
		return [];
	}

	private static async Task<(JsonElement, List<ScheduleSlot>)?> ReadPicker(string oneChoice)
	{
		var form = await PickerForm();
		var at = form.FindIndex(f => Field(f, "Kind") == "dropdown" && f.TryGetProperty("Choices", out var c)
			&& c.EnumerateArray().Any(x => x.GetString() == oneChoice));
		if (at < 0) return null;
		var options = form.Skip(at + 1).TakeWhile(f => Field(f, "Kind") == "button")
			.Select(f => new ScheduleSlot(Field(f, "Pointer"), Field(f, "Text"), Field(f, "Enabled") == "True"))
			.Where(o => o.Name.Length > 0).ToList();
		return (form[at], options);
	}

	/// <summary>Chooses a category in the picker and reads its options once they change.</summary>
	private static async Task<List<ScheduleSlot>> PickerCategory(JsonElement menu, string choice, string? path)
	{
		var oneChoice = menu.GetProperty("Choices")[0].GetString() ?? "";
		var said = path != null
			? await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("names", path), ("fire", "1"))
			: await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("choice", choice), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return null;
		var wantedLabel = path != null ? path[(path.IndexOf('>') + 1)..] : choice;
		List<ScheduleSlot> options = [];
		var previous = "";
		// The new options are drawn over a few frames; read once two readings agree and the list shows the choice.
		await Until(async () =>
		{
			var read = await ReadPicker(oneChoice);
			if (read == null) return false;
			options = read!.Value.Item2;
			var now = string.Join("|", options.Select(o => o.Name));
			var settled = Field(read!.Value.Item1, "Label") == wantedLabel && now == previous && options.Count > 0;
			previous = now;
			return settled;
		}, seconds: 6, every: 0.3);
		return options;
	}

	/// <summary>
	/// Presses an option and confirms the picker. In between the picker describes the option —
	/// what it is, the attributes it works on, what it does to condition and injury risk — and
	/// that is kept to be said once the change has gone through.
	/// </summary>
	private static async Task<bool> PressAndConfirm(string pointer)
	{
		PickedAbout = [];
		var before = string.Join("\0", (await PickerForm()).Where(f => Field(f, "Kind") == "text")
			.Select(f => Field(f, "Text")));
		var pressed = await Fm.Get("press", ("ptr", pointer));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return Report(pressed) == 0;
		// The picker redraws its description for the pressed option. It is read once that
		// description has changed from what was there before the press and two readings agree —
		// a fixed pause could read the previous option's description on a slow frame.
		List<JsonElement> form = [];
		var previous = before;
		var changed = false;
		await Until(async () =>
		{
			form = await PickerForm();
			var now = string.Join("\0", form.Where(f => Field(f, "Kind") == "text").Select(f => Field(f, "Text")));
			changed = now != before;
			var settled = changed && now == previous;
			previous = now;
			return settled;
		}, seconds: 2, every: 0.2);
		// The headings ("Happiness", "Injury Risk") are bound as paths, not drawn, so the pairs
		// that come with the page do not carry them; ask the game's own table for their words.
		await GameWords.Ask(Fm, form.Where(f => Field(f, "Kind") == "text" && Field(f, "Path") == "binding.Name")
			.Select(f => Field(f, "Text")).Distinct().ToArray());
		PickedAbout = About(form);
		// The picker's own confirm button sends these; its label ("Confirm") is the game's word.
		var confirmed = "error: the picker's confirm button was not found";
		foreach (var sent in new[] { "EventToPlugin:EventFmxConfirmTrainingSession", "EventToPlugin:EventFmxConfirmTrainingSchedule" })
		{
			confirmed = await Fm.Get("press", ("event", sent));
			if (!confirmed.StartsWith("error", StringComparison.Ordinal)) break;
		}
		if (confirmed.StartsWith("error", StringComparison.Ordinal)) return Report(confirmed) == 0;
		return true;
	}

	/// <summary>
	/// FM27's way to change one session: on Training › Overview each session is a button whose sent
	/// value names its day and place (Date, SessionIndex); pressing it opens the week's editor, where
	/// the categories and their sessions are two radio groups (known by the lists they show:
	/// TrainingSessionCategoryOnlyDropdown, TrainingSessionsForCategoryDropdown) and Confirm commits the
	/// week (EventFmxCommitTrainingWeekFromCard). Null when the page is not FM27's (no such buttons), so
	/// the older calendar is tried.
	/// </summary>
	private static async Task<int?> ChangeSessionInWeek(DateTime day, int slot, string wanted)
	{
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement? button = null;
		var date = day.ToString("yyyy-MM-dd");
		async Task<JsonElement?> Find()
		{
			var all = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Events").Contains("EventFmxBeginTrainingWeekEditFromCard", StringComparison.Ordinal) && Field(b, "Sends").Length > 0).ToList();
			if (all.Count == 0) return null;
			var sent = await PullEach(all.Select(b => (Field(b, "Sends"), "Date,SessionIndex")).ToList());
			var at = sent.FindIndex(x => Field(x, "Date") == date && Field(x, "SessionIndex") == (slot - 1).ToString());
			return at >= 0 ? all[at] : (JsonElement?)default(JsonElement);
		}
		await Until(async () => (button = await Find()) != null, seconds: 8, every: 0.5);
		if (button == null) return null;
		if (button.Value.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L($"Session {slot} of {DayName(day)} is not in the calendar on screen."));
			return 1;
		}
		if (Field(button.Value, "Enabled") != "True")
		{
			Console.Error.WriteLine(L($"Session {slot} of {DayName(day)} cannot be changed (a past day or a match)."));
			return 1;
		}

		async Task<JsonElement?> Button(string ev) => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains(ev, StringComparison.Ordinal)).Cast<JsonElement?>().FirstOrDefault();
		async Task<JsonElement?> Radio(string items) => (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.Where(d => Field(d, "Items").EndsWith(items, StringComparison.Ordinal)).Cast<JsonElement?>().FirstOrDefault();
		await Fm.Get("press", ("ptr", Field(button.Value, "Pointer")));
		if (!await Until(async () => await Button("EventFmxCommitTrainingWeekFromCard") != null && await Radio("TrainingSessionCategoryOnlyDropdown") != null, seconds: 8, every: 0.3))
		{
			Console.Error.WriteLine(L("The week's editor did not open."));
			return 1;
		}
		async Task Leave() { if (await Button("EventFmxCancelTrainingWeekEditFromCard") is { } cancel) await Fm.Get("press", ("ptr", Field(cancel, "Pointer"))); }

		// The session is looked for in each category, by its name as the plugin gives it or as the game shows it.
		var categories = await Radio("TrainingSessionCategoryOnlyDropdown");
		var names = categories!.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var all = new List<string>();
		for (var i = 0; i < names.Count; i++)
		{
			await Fm.Get("select", ("ptr", Field(categories.Value, "Pointer")), ("choice", names[i]));
			JsonElement? sessions = null;
			await Until(async () => (sessions = await Radio("TrainingSessionsForCategoryDropdown")) is { } s && Field(await Radio("TrainingSessionCategoryOnlyDropdown") ?? s, "Selected") == i.ToString(), seconds: 4, every: 0.2);
			if (sessions == null) continue;
			var choices = sessions.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
			var shown = sessions.Value.TryGetProperty("Shown", out var sh) ? sh.EnumerateArray().Select(c => c.GetString() ?? "").ToList() : choices;
			all.AddRange(shown);
			var at = choices.FindIndex(c => Fold(c) == Fold(wanted));
			if (at < 0) at = shown.FindIndex(c => Fold(c) == Fold(wanted));
			if (at < 0) continue;
			var picked = await Fm.Get("select", ("ptr", Field(sessions.Value, "Pointer")), ("choice", choices[at]));
			if (picked.StartsWith("error", StringComparison.Ordinal)) { await Leave(); return Report(picked); }
			if (await Button("EventFmxCommitTrainingWeekFromCard") is not { } confirm) { await Leave(); return 1; }
			await Fm.Get("press", ("ptr", Field(confirm, "Pointer")));
			await Until(async () => await Button("EventFmxCommitTrainingWeekFromCard") == null, seconds: 6, every: 0.3);
			// Read back from the calendar: the same day and place now carries the session.
			string now = "";
			var landed = await Until(async () => await Find() is { ValueKind: JsonValueKind.Object } b && (now = Field(b, "Label")) is { Length: > 0 }
				&& (Fold(now) == Fold(choices[at]) || Fold(GameWords.Of(now)) == Fold(shown[at])), seconds: 6, every: 0.5);
			Console.WriteLine(landed ? L($"{DayName(day)} session {slot}: {shown[at]}.") : L($"Sent, but the calendar shows {GameWords.Of(now)} for {DayName(day)} session {slot}."));
			return landed ? 0 : 1;
		}
		await Leave();
		Console.Error.WriteLine(L($"There is no session called '{wanted}'. Sessions: {string.Join(", ", all.Distinct())}"));
		return 1;
	}
}
