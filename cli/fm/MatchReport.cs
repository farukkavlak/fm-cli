using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// The last match the team played, read from its fixture without opening a screen: score,
/// scorers, player of the match, and the numbers side by side.
/// </summary>
internal static class MatchReport
{
	private static readonly (string Label, string Field)[] Numbers =
	[
		("Topla oynama", "Possession"),
		("Şut", "Shots"),
		("İsabetli şut", "ShotsOnTarget"),
		("Gol beklentisi", "ExpectedGoals"),
		("Korner", "Corners"),
		("Faul", "Fouls"),
	];

	public static async Task<int> Show(Bridge fm, bool json)
	{
		const string at = "Human.Team.LastFixture";
		var fields = string.Join(",", new[] { "HasResult", "HomeScore", "AwayScore", "GoalscorersString", "PlayerOfTheMatchRating", "Attendance" }
			.Concat(Numbers.SelectMany(n => new[] { "Home" + n.Field, "Away" + n.Field })));

		var fixture = await fm.Pull(at, fields);
		if (F(fixture, "HasResult") != "True")
		{
			Console.Error.WriteLine("Oynanmış bir maç yok.");
			return 1;
		}

		var home = F(await fm.Pull(at + ".HomeTeam", "Name"), "Name");
		var away = F(await fm.Pull(at + ".AwayTeam", "Name"), "Name");
		var star = F(await fm.Pull(at + ".PlayerOfTheMatch", "Name"), "Name");
		var competition = F(await fm.Pull(at + ".Competition", "Name"), "Name");

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(new { Home = home, Away = away, Competition = competition, PlayerOfTheMatch = star, Fixture = fixture }));
			return 0;
		}

		var score = $"{F(fixture, "HomeScore")}-{F(fixture, "AwayScore")}";
		var scorers = F(fixture, "GoalscorersString");
		var rating = Round(F(fixture, "PlayerOfTheMatchRating"), 1);

		if (!Ui.Rich)
		{
			Console.WriteLine($"{home} {score} {away}" + (competition.Length > 0 ? $"  ({competition})" : ""));
			if (scorers.Length > 0) Console.WriteLine($"Goller: {scorers.Replace("\n", "; ")}");
			if (star.Length > 0) Console.WriteLine($"Maçın oyuncusu: {star}" + (rating.Length > 0 ? $" ({rating})" : ""));
			foreach (var (label, field) in Numbers)
				if (F(fixture, "Home" + field) is { Length: > 0 } h) Console.WriteLine($"{label}: {Shown(field, h)} - {Shown(field, F(fixture, "Away" + field))}");
			return 0;
		}

		var grid = new Grid()
			.AddColumn(new GridColumn().RightAligned().NoWrap())
			.AddColumn(new GridColumn().RightAligned().NoWrap())
			.AddColumn(new GridColumn().Centered().NoWrap())
			.AddColumn(new GridColumn().NoWrap())
			.AddColumn(new GridColumn().NoWrap());

		foreach (var (label, field) in Numbers)
		{
			var h = Shown(field, F(fixture, "Home" + field));
			var a = Shown(field, F(fixture, "Away" + field));
			if (h.Length == 0 && a.Length == 0) continue;

			var (hv, av) = (Value(h), Value(a));
			var total = hv + av;
			const int width = 14;
			var hb = total > 0 ? (int)Math.Round(width * hv / total) : 0;
			var ab = total > 0 ? (int)Math.Round(width * av / total) : 0;
			var hc = hv >= av ? "yellow" : "grey";
			var ac = av >= hv ? "yellow" : "grey";

			grid.AddRow($"[{hc}]{new string('█', hb)}[/]", $"[bold]{Ui.Safe(h)}[/]", $"[dim]{label}[/]", $"[bold]{Ui.Safe(a)}[/]", $"[{ac}]{new string('█', ab)}[/]");
		}

		var (own, _) = await fm.OwnTeam();
		string Side(string team) => team == own ? $"[yellow]{Ui.Safe(team)}[/]" : Ui.Safe(team);
		var lines = new List<string> { $"[bold]{Side(home)}  {score}  {Side(away)}[/]" };
		if (competition.Length > 0) lines.Add($"[dim]{Ui.Safe(competition)}" + (F(fixture, "Attendance") is { Length: > 0 } crowd ? $" · {Ui.Safe(crowd)} seyirci" : "") + "[/]");
		if (scorers.Length > 0) lines.Add($"[dim]Goller:[/] {Ui.Safe(scorers.Replace("\n", " · "))}");
		if (star.Length > 0) lines.Add($"[dim]Maçın oyuncusu:[/] {Ui.Safe(star)}" + (rating.Length > 0 ? $" [green1]{Ui.Safe(rating)}[/]" : ""));

		Ui.Out.Write(new Panel(new Rows(new Markup(string.Join("\n", lines)), new Text(""), grid))
		{
			Header = new PanelHeader(" [bold]MAÇ RAPORU[/] "),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
		return 0;
	}

	/// <summary>A number as the game shows it: possession as a percentage, expected goals to two places.</summary>
	private static string Shown(string field, string value) => field switch
	{
		"Possession" when value.Length > 0 && !value.EndsWith('%') => value + "%",
		"ExpectedGoals" => Round(value, 2),
		_ => value,
	};

	private static string Round(string value, int places) =>
		double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			? v.ToString("F" + places, System.Globalization.CultureInfo.InvariantCulture)
			: value;

	/// <summary>"55%", "1.23" or "7" as a number to weigh the bars by.</summary>
	private static double Value(string shown) =>
		double.TryParse(shown.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}
