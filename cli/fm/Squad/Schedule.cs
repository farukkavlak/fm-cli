using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The training schedule: Training › Overview draws a calendar (FM26 this week and the next, FM27
/// the month), shown for this week and the next, read by each text's data path: the day, then its three
/// sessions — a session's name ("Recovery", "Match Tactics", "Rest", "Travel"…) or, on a match day,
/// where the match is played ("H", "A", "N").
/// </summary>
internal static partial class Program
{
	private sealed record TrainingDay(DateTime Day, List<string> Sessions);

	private static async Task<int> Schedule(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.Training);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// The page opened is the Training tab's Overview itself (its GUID): nothing is chosen by its words.

		// The calendar is drawn over a few frames; it is read once two readings in a row agree. It is
		// found by its days' data path, not a weekday's name: the squad overview left open underneath
		// has a week's headings of its own.
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

		var state = await Fm.GetJson("state");
		var today = DateTime.TryParseExact(Field(state, "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var days = TrainingDays(texts, today);
		if (days.Count == 0)
		{
			Console.Error.WriteLine(L("The training schedule could not be read."));
			return 1;
		}

		if (json)
		{
			// The plugin puts a session's name into English; the game's own word for it is put back
			// (GameWords) so `--json` says what the line output says. A match day's venue stays its
			// letter ("H"/"A"/"N", the same in every language), as everywhere else in the JSON.
			Console.WriteLine(JsonOut.Of(days.Select(x => new
			{
				Date = x.Day.ToString("yyyy-MM-dd"),
				Sessions = x.Sessions.Select(s => s is "H" or "A" or "N" ? s : GameWords.Of(s)).ToList(),
			})));
			return 0;
		}

		static string Session(string s) => s switch
		{
			"H" => L("MATCH (home)"),
			"A" => L("MATCH (away)"),
			"N" => L("MATCH (neutral)"),
			"Rest" => L("rest"),
			"Travel" => L("travel"),
			_ => s,
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
			.AddColumn(L("Day")).AddColumn("1. seans").AddColumn("2. seans").AddColumn("3. seans");
		foreach (var x in days)
		{
			// A new week starts on Monday; the weeks are kept apart.
			if (x.Day.DayOfWeek == DayOfWeek.Monday && x != days[0]) table.AddEmptyRow();
			var past = x.Day < today;
			string Cell(string s)
			{
				var text = Ui.Safe(Session(s));
				if (s is "H" or "A" or "N") return past ? $"[dim]{text}[/]" : $"[bold yellow]{text}[/]";
				return past || s is "Rest" ? $"[dim]{text}[/]" : text;
			}
			var label = x.Day == today ? $"[bold]▶ {Ui.Safe(Day(x.Day))}[/]" : past ? $"[dim]  {Ui.Safe(Day(x.Day))}[/]" : $"  {Ui.Safe(Day(x.Day))}";
			table.AddRow([label, .. x.Sessions.Select(Cell)]);
		}
		table.Caption(L("[dim]▶ today · faded: past days and rest[/]"));
		Ui.Out.Write(table);
		return 0;
	}

	/// <summary>
	/// Splits the calendar's texts into days by their data paths: a day (day.DayOfTheMonth) and its
	/// sessions (Event.SessionName), or where a match is played on a match day (Event.MatchVenueShort,
	/// "H"/"A"/"N" whatever the language). FM26 draws this week and the next, from Monday; FM27 the
	/// month's weeks. The grid's first date is where today's number falls in its run of day numbers
	/// (a month's name on the first is not read); this week and the next are kept.
	/// </summary>
	private static List<TrainingDay> TrainingDays(List<(string Text, string Path)> texts, DateTime today)
	{
		List<(int Number, List<string> Sessions)> cells = [];
		foreach (var (text, path) in texts)
		{
			if (path == "day.DayOfTheMonth")
			{
				if (!int.TryParse(System.Text.RegularExpressions.Regex.Match(text, @"^\d+").Value, out var number)) break;
				cells.Add((number, []));
			}
			else if (cells.Count > 0 && path is "Event.SessionName" or "Event.MatchVenueShort") cells[^1].Sessions.Add(text);
		}

		var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
		for (var at = 0; at < cells.Count; at++)
		{
			var start = today.AddDays(-at);
			if (start.DayOfWeek != DayOfWeek.Monday || cells.Select((c, i) => c.Number == start.AddDays(i).Day).Any(ok => !ok)) continue;
			return cells.Select((c, i) => new TrainingDay(start.AddDays(i), c.Sessions))
				.Where(x => x.Day >= monday && x.Day < monday.AddDays(14)).ToList();
		}
		return [];
	}
}
