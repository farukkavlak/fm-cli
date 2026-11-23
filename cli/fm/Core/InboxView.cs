using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal static class InboxView
{
	/// <summary>
	/// The inbox for a terminal, a heading over each day's messages (the message's own date): unread marked ●,
	/// what holds the game up in red and first under its day, the opening line of each under its subject. Each
	/// keeps the number `inbox` gave it, which is what `reply` takes, whatever place it is drawn in.
	/// </summary>
	public static void Draw(JsonElement inbox)
	{
		var numbered = inbox.EnumerateArray().Select((m, i) => (Message: m, Number: i + 1)).ToList();
		var rows = numbered.Select(x =>
		{
			var message = x.Message;
			var unread = Field(message, "NewsItemHasBeenRead") != "True";
			var title = Ui.Safe(Field(message, "NewsTitleString"));
			var preview = GameWords.Of(Field(message, "NewsFirstBubbleSpeechString"));
			var blocking = Program.Blocking(message);
			var subject = (blocking ? $"[red bold]{title}[/]" : unread ? $"[bold]{title}[/]" : title)
			              + (preview.Length > 0 ? $"\n[dim]{Ui.Safe(Ui.Fit(preview.Replace('\n', ' '), 90))}[/]" : "");
			var state = blocking ? L("[red bold]awaiting an answer[/]")
				: Program.Answerable(message) ? L("[yellow]can be answered[/]")
				: "";
			return (x.Number, Day: Field(message, "Date"), Blocking: blocking, Cells: new[]
			{
				$"[dim]{x.Number}[/]", unread ? "[deepskyblue1]●[/]" : "", Field(message, "NewsItemTimeSent"), Ui.Safe(Field(message, "Sender")), subject, state,
			});
		}).ToList();

		// One set of widths for every day's rows, so the columns line up under the one heading.
		static int Shown(string markup) => Markup.Remove(markup).Split('\n').Max(l => l.Length);
		var widths = new int[6];
		for (var c = 0; c < 6; c++)
			widths[c] = rows.Select(r => Shown(r.Cells[c])).DefaultIfEmpty(0).Max();
		string[] heads = ["#", "", L("TIME"), L("FROM"), L("SUBJECT"), ""];
		for (var c = 0; c < 6; c++) widths[c] = Math.Max(widths[c], heads[c].Length);
		const int gap = 2;
		var room = Ui.Out.Profile.Width - 1;
		// A narrow terminal gives the subject the room: the sender is cut, marked.
		var senderRoom = room < 110 ? 16 : 24;
		widths[3] = Math.Min(widths[3], senderRoom);
		foreach (var r in rows) r.Cells[3] = Ui.Safe(Ui.Fit(Markup.Remove(r.Cells[3]), senderRoom));
		widths[4] = Math.Max(30, Math.Min(widths[4], room - widths.Where((_, i) => i != 4).Sum() - gap * 5));

		Spectre.Console.Table Part(bool headed)
		{
			var table = new Spectre.Console.Table { Border = TableBorder.None, ShowHeaders = headed };
			for (var c = 0; c < 6; c++)
				table.AddColumn(new TableColumn(heads[c].Length > 0 ? $"[dim]{Ui.Safe(heads[c])}[/]" : "")
				{
					Width = widths[c], Padding = new Padding(0, 0, c < 5 ? gap : 0, 0), Alignment = c == 0 ? Justify.Right : Justify.Left,
				});
			return table;
		}

		// Days in the order the list has them; within a day what holds the game up first, then as listed.
		var days = rows.GroupBy(r => r.Day).ToList();
		var dated = days.Any(d => d.Key.Length > 0);
		Ui.Out.Write(Part(headed: true));
		foreach (var day in days)
		{
			if (dated && day.Key.Length > 0)
			{
				Ui.Out.WriteLine();
				Ui.Out.Write(new Rule($"[bold]{Ui.Safe(Program.DayHeading(day.Key))}[/]") { Justification = Justify.Left, Style = new Style(Color.Grey) });
			}
			var part = Part(headed: false);
			foreach (var row in day.OrderByDescending(r => r.Blocking)) part.AddRow(row.Cells);
			Ui.Out.Write(part);
		}
	}

	private static string Field(JsonElement element, string name) =>
		JsonRead.Text(element, name);
}
