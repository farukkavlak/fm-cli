using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The club's calendar (FINDINGS §8.66): Portal › Calendar draws a month as a grid,
/// chosen with its MonthDropdown. Read as text it is the weekday headings, then day after day:
/// the day ("28 Jul" where a month starts, "29" after), then what is on it — "Training", a match
/// ("Match", competition, kick-off or score, opponent, his league place, H/A/N), an under-19s
/// match ("U19", opponent, H/A, time or score), and notes such as "Transfer window opens".
/// </summary>
internal static partial class Program
{
	private sealed record CalendarMatch(DateTime Day, string Competition, string TimeOrScore, string Opponent, string Place, string Venue);

	private sealed record CalendarDay(DateTime Day, List<CalendarMatch> Matches, List<string> Notes, List<string> Youth);

	private static readonly string[] MonthNames = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

	private static readonly Regex DayMark = new(@"^(\d{1,2})(?: (Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec))?$");

	/// <summary>Opens the calendar on a month ("August 2025") and reads its grid; null when it cannot.</summary>
	private static async Task<List<CalendarDay>> CalendarMonth(string month) =>
		// Straight after the page opens the month can be chosen before the grid listens.
		await CalendarMonthOnce(month) ?? await CalendarMonthOnce(month);

	private static async Task<List<CalendarDay>> CalendarMonthOnce(string month)
	{
		await Fm.Get("select", ("name", "MonthDropdown"), ("choice", month));
		// The grid is drawn over a few frames; it is read once two readings in a row agree.
		List<string> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", month), ("up", "14"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var now = string.Join("|", texts);
			var settled = now == previous && texts.Contains("Sunday") && texts.Count > texts.IndexOf("Sunday") + 1;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.3);

		var parts = month.Split(' ');
		if (parts.Length != 2 || !int.TryParse(parts[1], out var year)) return null;
		var shown = DateTime.ParseExact(parts[0], "MMMM", CultureInfo.InvariantCulture).Month;
		var at = texts.IndexOf("Sunday");
		if (at < 0) return null;

		var days = new List<CalendarDay>();
		var monthNow = shown;
		CalendarDay today = null;
		for (var i = at + 1; i < texts.Count; i++)
		{
			var token = texts[i];
			var mark = DayMark.Match(token);
			if (mark.Success)
			{
				if (mark.Groups[2].Success) monthNow = Array.IndexOf(MonthNames, mark.Groups[2].Value) + 1;
				// The grid's first and last weeks reach into the months around it.
				var y = year + (monthNow - shown > 6 ? -1 : shown - monthNow > 6 ? 1 : 0);
				var d = int.Parse(mark.Groups[1].Value);
				if (d > DateTime.DaysInMonth(y, monthNow)) continue;
				today = new CalendarDay(new DateTime(y, monthNow, d), [], [], []);
				days.Add(today);
				continue;
			}
			if (today == null || token == "Training") continue;

			// A match's third cell is its kick-off or its score; a row where it is not (a youth
			// friendly drawn in the first team's style) is kept as a note.
			if (token == "Match" && i + 4 < texts.Count && !Regex.IsMatch(texts[i + 2], @"^(\d{1,2}:\d{2}|\d+-\d+[pe]?)$"))
			{
				today.Youth.Add(string.Join(" ", texts.Skip(i + 1).Take(4)));
				i += 4;
				continue;
			}
			if (token == "Match" && i + 4 < texts.Count)
			{
				var place = Regex.IsMatch(texts[i + 4], @"^\d+(st|nd|rd|th)$") ? texts[i + 4] : "";
				var venueAt = i + (place.Length > 0 ? 5 : 4);
				var venue = venueAt < texts.Count && texts[venueAt] is "H" or "A" or "N" ? texts[venueAt] : "";
				today.Matches.Add(new(today.Day, texts[i + 1], texts[i + 2], texts[i + 3], place, venue));
				i = venue.Length > 0 ? venueAt : venueAt - 1;
				continue;
			}
			// An under-19s match: a short code ("U19", "FR" for a friendly), the opponent's
			// youth side, H/A/N, then the time or the score.
			if (i + 3 < texts.Count && token.Length <= 3 && Regex.IsMatch(texts[i + 1], @"( U\d\ds| Yth Cands)$") && texts[i + 2] is "H" or "A" or "N")
			{
				// The code is the competition's ("U19", "3N"…); the opponent's name says which age group plays.
				today.Youth.Add($"Altyapı {token}: {texts[i + 1]} ({texts[i + 2]}) {texts[i + 3]}");
				i += 3;
				continue;
			}
			// An international is written "Bulgaria", "v", "Türkiye": one fixture, one note.
			if (token == "v" && today.Notes.Count > 0 && i + 1 < texts.Count)
			{
				today.Notes[^1] = $"{today.Notes[^1]} v {texts[++i]}";
				continue;
			}
			today.Notes.Add(token);
		}
		return days;
	}

	/// <summary>The months the calendar offers, as its dropdown names them, and the one showing.</summary>
	private static async Task<(List<string> Months, string Showing)?> CalendarMonths()
	{
		var opened = await OpenPage("Calendar", "Portal");
		if (opened.StartsWith("error", StringComparison.Ordinal))
		{
			Report(opened);
			return null;
		}
		JsonElement list = default;
		await Until(async () => (list = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => Field(d, "Name") == "MonthDropdown")).ValueKind == JsonValueKind.Object
			&& list.GetProperty("Choices").GetArrayLength() > 0, seconds: 8, every: 0.5);
		if (list.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine("Takvim sayfasında ay seçici yok.");
			return null;
		}
		return (list.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList(), Field(list, "Label"));
	}

	/// <summary>
	/// A month of the calendar: matches, under-19s matches and the notes on each day, days with
	/// only training left out. The month is "eylül", "september", "9" or "2025-09"; this month
	/// when none is given.
	/// </summary>
	private static async Task<int> CalendarCommand(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		if (await CalendarMonths() is not var (months, showing)) return 1;

		var wanted = string.Join(" ", Words(args));
		var month = wanted.Length == 0 ? CurrentMonth(months, showing) : PickMonth(months, wanted);
		if (month == null)
		{
			Console.Error.WriteLine($"'{wanted}' takvimde yok. Aylar: {string.Join(", ", months)}");
			return 1;
		}

		var days = await CalendarMonth(month);
		if (days == null)
		{
			Console.Error.WriteLine("Takvim okunamadı.");
			return 1;
		}
		var monthNumber = DateTime.ParseExact(month.Split(' ')[0], "MMMM", CultureInfo.InvariantCulture).Month;
		var busy = days.Where(d => d.Day.Month == monthNumber && (d.Matches.Count > 0 || d.Notes.Count > 0 || d.Youth.Count > 0)).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(busy));
			return 0;
		}

		var turkish = Months[monthNumber - 1];
		Console.WriteLine($"{turkish} {month.Split(' ')[1]}");
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
			var venue = m.Venue switch { "H" => "iç saha", "A" => "deplasman", "N" => "tarafsız", _ => "" };
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
		var mark = s.Groups[3].Success ? $" ({(s.Groups[3].Value == "p" ? "pen." : "uzt.")})" : "";
		return (m.Venue == "A" ? $"{s.Groups[2].Value}-{s.Groups[1].Value}" : $"{s.Groups[1].Value}-{s.Groups[2].Value}") + mark;
	}

	private static int MatchOutcome(CalendarMatch m)
	{
		var s = Regex.Match(Ours(m), @"^(\d+)-(\d+)");
		return s.Success ? Math.Sign(int.Parse(s.Groups[1].Value) - int.Parse(s.Groups[2].Value)) : 0;
	}

	/// <summary>The month the game is in, from its date; failing that, the one showing.</summary>
	private static string CurrentMonth(List<string> months, string showing)
	{
		var date = Field(Fm.GetJson("state").GetAwaiter().GetResult(), "Date");
		return DateTime.TryParseExact(date, "yyyy-MM-dd", null, DateTimeStyles.None, out var d)
			&& months.FirstOrDefault(m => m == d.ToString("MMMM yyyy", CultureInfo.InvariantCulture)) is { } found
			? found
			: showing;
	}

	private static string PickMonth(List<string> months, string wanted)
	{
		var w = Fold(wanted);
		var turkish = Months.Select(Fold).ToList();
		foreach (var month in months)
		{
			var parts = month.Split(' ');
			var number = DateTime.ParseExact(parts[0], "MMMM", CultureInfo.InvariantCulture).Month;
			string[] names = [Fold(parts[0]), turkish[number - 1], number.ToString(), $"{parts[1]}-{number:00}", Fold(month), $"{turkish[number - 1]} {parts[1]}"];
			if (names.Contains(w)) return month;
		}
		var starts = months.Where(m => Fold(m).StartsWith(w, StringComparison.Ordinal) || turkish[DateTime.ParseExact(m.Split(' ')[0], "MMMM", CultureInfo.InvariantCulture).Month - 1].StartsWith(w, StringComparison.Ordinal)).ToList();
		return starts.Count >= 1 ? starts[0] : null;
	}
}
