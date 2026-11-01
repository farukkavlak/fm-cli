using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The club's calendar: Portal › Calendar draws a month as a grid, chosen with its
/// MonthDropdown. Each text in the grid is read with the data path it is bound to:
/// "day.DayOfTheMonth" starts a day, and what is on it is an event whose fields say what they are —
/// "Event.Competition.Name", "Event.KickoffTime", "Event.TeamOpponent.Name", "Event.MatchVenueShort",
/// "Event.DisplayString" for a note. Nothing is told apart by its words, so it reads the same in any language.
/// </summary>
internal static partial class Program
{
	private sealed record CalendarMatch(DateTime Day, string Competition, string TimeOrScore, string Opponent, string Place, string Venue);

	private sealed record CalendarDay(DateTime Day, List<CalendarMatch> Matches, List<string> Notes, List<string> Youth);

	/// <summary>
	/// A month the calendar offers: the number its list gives it (counted from the season's first
	/// month), the month that is, and how the list writes it.
	/// </summary>
	private sealed record CalendarMonthChoice(int Value, int Year, int Month, string Label);

	// What a typed "14 oct" names (TrainingSchedule.ScheduleDate); the calendar itself is read by its data paths.
	private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

	/// <summary>Opens the calendar on a month and reads its grid; null when it cannot.</summary>
	private static async Task<List<CalendarDay>> CalendarMonth(CalendarMonthChoice month) =>
		// Straight after the page opens the month can be chosen before the grid listens.
		await CalendarMonthOnce(month) ?? await CalendarMonthOnce(month);

	private static async Task<List<CalendarDay>> CalendarMonthOnce(CalendarMonthChoice month)
	{
		await Fm.Get("select", ("name", "MonthDropdown"), ("value", month.Value.ToString()));
		// The grid is drawn over a few frames; it is read once two readings in a row agree.
		List<(string Text, string Path)> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", month.Label), ("up", "14"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			var now = string.Join("|", texts.Select(t => t.Text));
			var settled = now == previous && texts.Count(t => t.Path == "day.DayOfTheMonth") >= 28;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.3);

		var marks = texts.Where(t => t.Path == "day.DayOfTheMonth").ToList();
		if (marks.Count < 28) return null;
		// A note the plugin puts into English whole ("Transfer window closes (midnight)") and the
		// nations of another side's match come back without the game's words; those are asked for, parts too.
		await GameWords.AskWithParts(Fm, texts.Where(t => t.Path.StartsWith("Event.", StringComparison.Ordinal)).Select(t => t.Text));

		// The grid starts on a Monday, which is in the month before unless the month starts on one,
		// and ends in the month after; a day numbered lower than the one before starts a month.
		var first = new DateTime(month.Year, month.Month, 1);
		var day = first.AddDays(-(((int)first.DayOfWeek + 6) % 7));
		var days = new List<CalendarDay>();
		CalendarDay today = null;
		Dictionary<string, string> ev = null;

		foreach (var (text, path) in texts.Append(("", "day.DayOfTheMonth")))
		{
			if (path == "day.DayOfTheMonth")
			{
				Close();
				if (text.Length == 0) break;
				if (today != null) day = day.AddDays(1);
				if (!int.TryParse(Regex.Match(text, @"^\d+").Value, out var number) || number != day.Day) return null;
				today = new CalendarDay(day, [], [], []);
				days.Add(today);
				continue;
			}
			if (today == null) continue;

			// A note is an event of its own; a match's fields follow its competition. What a day is
			// for (Event.Title: training, rest, match) adds nothing the fields do not.
			if (path == "Event.DisplayString")
			{
				Close();
				today.Notes.Add(text);
				continue;
			}
			if (path.StartsWith("Event.Competition.", StringComparison.Ordinal)) Close();
			if (path is "Event.Title" || !path.StartsWith("Event.", StringComparison.Ordinal))
			{
				continue;
			}
			ev ??= [];
			var key = path["Event.".Length..];
			if (ev.ContainsKey(key)) { Close(); ev = []; }
			ev[key] = text;
		}
		return days;

		void Close()
		{
			if (ev == null || today == null) { ev = null; return; }
			var fields = ev;
			ev = null;
			string Get(string prefix) => fields.FirstOrDefault(f => f.Key.StartsWith(prefix, StringComparison.Ordinal)).Value ?? "";

			var opponent = Get("TeamOpponent.");
			if (opponent.Length == 0) return;
			var team = Get("Team.");
			var timeOrScore = Get("Result") is { Length: > 0 } result ? result : Get("KickoffTime");
			var venue = Get("MatchVenueShort");

			// Another side's match (a national team's, a youth international) names both teams.
			if (team.Length > 0) today.Notes.Add($"{GameWords.Of(team)} {(timeOrScore.Length > 0 ? timeOrScore : "-")} {GameWords.Of(opponent)}");
			// One of our youth sides' is written with the competition's short code.
			else if (!fields.ContainsKey("Competition.Name") && fields.ContainsKey("Competition.ShortName"))
				today.Youth.Add(L($"Youth {Get("Competition.ShortName")}: {opponent} ({venue}) {timeOrScore}"));
			else today.Matches.Add(new(today.Day, Get("Competition."), timeOrScore, opponent, Get("LeaguePosition"), venue));
		}
	}

	/// <summary>The months the calendar offers and the one showing.</summary>
	private static async Task<(List<CalendarMonthChoice> Months, CalendarMonthChoice Showing)?> CalendarMonths()
	{
		var opened = await OpenPage(Screen.Calendar);
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Report(opened);
			return null;
		}
		JsonElement list = default;
		await Until(async () => (list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => Field(d, "Name") == "MonthDropdown")).ValueKind == JsonValueKind.Object
			&& list.GetProperty("Choices").GetArrayLength() > 0, seconds: 8, every: 0.5);
		if (list!.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The calendar page has no month picker."));
			return null;
		}

		// The list counts months from the season's first, which the names (the game's own, in its
		// language) would have to be read for. The calendar keeps today's month as an entry of that
		// list (TodayMonthDropdownEntry, what its Today button writes to it), so the rest count from it.
		if (!DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var today))
		{
			Console.Error.WriteLine(L("The game's date could not be read."));
			return null;
		}
		int? fromToday = null;
		await Until(async () => int.TryParse(Field(await Fm.Pull("Human.GeneralCalendar.TodayMonthDropdownEntry", "PropertyValue"), "PropertyValue"), out var v)
			&& (fromToday = v) != null, seconds: 3, every: 0.2);
		var labels = list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var values = list.GetProperty("Values").EnumerateArray().Select(v => v.ValueKind == JsonValueKind.Number ? v.GetInt32() : int.MinValue).ToList();
		var at = int.TryParse(Field(list, "Selected"), out var chosen) && chosen >= 0 && chosen < values.Count ? chosen : 0;
		var todayValue = fromToday ?? (values[at] == int.MinValue ? 0 : values[at]);
		var months = new List<CalendarMonthChoice>();
		for (var i = 0; i < labels.Count && i < values.Count; i++)
		{
			if (values[i] == int.MinValue) continue;
			var date = new DateTime(today.Year, today.Month, 1).AddMonths(values[i] - todayValue);
			months.Add(new(values[i], date.Year, date.Month, labels[i]));
		}
		var current = months.FirstOrDefault(m => m.Value == todayValue);
		return (months, current);
	}

	/// <summary>
	/// A month of the calendar: matches, under-19s matches and the notes on each day, days with
	/// only training left out. The month is "september", "9" or "2025-09"; this month
	/// when none is given.
	/// </summary>
	private static async Task<int> CalendarCommand(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (await CalendarMonths() is not var (months, showing)) return 1;

		var wanted = string.Join(" ", Words(args));
		var month = wanted.Length == 0 ? showing : PickMonth(months, wanted);
		if (month == null)
		{
			Console.Error.WriteLine(L($"'{wanted}' is not in the calendar. Months: {string.Join(", ", months.Select(m => $"{Months[m.Month - 1]} {m.Year}"))}"));
			return 1;
		}

		var days = await CalendarMonth(month);
		if (days == null)
		{
			Console.Error.WriteLine(L("The calendar could not be read."));
			return 1;
		}
		var busy = days.Where(d => d.Day.Month == month.Month && (d.Matches.Count > 0 || d.Notes.Count > 0 || d.Youth.Count > 0)).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(busy.Select(d => d with
			{
				Matches = d.Matches.Select(m => m with { Competition = GameWords.Of(m.Competition), Opponent = GameWords.Of(m.Opponent), Venue = GameWords.Of(m.Venue) }).ToList(),
				Notes = d.Notes.Select(GameWords.Of).ToList(), Youth = d.Youth.Select(GameWords.Of).ToList(),
			})));
			return 0;
		}

		Console.WriteLine($"{Months[month.Month - 1]} {month.Year}");
		foreach (var day in busy)
		{
			var label = $"{day.Day.Day,2} {ShortDays[(int)day.Day.DayOfWeek]}";
			var notes = string.Join(" · ", day.Notes);
			var lines = day.Matches.Select(m => MatchLine(m)).ToList();
			if (notes.Length > 0) lines.Add(Ui.Rich ? $"[dim]{Ui.Safe(notes)}[/]" : notes);
			if (args.Contains("--u19")) lines.AddRange(day.Youth.Select(y => Ui.Rich ? $"[dim]{Ui.Safe(y)}[/]" : y));
			if (lines.Count == 0) continue;
			for (var i = 0; i < lines.Count; i++)
			{
				var head = i == 0 ? label : new string(' ', label.Length);
				if (Ui.Rich) Ui.Line($"  {head}  {lines[i]}");
				else Console.WriteLine($"  {head}  {lines[i]}");
			}
		}
		return 0;

		string MatchLine(CalendarMatch m)
		{
			var venue = m.Venue switch { "H" => L("home"), "A" => L("away"), "N" => L("neutral"), _ => "" };
			var score = Regex.Match(m.TimeOrScore, @"^(\d+)-(\d+)(p|e)?$");
			var text = $"{m.Opponent}{(m.Place.Length > 0 ? $" ({m.Place})" : "")} · {venue} · {m.Competition} · {(score.Success ? Ours(m) : m.TimeOrScore)}";
			if (!Ui.Rich) return text;
			var colour = !score.Success ? "white" : MatchOutcome(m) switch { > 0 => "green", < 0 => "red", _ => "yellow" };
			return $"[bold {colour}]{Ui.Safe(text)}[/]";
		}
	}

	/// <summary>The score as ours–theirs: the calendar writes it home–away.</summary>
	private static string Ours(CalendarMatch m)
	{
		var s = Regex.Match(m.TimeOrScore, @"^(\d+)-(\d+)(p|e)?$");
		if (!s.Success) return m.TimeOrScore;
		// "p": settled on penalties, "e": after extra time; the mark stays with the score.
		var mark = s.Groups[3].Success ? $" ({(s.Groups[3].Value == "p" ? "pen." : L("e.t."))})" : "";
		return (m.Venue == "A" ? $"{s.Groups[2].Value}-{s.Groups[1].Value}" : $"{s.Groups[1].Value}-{s.Groups[2].Value}") + mark;
	}

	private static int MatchOutcome(CalendarMatch m)
	{
		var s = Regex.Match(Ours(m), @"^(\d+)-(\d+)");
		return s.Success ? Math.Sign(int.Parse(s.Groups[1].Value) - int.Parse(s.Groups[2].Value)) : 0;
	}

	/// <summary>A month by "september", "9" or "2025-09"; the first that starts so otherwise.</summary>
	private static CalendarMonthChoice PickMonth(List<CalendarMonthChoice> months, string wanted)
	{
		var w = Fold(wanted);
		string[] Names(CalendarMonthChoice m)
		{
			var english = Fold(CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m.Month));
			var turkish = Fold(Months[m.Month - 1]);
			return [english, turkish, m.Month.ToString(), $"{m.Year}-{m.Month:00}", $"{english} {m.Year}", $"{turkish} {m.Year}", Fold(m.Label)];
		}
		return months.FirstOrDefault(m => Names(m).Contains(w))
			?? months.FirstOrDefault(m => Names(m).Any(n => n.StartsWith(w, StringComparison.Ordinal)));
	}
}
