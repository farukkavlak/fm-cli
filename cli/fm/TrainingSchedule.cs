using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Changing the training schedule (FINDINGS §8.89). Portal › Calendar has a Training tab that draws
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
				? L("kullanım: schedule style <gün> [<program>]   (gün: 14, \"14 ekim\", 2025-10-14, yarın, cuma)")
				: L("kullanım: schedule set <gün> <1|2|3> [<seans>]   (gün: 14, \"14 ekim\", 2025-10-14, yarın, cuma)"));
			return 64;
		}
		words = words.Skip(used).ToList();
		if (!style && (words.Count == 0 || !int.TryParse(words[0], out var slot) || slot < 1 || slot > 3))
		{
			Console.Error.WriteLine(L("Hangi seans? Günden sonra 1, 2 ya da 3 (günün sırasıyla)."));
			return 64;
		}

		var grid = await TrainingCalendar(day);
		if (grid == null) return 1;
		var found = grid.FirstOrDefault(d => d.Day == day);
		if (found == null)
		{
			Console.Error.WriteLine(L($"{DayName(day)} takvimde bulunamadı."));
			return 1;
		}
		return style ? await ChangeWeek(found, words, day) : await ChangeSession(found, words, day);
	}

	private static async Task<int> ChangeSession(ScheduleDay day, List<string> words, DateTime date)
	{
		var slot = int.Parse(words[0]);
		if (day.Sessions.Count < slot)
		{
			Console.Error.WriteLine(L($"{DayName(date)} günü {slot}. seans okunamadı."));
			return 1;
		}
		var session = day.Sessions[slot - 1];
		if (session.Match)
		{
			Console.Error.WriteLine(L($"{DayName(date)}, {slot}. seans maçın kendisi ({session.Name}); değiştirilemez."));
			return 1;
		}
		if (!session.Enabled)
		{
			Console.Error.WriteLine(L($"{DayName(date)}, {slot}. seans ({session.Name}) değiştirilemiyor: geçmiş bir gün ya da maç/yolculuk."));
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
				Console.WriteLine(L($"{DayName(date)}, {slot}. seans: {session.Name}"));
				Console.WriteLine(L($"Kategoriler: {string.Join(", ", categories)}"));
				Console.WriteLine($"{category}: {string.Join(", ", options.Select(o => o.Name))}");
				return 0;
			}
			while (true)
			{
				if (Ui.Ask(L($"{DayName(date)}, {slot}. seans ({session.Name}) — kategori"), categories, categories.IndexOf(category)) is not { } c)
				{
					await ClosePicker();
					return 0;
				}
				options = await PickerCategory(menu, categories[c], null) ?? [];
				var names = options.Select(o => o.Name).ToList();
				if (Ui.Ask(L($"{categories[c]} — seans"), names, names.IndexOf(session.Name), cancel: L("Geri")) is { } s)
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
					? L($"'{wanted}' birden çok seansa uyuyor: {string.Join(", ", hit.Close)}")
					: L($"'{wanted}' diye bir seans yok. Seçenekleri görmek için: schedule set {date:yyyy-MM-dd} {slot}"));
				return 1;
			}
			chosen = hit.Session;
			options = await PickerCategory(menu, hit.Category, null) ?? [];
			if (options.All(o => o.Name != chosen))
			{
				await ClosePicker();
				Console.Error.WriteLine(L($"'{chosen}' {hit.Category} kategorisinde görünmüyor."));
				return 1;
			}
		}

		if (chosen == session.Name)
		{
			await ClosePicker();
			Console.WriteLine(L($"{DayName(date)}, {slot}. seans zaten {chosen}."));
			return 0;
		}
		var option = options.First(o => o.Name == chosen);
		if (!await PressAndConfirm(option.Pointer)) return 1;

		var after = await ReadSlot(date, d => d.Sessions.Count >= slot ? d.Sessions[slot - 1].Name : null, chosen);
		if (after != chosen)
		{
			Console.Error.WriteLine(L($"Onaylandı ama takvimde {slot}. seans '{after ?? "?"}' görünüyor."));
			return 1;
		}
		Console.WriteLine(L($"{DayName(date)}, {slot}. seans: {session.Name} → {chosen}"));
		SayAbout();
		return 0;
	}

	private static async Task<int> ChangeWeek(ScheduleDay day, List<string> words, DateTime date)
	{
		if (day.Week == null)
		{
			Console.Error.WriteLine(L($"{DayName(date)} haftasının program kartı okunamadı."));
			return 1;
		}
		if (!day.Week.Enabled)
		{
			Console.Error.WriteLine(L($"{DayName(date)} haftası geçti; programı değiştirilemiyor."));
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
				Console.WriteLine(L($"Türler: {string.Join(", ", kinds.Select(k => k.Label))}"));
				Console.WriteLine($"{Field(menu, "Label")}: {string.Join(", ", options.Select(o => o.Name))}");
				return 0;
			}
			while (true)
			{
				if (Ui.Ask(L($"{WeekName(date)} ({day.Week.Name}) — program türü"), kinds.Select(k => k.Label).ToList(), kinds.FindIndex(k => k.Choice == Field(menu, "Label"))) is not { } k)
				{
					await ClosePicker();
					return 0;
				}
				options = await PickerCategory(menu, kinds[k].Choice, kinds[k].Path) ?? [];
				var names = options.Select(o => o.Name).ToList();
				if (Ui.Ask(L($"{kinds[k].Label} — program"), names, names.FindIndex(n => day.Week.Name.StartsWith(n, StringComparison.Ordinal)), cancel: L("Geri")) is { } s)
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
				Console.Error.WriteLine(L($"'{wanted}' diye bir program bulunamadı. Seçenekleri görmek için: schedule style {date:yyyy-MM-dd}"));
				return 1;
			}
		}

		var option = options.First(o => o.Name == chosen);
		if (!await PressAndConfirm(option.Pointer)) return 1;

		// A pre-season schedule's card adds its stage: "Light - Early".
		var after = await ReadSlot(date, d => d.Week?.Name is { } n && n.StartsWith($"{chosen} - ", StringComparison.Ordinal) ? chosen : d.Week?.Name, chosen);
		if (after != chosen)
		{
			Console.Error.WriteLine(L($"Onaylandı ama haftanın kartında '{after ?? "?"}' görünüyor."));
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
			Console.Error.WriteLine(L("Seçim penceresi açılmadı."));
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
	private static async Task<List<ScheduleSlot>> PickerCategory(JsonElement menu, string choice, string path)
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
			options = read.Value.Item2;
			var now = string.Join("|", options.Select(o => o.Name));
			var settled = Field(read.Value.Item1, "Label") == wantedLabel && now == previous && options.Count > 0;
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

	private static string SessionCatalogueFile => CacheFile("training-sessions.json");

	private static Dictionary<string, List<string>> SessionCatalogue()
	{
		try { return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(SessionCatalogueFile)) ?? []; }
		catch { return []; }
	}

	/// <summary>
	/// A session by what was typed: the one named exactly so, else the only one it fits.
	/// "Physical/Recovery" names the category too — "Recovery" and "Rest" are in more than one.
	/// </summary>
	private static (string Category, string Session, List<string> Close) FindSession(Dictionary<string, List<string>> catalogue, string typed)
	{
		var slash = typed.IndexOf('/');
		var inCategory = slash > 0 ? Fold(typed[..slash]).Trim() : null;
		var wanted = slash > 0 ? typed[(slash + 1)..].Trim() : typed;
		var all = catalogue
			.Where(c => inCategory == null || Fold(c.Key).StartsWith(inCategory, StringComparison.Ordinal))
			.SelectMany(c => c.Value.Select(s => (Category: c.Key, Session: s))).ToList();

		var exact = all.Where(x => Fold(x.Session) == Fold(wanted)).ToList();
		// The same session listed under two categories is one session: the first will do.
		if (exact.Count > 0) return (exact[0].Category, exact[0].Session, []);
		var close = Matching(all, x => x.Session, wanted);
		var names = close.Select(x => x.Session).Distinct().ToList();
		return names.Count == 1 ? (close[0].Category, close[0].Session, names) : (null, null, names);
	}

	private static List<string> PickedAbout = [];

	/// <summary>
	/// A session is described in the picker as its focuses (primary, secondary, tertiary), each
	/// with who it is for, a sentence, "Impacts", its priority and share, then headings with what
	/// is under them — the attributes and their names, and effects as a pair (a heading and a
	/// level). The focuses and their descriptions are taken by the data paths they bind to: the
	/// first focus is "Primary Focus" in English but "Priority" in Turkish, so looking for one
	/// text left a Turkish game with only the sentence. The attributes are the heading under
	/// which the most rows are listed; the level words are the bridge's English, the same in
	/// both games. A week's schedule has no focus: its title and a sentence.
	/// </summary>
	private static List<string> About(List<JsonElement> form)
	{
		var items = form.Where(f => Field(f, "Kind") == "text")
			.Select(f => (Path: Field(f, "Path"), Text: Field(f, "Text"))).ToList();
		List<string> lines = [];
		var focus = items.FindIndex(i => i.Path == "binding.SessionGroupFocusName");
		if (focus < 0)
		{
			// A week's schedule: "Tactical - No matches", then its sentence.
			var sentence = items.Select(i => i.Text).FirstOrDefault(t => t.EndsWith('.') && t.Contains(' '));
			if (sentence != null) lines.Add(sentence);
			return lines;
		}
		var end = items.FindIndex(focus + 1, i => i.Path == "binding.SessionGroupFocusName") is var next and > 0 ? next : items.Count;
		var own = items.Skip(focus + 1).Take(end - focus - 1).ToList();
		if (own.FirstOrDefault(i => i.Path == "binding.Description").Text is { Length: > 0 } about) lines.Add(about);

		static bool Level(string t) => Regex.IsMatch(t, @"^(Slightly |Greatly )?(Increased|Reduced)$");
		// Each heading is followed by its rows; the attributes are the longest of them, and the
		// ones named under it run on until the first heading followed by a level.
		var start = -1;
		var longest = 0;
		for (var i = 0; i < own.Count; i++)
		{
			if (own[i].Path != "binding.Name") continue;
			var rows = own.Skip(i + 1).TakeWhile(x => x.Path == "row").Count();
			if (rows <= longest) continue;
			longest = rows;
			start = i;
		}
		List<string> names = [];
		for (var i = start + 1; start >= 0 && i < own.Count; i++)
		{
			if (i + 1 < own.Count && Level(own[i + 1].Text)) break;
			names.Add(own[i].Text);
		}
		if (names.Count > 0) lines.Add(L($"Özellikler: {string.Join(", ", names)}"));
		List<string> effects = [];
		for (var i = 0; i + 1 < own.Count; i++)
			if (own[i].Path == "binding.Name" && own[i + 1].Path == "row" && Level(own[i + 1].Text))
				effects.Add($"{GameWords.Of(own[i].Text)} {Effect(own[i + 1].Text)}");
		if (effects.Count > 0) lines.Add(L($"Etkiler: {string.Join(" · ", effects)}"));
		return lines;

		static string Effect(string level) => level switch
		{
			"Slightly Increased" => "↑ az",
			"Increased" => "↑",
			"Greatly Increased" => "↑↑",
			"Slightly Reduced" => "↓ az",
			"Reduced" => "↓",
			"Greatly Reduced" => "↓↓",
			_ => level,
		};
	}

	private static void SayAbout()
	{
		foreach (var line in PickedAbout)
		{
			if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(line)}[/]");
			else Console.WriteLine($"  {line}");
		}
	}

	/// <summary>Cancels the picker and waits for it to go, so what is read next is the calendar again.</summary>
	private static async Task ClosePicker()
	{
		// The picker's Cancel is the control that sends ClosePanel under this template name; the
		// word "Cancel" is the game's, not the control's.
		await Fm.Get("press", ("event", "ClosePanel"), ("name", "button-base-label"));
		await Until(async () => (await PickerForm()).Count == 0, seconds: 4, every: 0.3);
	}

	/// <summary>Reads the calendar again until a day's slot shows what was chosen, or a few seconds pass.</summary>
	private static async Task<string> ReadSlot(DateTime date, Func<ScheduleDay, string> slot, string expected)
	{
		string now = null;
		await Until(async () =>
		{
			var day = (await ReadTrainingGrid(date))?.FirstOrDefault(d => d.Day == date);
			now = day == null ? null : slot(day);
			return now == expected;
		}, seconds: 6, every: 0.4);
		return now;
	}

	/// <summary>Opens Portal › Calendar on the month of a day, on its Training tab, and reads the grid.</summary>
	private static async Task<List<ScheduleDay>> TrainingCalendar(DateTime day)
	{
		if (await CalendarMonths() is not var (months, _)) return null;
		var month = months.FirstOrDefault(m => m.Year == day.Year && m.Month == day.Month);
		if (month == null)
		{
			Console.Error.WriteLine(L($"{DayName(day)} takvimin gösterdiği aylarda yok."));
			return null;
		}

		// The calendar read as a form from its "Monday" heading, which every tab of it has: the
		// three tab buttons (they send no event, so the button catalogue leaves them out), the
		// month list — each tab has its own — and the grid.
		async Task<List<JsonElement>> Page() =>
			(await Fm.GetJson("form", ("anchor", "Monday"), ("up", "18"), ("buttons", "1"))).EnumerateArray().ToList();

		// Straight after the page opens the tab can be clicked, or the month chosen, before it
		// listens, so each is done again until the grid shows it. The grid is drawn over a few
		// frames; it is read once two readings in a row agree.
		List<ScheduleDay> grid = null;
		for (var attempt = 0; attempt < 5; attempt++)
		{
			var page = await Page();
			if (!page.Any(f => Field(f, "Name") == "TrainingFocusCard"))
			{
				var tab = page.FirstOrDefault(f => Field(f, "Name") == "navigation-tab-default-default" && Field(f, "Text") == "Training");
				if (tab.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(tab, "Pointer")));
				await Until(async () => (await Page()).Any(f => Field(f, "Name") == "TrainingFocusCard"), seconds: 8, every: 0.4);
				continue;
			}
			var list = page.FirstOrDefault(f => Field(f, "Name") == "MonthDropdown");
			if (list.ValueKind == JsonValueKind.Object && Field(list, "Label") != month.Label)
				await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", month.Value.ToString()));

			var previous = "";
			var drawn = await Until(async () =>
			{
				grid = await ReadTrainingGrid(day);
				var now = grid == null ? "" : string.Join("|", grid.Select(d => $"{d.Day:dd}{d.Week?.Name}{string.Join(",", d.Sessions.Select(s => s.Name))}"));
				var settled = now.Length > 0 && now == previous && grid.Any(d => d.Day == day) && grid.Any(d => d.Week != null);
				previous = now;
				return settled;
			}, seconds: 12, every: 0.4);
			if (drawn) return grid;
		}
		Console.Error.WriteLine(L("Takvimin antrenman sekmesi okunamadı."));
		return null;
	}

	/// <summary>
	/// The Training tab's grid read as a form, by each text's data path (FINDINGS §8.169): for each
	/// week its card (TrainingFocusCard), then its days, each a day (day.DayOfTheMonth) and three
	/// buttons. A match day's middle button is the competition ("SL", "UEL"). The grid starts on the
	/// Monday on or before the month's first day; a day out of step ends the reading.
	/// </summary>
	private static async Task<List<ScheduleDay>> ReadTrainingGrid(DateTime shown)
	{
		var form = (await Fm.GetJson("form", ("anchorpath", "day.DayOfTheMonth"), ("up", "14"), ("buttons", "1"), ("most", "1"))).EnumerateArray().ToList();
		if (!form.Any(f => Field(f, "Name") == "TrainingFocusCard")) return null;

		var days = new List<ScheduleDay>();
		ScheduleSlot week = null;
		var first = new DateTime(shown.Year, shown.Month, 1);
		var next = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
		foreach (var f in form)
		{
			var kind = Field(f, "Kind");
			if (kind == "button" && Field(f, "Name") == "TrainingFocusCard")
			{
				// The card's first text is the schedule's name; the one under it its kind.
				var title = f.TryGetProperty("Texts", out var t) && t.GetArrayLength() > 0 ? t[0].GetString() ?? "" : Field(f, "Text");
				week = new ScheduleSlot(Field(f, "Pointer"), title, Field(f, "Enabled") == "True");
				continue;
			}
			if (kind == "text" && Field(f, "Path") == "day.DayOfTheMonth")
			{
				if (!int.TryParse(Regex.Match(Field(f, "Text"), @"^\d+").Value, out var number) || number != next.Day) break;
				days.Add(new ScheduleDay(next, [], week));
				next = next.AddDays(1);
				continue;
			}
			// A match's cell carries more than a name: the competition, the opponent, home or away.
			if (kind == "button" && days.Count > 0)
				days[^1].Sessions.Add(new ScheduleSlot(Field(f, "Pointer"), Field(f, "Text"), Field(f, "Enabled") == "True",
					f.TryGetProperty("Texts", out var inside) && inside.GetArrayLength() > 1));
		}
		return days;
	}

	private static async Task<DateTime> GameDate() =>
		DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;

	/// <summary>
	/// A day as typed: "2025-10-14", "14" (the next 14th from today), "14 ekim" / "14 oct",
	/// "bugün", "yarın", or a weekday ("cuma": the next Friday, today included). Says how many words it took.
	/// </summary>
	private static DateTime? ScheduleDate(List<string> words, DateTime today, out int used)
	{
		used = 1;
		var first = Fold(words[0]);
		if (DateTime.TryParseExact(words[0], "yyyy-MM-dd", null, DateTimeStyles.None, out var iso)) return iso;
		if (first is "bugun" or "today") return today;
		if (first is "yarin" or "tomorrow") return today.AddDays(1);
		var weekday = Array.IndexOf(WeekdayWords, first);
		if (weekday < 0) weekday = Array.FindIndex(Enum.GetNames<DayOfWeek>(), n => Fold(n) == first);
		if (weekday >= 0) return today.AddDays(((weekday - (int)today.DayOfWeek) + 7) % 7);
		if (!int.TryParse(words[0], out var n) || n < 1 || n > 31) return null;

		if (words.Count > 1)
		{
			var m = Fold(words[1]);
			var month = Months.Select(Fold).ToList().FindIndex(x => x.StartsWith(m, StringComparison.Ordinal) && m.Length >= 3) + 1;
			if (month == 0) month = Array.FindIndex(MonthNames, x => Fold(x) == (m.Length >= 3 ? m[..3] : m)) + 1;
			if (month > 0)
			{
				used = 2;
				var year = month < today.Month - 6 ? today.Year + 1 : month > today.Month + 6 ? today.Year - 1 : today.Year;
				return n <= DateTime.DaysInMonth(year, month) ? new DateTime(year, month, n) : null;
			}
		}
		for (var ahead = 0; ahead < 3; ahead++)
		{
			var at = today.AddMonths(ahead);
			if (n > DateTime.DaysInMonth(at.Year, at.Month)) continue;
			var date = new DateTime(at.Year, at.Month, n);
			if (date >= today) return date;
		}
		return null;
	}

	private static string DayName(DateTime d) => $"{d.Day} {Months[d.Month - 1]} {Days[(int)d.DayOfWeek]}";

	private static string WeekName(DateTime d)
	{
		var monday = d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
		return L($"{monday.Day} {Months[monday.Month - 1]} haftası");
	}
}
