using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>The league table for a terminal: the places that go somewhere marked down the side, and the team picked out.</summary>
internal static class Standings
{
	public static void Draw(JsonElement table, string own)
	{
		// The league's own page binds a shorter table, without wins, draws, losses and goals.
		var full = table.EnumerateArray().Any(r => F(r, "LeagueGamesWon").Length > 0);

		var grid = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) }
			.AddColumn(new TableColumn("[dim]#[/]").RightAligned())
			.AddColumn(new TableColumn(""))
			.AddColumn(new TableColumn(L("[dim]TEAM[/]")))
			.AddColumn(new TableColumn($"[dim]{L("P")}[/]").RightAligned());
		if (full)
			grid.AddColumn(new TableColumn($"[dim]{L("W")}[/]").RightAligned())
				.AddColumn(new TableColumn($"[dim]{L("D")}[/]").RightAligned())
				.AddColumn(new TableColumn($"[dim]{L("L")}[/]").RightAligned())
				.AddColumn(new TableColumn($"[dim]{L("GD")}[/]").RightAligned());
		grid.AddColumn(new TableColumn($"[dim]{L("PTS")}[/]").RightAligned());
		var form = table.EnumerateArray().Any(r => F(r, "RecentFormValues").Length > 0);
		if (form) grid.AddColumn(new TableColumn("[dim]FORM[/]"));

		foreach (var row in table.EnumerateArray())
		{
			var mine = F(row, "Name") == own;
			string Cell(string text) => mine ? $"[bold yellow]{Ui.Safe(text)}[/]" : Ui.Safe(text);

			var difference = F(row, "LeagueGoalDifference");
			var shownDifference = int.TryParse(difference, out var d) && d > 0 ? "+" + difference : difference;

			var cells = new List<string>
			{
				Cell(F(row, "TablePosition")),
				Zone(F(row, "StageQualificationPlaceColorGroupId")),
				Cell(F(row, "Name") + (F(row, "Fate") is { Length: > 0 } fate ? $" ({fate})" : "") + (mine ? "  ◀" : "")),
				Cell(F(row, "LeagueGamesPlayed")),
			};
			if (full) cells.AddRange([Cell(F(row, "LeagueGamesWon")), Cell(F(row, "LeagueGamesDrawn")), Cell(F(row, "LeagueGamesLost")), Cell(shownDifference)]);
			cells.Add($"[bold{(mine ? " yellow" : "")}]{Ui.Safe(F(row, "LeaguePoints"))}[/]");
			// Coloured by the game's own codes (3 won, 0 lost, else drawn), the letter only shown.
			if (form) cells.Add(string.Join(" ", F(row, "RecentFormValues").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim())
				.Where(c => c is "0" or "1" or "2" or "3")
				.Select(c => c switch { "3" => $"[green]{L("W")}[/]", "0" => $"[red]{L("L")}[/]", _ => $"[yellow]{L("D")}[/]" })));
			grid.AddRow(cells.ToArray());
		}

		Ui.Out.Write(grid);
	}

	/// <summary>The fixtures, each result in the colour of what it was for us.</summary>
	public static void Fixtures(string[] headers, List<string[]> rows, List<string> kinds)
	{
		var table = new Spectre.Console.Table { Border = TableBorder.Rounded, BorderStyle = new Style(Color.Grey) };
		foreach (var header in headers) table.AddColumn(new TableColumn($"[dim]{header}[/]"));

		for (var i = 0; i < rows.Count; i++)
		{
			var cells = rows[i].Select(Ui.Safe).ToArray();
			var result = cells[^1];
			if (result.Length > 0)
				cells[^1] = (i < kinds.Count ? kinds[i] : "") switch
				{
					"1" or "3" => $"[black on green1] G [/] {result}",
					"9" => $"[black on grey] B [/] {result}",
					"10" => $"[white on red] M [/] {result}",
					_ => $"[grey] · [/] {result}",
				};
			else cells = cells.Select(c => $"[dim]{c}[/]").ToArray();
			table.AddRow(cells);
		}

		Ui.Out.Write(table);
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
	/// <summary>
	/// A team's last league results, oldest first, as the game codes them: 3 a win, 0 a defeat,
	/// 1 a goalless draw and 2 a score draw.
	/// </summary>
	public static string Form(string codes) =>
		string.Concat(codes.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim() switch { "3" => L("W"), "0" => L("L"), "1" or "2" => L("D"), _ => "" }));
}
