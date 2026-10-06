using System.Text.Json;
using Spectre.Console;

namespace FmCli;

internal static class InboxView
{
	/// <summary>
	/// The inbox for a terminal: unread in bold, what holds the game up in red, and the opening
	/// line of each message under its subject.
	/// </summary>
	public static void Draw(JsonElement inbox)
	{
		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]#[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]SAAT[/]"))
			.AddColumn(new TableColumn("[dim]GÖNDEREN[/]"))
			.AddColumn(new TableColumn("[dim]KONU[/]"))
			.AddColumn(new TableColumn(""));

		var number = 0;
		foreach (var message in inbox.EnumerateArray())
		{
			number++;
			var unread = Field(message, "NewsItemHasBeenRead") != "True";
			var title = Ui.Safe(Field(message, "NewsTitleString"));
			var preview = Field(message, "NewsFirstBubbleSpeechString");
			var subject = (unread ? $"[bold]{title}[/]" : title)
			              + (preview.Length > 0 ? $"\n[dim]{Ui.Safe(Ui.Fit(preview.Replace('\n', ' '), 90))}[/]" : "");

			var state = Program.Blocking(message) ? "[red bold]cevap bekliyor[/]"
				: Program.Answerable(message) ? "[yellow]cevaplanabilir[/]"
				: unread ? "[deepskyblue1]●[/]" : "";

			table.AddRow($"[dim]{number}[/]", Field(message, "NewsItemTimeSent"), Ui.Safe(Field(message, "Sender")), subject, state);
		}

		Ui.Out.Write(table);
	}


	private static string Field(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}
