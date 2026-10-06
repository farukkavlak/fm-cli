using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>The league table for a terminal: the places that go somewhere marked down the side, and the team picked out.</summary>
internal static class Standings
{
	public static void Draw(JsonElement table, string own)
	{
		var grid = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]#[/]").RightAligned())
			.AddColumn(new TableColumn(""))
			.AddColumn(new TableColumn("[dim]TAKIM[/]"))
			.AddColumn(new TableColumn("[dim]O[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]G[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]B[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]M[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]AV[/]").RightAligned())
			.AddColumn(new TableColumn("[dim]P[/]").RightAligned());

		foreach (var row in table.EnumerateArray())
		{
			var mine = F(row, "Name") == own;
			string Cell(string text) => mine ? $"[bold yellow]{Ui.Safe(text)}[/]" : Ui.Safe(text);

			var difference = F(row, "LeagueGoalDifference");
			var shownDifference = int.TryParse(difference, out var d) && d > 0 ? "+" + difference : difference;

			grid.AddRow(
				Cell(F(row, "TablePosition")),
				Zone(F(row, "StageQualificationPlaceColorGroupId")),
				Cell(F(row, "Name") + (mine ? "  ◀" : "")),
				Cell(F(row, "LeagueGamesPlayed")),
				Cell(F(row, "LeagueGamesWon")),
				Cell(F(row, "LeagueGamesDrawn")),
				Cell(F(row, "LeagueGamesLost")),
				Cell(shownDifference),
				$"[bold{(mine ? " yellow" : "")}]{Ui.Safe(F(row, "LeaguePoints"))}[/]");
		}

		Ui.Out.Write(grid);
	}

	/// <summary>
	/// The colour the game gives a place that leads somewhere: the title and Europe at the top,
	/// relegation at the bottom. The game names the group by a number only.
	/// </summary>
	private static string Zone(string group) => group switch
	{
		"0" or "" => " ",
		"1" => "[green1]▌[/]",
		"2" => "[deepskyblue1]▌[/]",
		"3" => "[darkorange]▌[/]",
		"4" => "[orchid]▌[/]",
		_ => "[red]▌[/]",
	};

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}
