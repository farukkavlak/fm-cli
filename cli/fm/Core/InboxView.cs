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
		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]#[/]").RightAligned())
			.AddColumn(new TableColumn(""))
			.AddColumn(new TableColumn(L("[dim]TIME[/]")))
			.AddColumn(new TableColumn(L("[dim]FROM[/]")))
			.AddColumn(new TableColumn(L("[dim]SUBJECT[/]")))
			.AddColumn(new TableColumn(""));

		var numbered = inbox.EnumerateArray().Select((m, i) => (Message: m, Number: i + 1)).ToList();
		// Days in the order the list has them; within a day what holds the game up first, then as listed.
		var days = numbered.GroupBy(x => Field(x.Message, "Date")).ToList();
		var dated = days.Any(d => d.Key.Length > 0);
		foreach (var day in days)
		{
			if (dated && day.Key.Length > 0)
			{
				if (table.Rows.Count > 0) table.AddEmptyRow();
				table.AddRow("", "", "", "", $"[bold underline]{Ui.Safe(Program.DayHeading(day.Key))}[/]", "");
			}
			foreach (var (message, number) in day.OrderByDescending(x => Program.Blocking(x.Message)))
			{
				var unread = Field(message, "NewsItemHasBeenRead") != "True";
				var title = Ui.Safe(Field(message, "NewsTitleString"));
				var preview = GameWords.Of(Field(message, "NewsFirstBubbleSpeechString"));
				var blocking = Program.Blocking(message);
				var subject = (blocking ? $"[red bold]{title}[/]" : unread ? $"[bold]{title}[/]" : title)
				              + (preview.Length > 0 ? $"\n[dim]{Ui.Safe(Ui.Fit(preview.Replace('\n', ' '), 90))}[/]" : "");
				var state = blocking ? L("[red bold]awaiting an answer[/]")
					: Program.Answerable(message) ? L("[yellow]can be answered[/]")
					: "";
				table.AddRow($"[dim]{number}[/]", unread ? "[deepskyblue1]●[/]" : "", Field(message, "NewsItemTimeSent"), Ui.Safe(Field(message, "Sender")), subject, state);
			}
		}

		Ui.Out.Write(table);
	}

	private static string Field(JsonElement element, string name) =>
		JsonRead.Text(element, name);
}
