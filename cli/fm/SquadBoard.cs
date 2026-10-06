using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The first team at a glance for a terminal, read straight from the team's data so no screen
/// has to be opened for it: who, where, how fit and how sharp, in the game's own order.
/// </summary>
internal static class SquadBoard
{
	public const string Fields = "Name,SquadNumber,PositionCombinedString,Age,Condition,Sharpness,IsInjured";

	public static void Draw(JsonElement players)
	{
		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]NO[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]OYUNCU[/]"))
			.AddColumn(new TableColumn("[dim]MEVKİ[/]"))
			.AddColumn(new TableColumn("[dim]YAŞ[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]KONDİSYON[/]"))
			.AddColumn(new TableColumn("[dim]MAÇ FORMU[/]"))
			.AddColumn(new TableColumn(""));

		foreach (var player in players.EnumerateArray())
		{
			var injured = F(player, "IsInjured") == "True";
			table.AddRow(
				$"[dim]{Ui.Safe(F(player, "SquadNumber"))}[/]",
				injured ? $"[red]{Ui.Safe(F(player, "Name"))}[/]" : $"[bold]{Ui.Safe(F(player, "Name"))}[/]",
				Ui.Safe(F(player, "PositionCombinedString")),
				Ui.Safe(F(player, "Age")),
				Meter(F(player, "Condition")),
				Meter(F(player, "Sharpness")),
				injured ? "[red]sakat[/]" : "");
		}

		Ui.Out.Write(table);
	}

	/// <summary>A percentage as a short bar: green when ready, amber when not, red when it is a worry. An injured player's -1 is no reading at all.</summary>
	private static string Meter(string shown)
	{
		if (!int.TryParse(shown, out var value) || value < 0 || value > 100) return "[dim]—[/]";

		const int cells = 10;
		var full = (int)Math.Round(value / 100.0 * cells);
		var colour = value >= 90 ? "green1" : value >= 70 ? "yellow" : value >= 50 ? "darkorange" : "red";
		return $"[{colour}]{new string('█', full)}[/][grey23]{new string('·', cells - full)}[/] {value,3}%";
	}

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}
