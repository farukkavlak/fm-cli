using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

internal static partial class Program
{
	/// <summary>Opens Portal › Calendar on the month of a day, on its Training tab, and reads the grid.</summary>
	private static async Task<List<ScheduleDay>?> TrainingCalendar(DateTime day)
	{
		var opened = await OpenPage(Screen.Calendar);
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Report(opened);
			return null;
		}

		// The calendar read as a form from its "Monday" heading, which every tab of it has: the
		// three tab buttons (they send no event, so the button catalogue leaves them out), the
		// month list — each tab has its own — and the grid.
		async Task<List<JsonElement>> Page() =>
			(await Fm.GetJson("form", ("anchor", "Monday"), ("up", "18"), ("buttons", "1"))).EnumerateArray().ToList();

		// Each tab keeps a month list of its own, and the general tab's is read too: the month the
		// training tab shows is known from the training tab's own list, anchored on the game's
		// "Today", never from whichever list came first.
		var today = await GameDate();
		var anchored = false;
		int todayValue = int.MinValue;
		List<int> values = [];

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
				if (tab!.ValueKind == JsonValueKind.Object) await Fm.Get("click", ("ptr", Field(tab, "Pointer")));
				await Until(async () => (await Page()).Any(f => Field(f, "Name") == "TrainingFocusCard"), seconds: 8, every: 0.4);
				continue;
			}

			var list = ActiveMonthList(page);
			if (list!.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine(L("The calendar page has no month picker."));
				return null;
			}

			if (!anchored)
			{
				if (await TrainingMonthAnchor(Page, list) is not { } anchor) return null;
				(list, todayValue, values) = (anchor.List, anchor.TodayValue, anchor.Values);
				anchored = true;
			}

			// The list's values count months from the season's first; today's is the one "Today"
			// chose, so the month wanted is that many months away.
			var wanted = todayValue + (day.Year - today.Year) * 12 + (day.Month - today.Month);
			if (!values.Contains(wanted))
			{
				Console.Error.WriteLine(L($"{DayName(day)} is not among the months the calendar shows."));
				return null;
			}
			if (MonthValue(list) != wanted)
				await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", wanted.ToString()));
			await Until(async () => ActiveMonthList(await Page()) is { ValueKind: JsonValueKind.Object } now
				&& MonthValue(now) == wanted, seconds: 6, every: 0.25);

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
		Console.Error.WriteLine(L("The calendar's training tab could not be read."));
		return null;
	}

	/// <summary>The month values a list offers, and the value it is on.</summary>
	private static List<int> MonthValues(JsonElement list) =>
		list.TryGetProperty("Values", out var values)
			? values.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.MinValue).Where(v => v != int.MinValue).ToList()
			: [];

	private static int MonthValue(JsonElement list)
	{
		var values = MonthValues(list);
		return int.TryParse(Field(list, "Selected"), out var at) && at >= 0 && at < values.Count ? values[at] : int.MinValue;
	}

	/// <summary>
	/// The month list of the tab in front: the tabs keep theirs side by side, and the one a tab is
	/// not on can be left empty (label and selection gone), so the last one that knows its month
	/// is the one showing it.
	/// </summary>
	private static JsonElement ActiveMonthList(List<JsonElement> page)
	{
		var shown = page.Where(f => Field(f, "Name") == "MonthDropdown" && MonthValue(f) != int.MinValue).ToList();
		return shown.Count > 0 ? shown[^1] : page.FirstOrDefault(f => Field(f, "Name") == "MonthDropdown");
	}

	/// <summary>
	/// Anchors the month on show to the game's "Today", from the training tab's own list: the list
	/// is moved off the month it shows first, so that "Today" is a change and its answer can be
	/// told apart from the month it was already on. With the list's value for today's month known,
	/// every value in it names its month, without reading any label.
	/// </summary>
	private static async Task<(JsonElement List, int TodayValue, List<int> Values)?> TrainingMonthAnchor(
		Func<Task<List<JsonElement>>> page, JsonElement list)
	{
		var values = MonthValues(list);
		if (values.Count == 0) return null;

		var showing = MonthValue(list);
		var probe = values.FirstOrDefault(v => v != showing, values[0]);
		if (probe != showing)
		{
			await Fm.Get("select", ("ptr", Field(list, "Pointer")), ("value", probe.ToString()));
			await Until(async () => ActiveMonthList(await page()) is { ValueKind: JsonValueKind.Object } now
				&& MonthValue(now) == probe, seconds: 4, every: 0.2);
		}

		// The catalogue's "Today" is enabled only when the month on show is not today's; a click
		// straight after a month change can still find the old, disabled button, so it is waited
		// for rather than taken on the first look.
		static bool IsTodayButton(JsonElement b) =>
			Field(b, "Label") == "Today" && Field(b, "Events").Contains("SetDatastoreValue", StringComparison.Ordinal);
		if (probe != showing)
			await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(IsTodayButton) is { ValueKind: JsonValueKind.Object } b && Field(b, "Enabled") == "True",
				seconds: 4, every: 0.2);

		// The calendar's own "Today" button: the one in the tab's form when it is there, and the
		// toolbar's when the form was read before it was drawn (the catalogue holds it by the
		// event it sends, not by its words).
		var todayButton = (await page()).FirstOrDefault(f => Field(f, "Kind") == "button" && Field(f, "Text") == "Today");
		if (todayButton.ValueKind != JsonValueKind.Object)
			todayButton = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Label") == "Today" && Field(b, "Events").Contains("SetDatastoreValue", StringComparison.Ordinal));
		if (todayButton.ValueKind == JsonValueKind.Object)
		{
			var clicked = await Fm.Get("click", ("ptr", Field(todayButton, "Pointer")));
			// A click can land on the button of a tab just left; finding it once more costs little.
			if (clicked.StartsWith("error", StringComparison.Ordinal))
			{
				await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.FirstOrDefault(IsTodayButton) is { ValueKind: JsonValueKind.Object } b && Field(b, "Enabled") == "True",
					seconds: 2, every: 0.15);
				var again = (await page()).FirstOrDefault(f => Field(f, "Kind") == "button" && Field(f, "Text") == "Today");
				if (again.ValueKind != JsonValueKind.Object)
					again = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(IsTodayButton);
				if (again.ValueKind == JsonValueKind.Object)
					clicked = await Fm.Get("click", ("ptr", Field(again, "Pointer")));
			}
			if (!clicked.StartsWith("error", StringComparison.Ordinal))
			{
				var previous = int.MinValue;
				await Until(async () =>
				{
					var now = ActiveMonthList(await page());
					var at = now.ValueKind == JsonValueKind.Object ? MonthValue(now) : int.MinValue;
					var settled = at != int.MinValue && at != probe && at == previous;
					previous = at;
					return settled;
				}, seconds: 8, every: 0.3);
			}
		}

		var anchor = ActiveMonthList(await page());
		if (anchor.ValueKind != JsonValueKind.Object) return null;
		var todayValue = MonthValue(anchor);
		// "Today" was asked to move the list off the month it was put on; if it did not, the
		// list's value for today is not known and the month is not guessed.
		if (todayValue == int.MinValue || (probe != showing && todayValue == probe)) return null;
		return (anchor, todayValue, MonthValues(anchor));
	}

	/// <summary>
	/// The Training tab's grid read as a form, by each text's data path: for each
	/// week its card (TrainingFocusCard), then its days, each a day (day.DayOfTheMonth) and three
	/// buttons. A match day's middle button is the competition ("SL", "UEL"). The grid starts on the
	/// Monday on or before the month's first day; a day out of step ends the reading.
	/// </summary>
	private static async Task<List<ScheduleDay>?> ReadTrainingGrid(DateTime shown)
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
	/// "today", "tomorrow", or a weekday ("friday": the next Friday, today included). Says how many words it took.
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
		return L($"week of {monday.Day} {Months[monday.Month - 1]}");
	}
}
