using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The training schedule (FINDINGS §8.72): Squad › Training › Overview draws this week and the
/// next as a calendar. Read as text it is the weekday headings ("Mon" … "Sun"), then day after
/// day: the day ("29 Sep" where the grid or a month starts, "30" after) and its three sessions —
/// a session's name ("Recovery", "Match Tactics", "Rest", "Travel"…) or, on a match day, where the
/// match is played ("H", "A", "N").
/// </summary>
internal static partial class Program
{
	private sealed record TrainingDay(DateTime Day, List<string> Sessions);

	private static async Task<int> Schedule(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Training", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// "Overview" is on every subtab menu of the tab, so the Training one is chosen by its pointer.
		// It is chosen even when it says so already: the menu keeps its choice while the squad's
		// own overview is up, and there the calendar is drawn without its days.
		var menu = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray()
			.FirstOrDefault(d => Field(d, "Name") == "SecondaryTabDropdownButton" && Field(d, "Label") == "Training");
		if (menu.ValueKind == JsonValueKind.Object)
			await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("choice", "Overview"), ("fire", "1"));

		// The calendar is drawn over a few frames; it is read once two readings in a row agree.
		List<string> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Thur"), ("up", "3"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var now = string.Join("|", texts);
			var settled = now == previous && texts.IndexOf("Sun") is var sun && sun >= 0 && texts.Count > sun + 7;
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);

		var state = await Fm.GetJson("state");
		var today = DateTime.TryParseExact(Field(state, "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var days = TrainingDays(texts.Skip(texts.IndexOf("Sun") + 1).ToList(), today);
		if (days.Count == 0)
		{
			Console.Error.WriteLine("Antrenman programı okunamadı.");
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(days.Select(x => new { Date = x.Day.ToString("yyyy-MM-dd"), x.Sessions })));
			return 0;
		}

		static string Session(string s) => s switch
		{
			"H" => "MAÇ (iç saha)",
			"A" => "MAÇ (deplasman)",
			"N" => "MAÇ (tarafsız)",
			"Rest" => "dinlenme",
			"Travel" => "yolculuk",
			_ => s,
		};
		static string Day(DateTime x) => $"{ShortDays[(int)x.DayOfWeek]} {x.Day} {Months[x.Month - 1]}";

		if (!Ui.Rich)
		{
			Console.WriteLine("GÜN\t1. SEANS\t2. SEANS\t3. SEANS");
			foreach (var x in days)
				Console.WriteLine($"{(x.Day == today ? "*" : "")}{Day(x.Day)}\t{string.Join("\t", x.Sessions.Select(Session))}");
			return 0;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
			.Title("[bold]Antrenman programı[/]")
			.AddColumn("Gün").AddColumn("1. seans").AddColumn("2. seans").AddColumn("3. seans");
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
		table.Caption("[dim]▶ bugün · soluk: geçmiş günler ve dinlenme[/]");
		Ui.Out.Write(table);
		return 0;
	}

	/// <summary>
	/// Splits the calendar's texts into days: a day mark ("29 Sep" or "30") and the sessions after it.
	/// The grid's first day names its month; the year is the one that puts it nearest today.
	/// </summary>
	private static List<TrainingDay> TrainingDays(List<string> texts, DateTime today)
	{
		List<TrainingDay> days = [];
		DateTime? next = null;
		foreach (var text in texts)
		{
			var mark = DayMark.Match(text);
			if (mark.Success)
			{
				var day = int.Parse(mark.Groups[1].Value);
				DateTime date;
				if (mark.Groups[2].Success)
				{
					var month = Array.IndexOf(MonthNames, mark.Groups[2].Value) + 1;
					date = new[] { today.Year - 1, today.Year, today.Year + 1 }
						.Select(y => new DateTime(y, month, Math.Min(day, DateTime.DaysInMonth(y, month))))
						.MinBy(x => Math.Abs((x - today).TotalDays));
				}
				else if (next is { } n && n.Day == day) date = n;
				else return days; // a day out of step: what was read is all that can be trusted
				days.Add(new TrainingDay(date, []));
				next = date.AddDays(1);
			}
			else if (days.Count > 0) days[^1].Sessions.Add(text);
		}
		return days;
	}
}
