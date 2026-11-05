using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The training schedule: Training › Overview, shown for this week and the next. FM27 draws each
/// session as a button whose sent value names its day, place and kind (Date, SessionIndex,
/// SessionType); a match day's match is the place with no button, told from the team's fixture on
/// that date. FM26 drew a calendar of texts, read by each text's data path.
/// </summary>
internal static partial class Program
{
	/// <summary>A session: its name in the game's words, what it is ("rest", "travel", a match's venue "H"/"A"/"N", else ""), and the game's SessionType.</summary>
	private sealed record DaySession(string Name, string Kind, string Type = "");

	private sealed record TrainingDay(DateTime Day, List<DaySession> Sessions);

	// SessionType values the game gives a session that is no training.
	private const string RestSession = "300", TravelSession = "402";

	private static async Task<int> Schedule(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The page opened is the Training tab's Overview itself (its GUID): nothing is chosen by its words.
		var state = await Fm.GetJson("state");
		var today = DateTime.TryParseExact(Field(state, "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var days = await SessionDays(today) ?? TrainingDays(await CalendarTexts(), today, (await FixtureFacts()).ToDictionary(f => f.Key, f => f.Value.Venue));
		if (days.Count == 0)
		{
			Console.Error.WriteLine(L("The training schedule could not be read."));
			return 1;
		}

		if (json)
		{
			// A match day's venue stays its letter ("H"/"A"/"N"), as everywhere else in the JSON.
			Console.WriteLine(JsonOut.Of(days.Select(x => new
			{
				Date = x.Day.ToString("yyyy-MM-dd"),
				Sessions = x.Sessions.Select(s => IsMatch(s) ? s.Kind : s.Name).ToList(),
			})));
			return 0;
		}

		static bool IsMatch(DaySession s) => s.Kind is "H" or "A" or "N" or "match";
		static string Session(DaySession s) => s.Kind switch
		{
			"H" => L("MATCH (home)"),
			"A" => L("MATCH (away)"),
			"N" => L("MATCH (neutral)"),
			"match" => L("MATCH"),
			_ => s.Name,
		};
		static string Day(DateTime x) => $"{ShortDays[(int)x.DayOfWeek]} {x.Day} {Months[x.Month - 1]}";

		if (!Ui.Rich)
		{
			Console.WriteLine(L("DAY\tSESSION 1\tSESSION 2\tSESSION 3"));
			foreach (var x in days)
				Console.WriteLine($"{(x.Day == today ? "*" : "")}{Day(x.Day)}\t{string.Join("\t", x.Sessions.Select(Session))}");
			return 0;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
			.Title(L("[bold]Training schedule[/]"))
			.AddColumn(L("Day")).AddColumn(L("Session 1")).AddColumn(L("Session 2")).AddColumn(L("Session 3"));
		foreach (var x in days)
		{
			// A new week starts on Monday; the weeks are kept apart.
			if (x.Day.DayOfWeek == DayOfWeek.Monday && x != days[0]) table.AddEmptyRow();
			var past = x.Day < today;
			string Cell(DaySession s)
			{
				var text = Ui.Safe(Session(s));
				if (IsMatch(s)) return past ? $"[dim]{text}[/]" : $"[bold yellow]{text}[/]";
				return past || s.Kind == "rest" ? $"[dim]{text}[/]" : text;
			}
			var label = x.Day == today ? $"[bold]▶ {Ui.Safe(Day(x.Day))}[/]" : past ? $"[dim]  {Ui.Safe(Day(x.Day))}[/]" : $"  {Ui.Safe(Day(x.Day))}";
			table.AddRow([label, .. x.Sessions.Select(Cell)]);
		}
		table.Caption(L("[dim]▶ today · faded: past days and rest[/]"));
		Ui.Out.Write(table);
		return 0;
	}

	/// <summary>
	/// FM27's schedule from the session buttons on Training › Overview: each sends its day, place
	/// and kind (Date, SessionIndex, SessionType). Read until two readings in a row agree (the month is
	/// drawn over a few frames). A day's place with no button is its match, known by the team's fixture
	/// on that day (its venue letter). Null when the page has no such buttons (FM26).
	/// </summary>
	private static async Task<List<TrainingDay>?> SessionDays(DateTime today)
	{
		List<(DateTime Day, int Index, string Type, string Name)> read = [];
		var previous = "";
		var seen = false;
		await Until(async () =>
		{
			var all = (await Fm.GetJson("buttons", ("json", "1"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
				.Where(b => Field(b, "Events").Contains("EventFmxBeginTrainingWeekEditFromCard", StringComparison.Ordinal) && Field(b, "Sends").Length > 0).ToList();
			if (all.Count == 0) return false;
			seen = true;
			var sent = await PullEach(all.Select(b => (Field(b, "Sends"), "Date,SessionIndex,SessionType")).ToList());
			read = all.Zip(sent).Select(x => (
					Day: DateTime.TryParseExact(Field(x.Second, "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var day) ? day : DateTime.MinValue,
					Index: int.TryParse(Field(x.Second, "SessionIndex"), out var i) ? i : -1,
					Type: Field(x.Second, "SessionType"),
					Name: GameWords.Of(Field(x.First, "Label"))))
				.Where(x => x.Day != DateTime.MinValue && x.Index >= 0)
				.OrderBy(x => x.Day).ThenBy(x => x.Index).ToList();
			var now = string.Join("|", read);
			var settled = now == previous && read.Select(x => x.Day).Distinct().Count() >= 7;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (!seen) return null;

		var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
		var week = read.Where(x => x.Day >= monday && x.Day < monday.AddDays(14)).ToList();
		if (week.Count == 0) return [];
		var places = week.Max(x => x.Index) + 1;
		var fixtures = await TeamFixtures() ?? [];
		return week.GroupBy(x => x.Day).Select(g =>
		{
			var date = g.Key.ToString("yyyy-MM-dd");
			var match = fixtures.FirstOrDefault(f => f.Date == date);
			var sessions = Enumerable.Range(0, places).Select(i => g.FirstOrDefault(x => x.Index == i) is { Name: not null } x
				? new DaySession(x.Name, x.Type switch { RestSession => "rest", TravelSession => "travel", _ => "" }, x.Type)
				: new DaySession("", match is { VenueShort: "H" or "A" or "N" } ? match.VenueShort : "match")).ToList();
			return new TrainingDay(g.Key, sessions);
		}).ToList();
	}

	/// <summary>
	/// FM26's calendar texts, read once two readings in a row agree (it is drawn over a few frames). It is
	/// found by its days' data path, not a weekday's name: the squad overview left open underneath has a
	/// week's headings of its own.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> CalendarTexts()
	{
		List<(string Text, string Path)> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchorpath", "day.DayOfTheMonth"), ("up", "12"), ("paths", "1"), ("most", "1")))
				.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			var now = string.Join("|", texts.Select(t => t.Text));
			var settled = now == previous && texts.Count(t => t.Path == "day.DayOfTheMonth") >= 7;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		return texts;
	}

	/// <summary>
	/// FM26: splits the calendar's texts into days by their data paths: a day (day.DayOfTheMonth) and its
	/// sessions (Event.SessionName), or a match (Event.MatchVenueShort, whose letter is the game's shown
	/// one; the venue is taken from the fixture list's team ids by the day instead). A rest or travel
	/// session is not told apart here: only its shown name would say so. FM26 draws this week and the next, from Monday. The grid's first date is where today's number falls in its run of day numbers
	/// (a month's name on the first is not read); this week and the next are kept.
	/// </summary>
	private static List<TrainingDay> TrainingDays(List<(string Text, string Path)> texts, DateTime today, Dictionary<string, string> venues)
	{
		List<(int Number, List<(string Text, bool Match)> Sessions)> cells = [];
		foreach (var (text, path) in texts)
		{
			if (path == "day.DayOfTheMonth")
			{
				if (!int.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"^\d+").Value, out var number)) break;
				cells.Add((number, []));
			}
			else if (cells.Count > 0 && path is "Event.SessionName" or "Event.MatchVenueShort") cells[^1].Sessions.Add((text, path == "Event.MatchVenueShort"));
		}

		var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
		for (var at = 0; at < cells.Count; at++)
		{
			var start = today.AddDays(-at);
			if (start.DayOfWeek != DayOfWeek.Monday || cells.Select((c, i) => c.Number == start.AddDays(i).Day).Any(ok => !ok)) continue;
			return cells.Select((c, i) => new TrainingDay(start.AddDays(i), c.Sessions.Select(s => s.Match
					? new DaySession("", venues.GetValueOrDefault(start.AddDays(i).ToString("yyyy-MM-dd"), "match"))
					: new DaySession(GameWords.Of(s.Text), "")).ToList()))
				.Where(x => x.Day >= monday && x.Day < monday.AddDays(14)).ToList();
		}
		return [];
	}
}
