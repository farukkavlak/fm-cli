using System.Text.Json;

namespace FmCli;

/// <summary>
/// FM27's way to change a week's schedule. On Training › Overview each week row has a selector
/// (#TrainingScheduleSelectorPopup, a floating popup; disabled for past weeks), found beside one
/// of the day's session buttons (they send Date). Inside, the categories are a list kept as one
/// choice (#category-list: items with PropertyValue and DisplayValue), and the chosen category's
/// schedules are rows whose button sends EventFmxConfirmTrainingSchedule, each row's item naming
/// its schedule (ScheduleIndex, DescriptionShort, Recommended, Description). Read back from the
/// selector's own title (week.TrainingScheduleName).
/// </summary>
internal static partial class Program
{
	private const string WeekSelector = "TrainingScheduleSelectorPopup";
	private const string ApplySchedule = "EventToPlugin:EventFmxConfirmTrainingSchedule";

	private sealed record WeekSchedule(int Category, int Index, string Name, string About, bool Recommended, string Pointer);

	/// <summary>Null when the page shows no session buttons.</summary>
	private static async Task<int?> ChangeWeekInSelector(DateTime day, string wanted, bool json = false)
	{
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		JsonElement? session = null;
		await Until(async () => (session = await SessionButton(day, 0)) != null, seconds: 8, every: 0.5);
		if (session == null) return null;
		if (session.Value.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L($"{DayName(day)} is not in the calendar on screen."));
			return 1;
		}
		async Task<JsonElement?> Selector()
		{
			try { return await Fm.GetJson("popup", ("near", Field(session.Value, "Pointer")), ("named", WeekSelector)); }
			catch (FmError) { return null; }
		}
		if (await Selector() is not { } selector)
		{
			Console.Error.WriteLine(L($"The schedule card of the week of {DayName(day)} could not be read."));
			return 1;
		}
		var current = Field(selector, "Title");
		if (Field(selector, "Enabled") != "True")
		{
			Console.Error.WriteLine(L($"The game does not let the week of {DayName(day)} change its schedule (a week under way or gone)."));
			return 1;
		}

		var sessionsBefore = await WeekSessions(day);
		async Task Close() => await Fm.Get("popup", ("ptr", Field(selector, "Pointer")), ("close", "1"));
		async Task<bool> Shown() => (await Fm.GetJson("checkrows", ("named", "category-list"))).GetArrayLength() > 0;
		// Its click opens and shuts it: one left open is shut first.
		if (await Shown())
		{
			await Close();
			if (!await Until(async () => !await Shown(), seconds: 4, every: 0.3))
			{
				Console.Error.WriteLine(L("Another week's choice window is open; close it first."));
				return 1;
			}
		}
		var said = await Fm.Get("popup", ("ptr", Field(selector, "Pointer")));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		JsonElement list = default;
		var gen = Environment.TickCount64.ToString();
		if (!await Until(async () => (await Fm.GetJson("checkrows", ("named", "category-list"), ("gen", gen))).EnumerateArray()
			    .FirstOrDefault() is { ValueKind: JsonValueKind.Object } l && (list = l).GetProperty("Items").GetArrayLength() > 0, seconds: 8, every: 0.3))
		{
			await Close();
			Console.Error.WriteLine(L("The choice window did not open."));
			return 1;
		}
		var table = Field(list, "Table");
		var items = list.GetProperty("Items").EnumerateArray().Select(i => i.GetString() ?? "").ToList();
		var read = await PullEach(items.Select(i => (i, "DisplayValue,PropertyValue")).ToList(), shown: "DisplayValue");
		var categories = read.Select(c => (Id: int.TryParse(Field(c, "PropertyValue"), out var n) ? n : -1, Name: Field(c, "DisplayValue"))).ToList();

		// One category chosen, its schedules read once its rows all say they are of it.
		async Task<List<WeekSchedule>> Schedules(int at)
		{
			await Fm.Get("rowselect", ("table", table), ("index", at.ToString()), ("choose", "1"));
			List<WeekSchedule> found = [];
			await Until(async () =>
			{
				var g = Environment.TickCount64.ToString();
				var rows = (await Fm.GetJson("rowcontrols", ("event", ApplySchedule), ("buttons", "1"), ("gen", g))).EnumerateArray()
					.Where(r => Field(r, "Item").Length > 0).ToList();
				if (rows.Count == 0) return false;
				// A row's fields come in over a few frames; its category is read at its own path. Names in the game's words.
				var held = await PullEach(rows.Select(r => (Field(r, "Item"), "ScheduleIndex,DescriptionShort,Recommended,Description"))
					.Concat(rows.Select(r => ($"{Field(r, "Item")}.ScheduleCategory", "PropertyValue"))).ToList(),
					complete: x => Field(x, "ScheduleIndex").Length > 0 && Field(x, "DescriptionShort").Length > 0, shown: "DescriptionShort,Description");
				var kinds = held.Skip(rows.Count).Select(h => Field(h, "PropertyValue")).ToList();
				if (kinds.Any(k => k != categories[at].Id.ToString())) return false;
				found = rows.Zip(held).Select(p => new WeekSchedule(categories[at].Id, int.TryParse(Field(p.Second, "ScheduleIndex"), out var n) ? n : -1,
					Field(p.Second, "DescriptionShort"), Field(p.Second, "Description"), Field(p.Second, "Recommended") == "True", Field(p.First, "Pointer"))).ToList();
				return found.All(f => f.Name.Length > 0 && f.Index >= 0);
			}, seconds: 6, every: 0.3);
			return found;
		}

		var all = new List<(int At, List<WeekSchedule> Schedules)>();
		for (var i = 0; i < categories.Count; i++) all.Add((i, await Schedules(i)));

		WeekSchedule? chosen = null;
		if (wanted.Length == 0)
		{
			if (json)
			{
				await Close();
				// Each schedule by the game's own numbers (category.index): `schedule style <day> <id>` takes them.
				Console.WriteLine(JsonOut.Of(new
				{
					Week = day.ToString("yyyy-MM-dd"), Current = current,
					CurrentId = all.SelectMany(a => a.Schedules).FirstOrDefault(s => s.Name == current) is { } on ? $"{on.Category}.{on.Index}" : "",
					Schedules = all.SelectMany(a => a.Schedules.Select(s => new { Id = $"{s.Category}.{s.Index}", Kind = categories[a.At].Name, s.Name, s.Recommended })),
				}));
				return 0;
			}
			if (!Ui.Interactive)
			{
				await Close();
				Console.WriteLine($"{WeekName(day)}: {current}");
				foreach (var (at, schedules) in all)
					Console.WriteLine($"{categories[at].Name}: {string.Join(", ", schedules.Select(s => s.Recommended ? $"{s.Name} ★" : s.Name))}");
				return 0;
			}
			while (chosen == null)
			{
				var names = categories.Select(c => c.Name).ToList();
				if (Ui.Ask(L($"{WeekName(day)} ({current}) — schedule type"), names, all.FindIndex(a => a.Schedules.Any(s => s.Name == current))) is not { } c)
				{
					await Close();
					return 0;
				}
				var schedules = all[c].Schedules;
				if (Ui.Ask(L($"{names[c]} — schedule"), schedules.Select(s => s.Recommended ? $"{s.Name} ★" : s.Name).ToList(),
					    schedules.FindIndex(s => s.Name == current), cancel: L("Back")) is { } s)
					chosen = schedules[s];
			}
		}
		else
		{
			var every = all.SelectMany(a => a.Schedules).ToList();
			// The game's own numbers (category.index, from --json) name one exactly in any language.
			var hits = every.Where(s => $"{s.Category}.{s.Index}" == wanted.Trim()).ToList();
			if (hits.Count == 0) hits = every.Where(s => Fold(s.Name) == Fold(wanted)).ToList();
			if (hits.Count == 0) hits = Matching(every, s => s.Name, wanted);
			if (hits.Count != 1)
			{
				await Close();
				Console.Error.WriteLine(hits.Count > 1
					? L($"'{wanted}' matches more than one schedule: {string.Join(", ", hits.Select(h => h.Name))}")
					: L($"No schedule called '{wanted}' was found. To see the options: schedule style {day:yyyy-MM-dd}"));
				return 1;
			}
			chosen = hits[0];
		}

		if (chosen.Name == current)
		{
			await Close();
			Console.WriteLine(L($"{WeekName(day)} is already {current}."));
			return 0;
		}
		// The buttons were read for the last category; the chosen one's are read again for their pointers.
		var at2 = categories.FindIndex(c => c.Id == chosen.Category);
		var button = (await Schedules(at2)).FirstOrDefault(s => s.Index == chosen.Index);
		if (button == null)
		{
			await Close();
			Console.Error.WriteLine(L($"'{chosen.Name}' could not be found again in the choice window."));
			return 1;
		}
		var pressed = await Fm.Get("press", ("ptr", button.Pointer));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) { await Close(); return Report(pressed); }

		// Read back: the card names the schedule, except a variant (the card says only the mix's name);
		// then the week's sessions, by their kind, are what changed.
		var now = current;
		var landed = await Until(async () => (now = await Selector() is { } s ? Field(s, "Title") : now) == chosen.Name
			|| (now != current && await WeekSessions(day) != sessionsBefore), seconds: 6, every: 0.3);
		if (!landed && await WeekSessions(day) != sessionsBefore) landed = true;
		if (!landed)
		{
			Console.Error.WriteLine(now == current
				? L($"Sent, but the week's card and sessions did not change ({now}); it may already be this schedule.")
				: L($"Confirmed, but the week's card shows '{now}'."));
			return 1;
		}
		Console.WriteLine($"{WeekName(day)}: {current} → {chosen.Name}");
		if (chosen.About.Length > 0) PickedAbout = [chosen.About];
		else PickedAbout = [];
		SayAbout();
		return 0;
	}

	/// <summary>The kinds of a week's sessions (SessionType), day by day, as its session buttons send them.</summary>
	private static async Task<string> WeekSessions(DateTime day)
	{
		var monday = day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
		var all = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Where(b => Field(b, "Events").Contains("EventFmxBeginTrainingWeekEditFromCard", StringComparison.Ordinal) && Field(b, "Sends").Length > 0).ToList();
		var sent = await PullEach(all.Select(b => (Field(b, "Sends"), "Date,SessionIndex,SessionType")).ToList());
		return string.Join(",", sent
			.Where(x => DateTime.TryParseExact(Field(x, "Date"), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) && d >= monday && d < monday.AddDays(7))
			.OrderBy(x => Field(x, "Date")).ThenBy(x => Field(x, "SessionIndex"))
			.Select(x => Field(x, "SessionType")));
	}
}
