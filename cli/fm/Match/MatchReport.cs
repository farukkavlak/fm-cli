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
		(L("Possession"), "Possession"),
		(L("Shots"), "Shots"),
		(L("Shots on target"), "ShotsOnTarget"),
		(L("Expected goals"), "ExpectedGoals"),
		(L("Corners"), "Corners"),
		(L("Fouls"), "Fouls"),
	];

	public static async Task<int> Show(Bridge fm, bool json, string? expected = null)
	{
		const string at = "Human.Team.LastFixture";
		var fields = string.Join(",", new[] { "HasResult", "HomeScore", "AwayScore", "GoalscorersString", "PlayerOfTheMatchRating", "Attendance", "Result", "HomeTeamWon", "AwayTeamWon" }
			.Concat(Numbers.SelectMany(n => new[] { "Home" + n.Field, "Away" + n.Field })));

		// Straight after the whistle the last fixture is still the one before (seen: the old
		// match's teams and numbers under the new goals), so a caller that knows the result
		// waits for it to show.
		JsonElement fixture = default;
		string home = "", away = "", star = "", competition = "";
		var matched = expected == null;
		for (var attempt = 0; attempt < (expected == null ? 1 : 24); attempt++)
		{
			if (attempt > 0) await Task.Delay(500);
			fixture = await fm.Pull(at, fields);
			if (F(fixture, "HasResult") != "True") continue;
			home = F(await fm.Pull(at + ".HomeTeam", "Name"), "Name");
			away = F(await fm.Pull(at + ".AwayTeam", "Name"), "Name");
			star = F(await fm.Pull(at + ".PlayerOfTheMatch", "Name"), "Name");
			competition = F(await fm.Pull(at + ".Competition", "Name"), "Name");
			if (expected == null || $"{home} {F(fixture, "HomeScore")}-{F(fixture, "AwayScore")} {away}" == expected) { matched = true; break; }
		}
		if (F(fixture, "HasResult") != "True")
		{
			Console.Error.WriteLine(L("No match has been played."));
			return 1;
		}
		if (!matched)
		{
			// The caller knows the result it just watched and the game has not moved its last
			// fixture on (a cup tie lagging): saying nothing beats printing the
			// previous match as if it were this one.
			Console.Error.WriteLine(L($"Match report is not ready yet; the game's last fixture is still {home} {F(fixture, "HomeScore")}-{F(fixture, "AwayScore")} {away}."));
			return 0;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Home = home, Away = away, Competition = competition, PlayerOfTheMatch = star, Fixture = fixture }));
			return 0;
		}

		var score = $"{F(fixture, "HomeScore")}-{F(fixture, "AwayScore")}";

		// A draw settled on penalties: the game marks the result with a "p" and says who won.
		var shootout = F(fixture, "Result").EndsWith(" p", StringComparison.Ordinal)
			? F(fixture, "HomeTeamWon") == "True" ? home : F(fixture, "AwayTeamWon") == "True" ? away : null
			: null;
		if (shootout != null) score += " (pen.)";

		// Each side's goals, from its own list of match events; the fixture's scorers string
		// only names the team it is read for.
		var homeGoals = await Goals(fm, at + ".HomeEventInfos");
		var awayGoals = await Goals(fm, at + ".AwayEventInfos");
		var scorers = homeGoals.Count + awayGoals.Count > 0
			? string.Join("   ", new[] { (home, homeGoals), (away, awayGoals) }.Where(s => s.Item2.Count > 0).Select(s => $"{s.Item1}: {string.Join(", ", s.Item2)}"))
			: F(fixture, "GoalscorersString");
		var rating = Round(F(fixture, "PlayerOfTheMatchRating"), 1);

		if (!Ui.Rich)
		{
			Console.WriteLine($"{home} {score} {away}" + (competition.Length > 0 ? $"  ({competition})" : ""));
			if (shootout != null) Console.WriteLine(L($"Won on penalties: {shootout}"));
			if (scorers.Length > 0) Console.WriteLine(L($"Goals: {scorers.Replace("\n", "; ")}"));
			if (star.Length > 0) Console.WriteLine(L($"Player of the match: {star}") + (rating.Length > 0 ? $" ({rating})" : ""));
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
		if (competition.Length > 0) lines.Add($"[dim]{Ui.Safe(competition)}" + (F(fixture, "Attendance") is { Length: > 0 } crowd ? L($" · {Ui.Safe(crowd)} attendance") : "") + "[/]");
		if (shootout != null) lines.Add(L($"[dim]Won on penalties:[/] {Side(shootout)}"));
		if (scorers.Length > 0) lines.Add(L($"[dim]Goals:[/] {Ui.Safe(scorers.Replace("\n", " · "))}"));
		if (star.Length > 0) lines.Add(L($"[dim]Player of the match:[/] {Ui.Safe(star)}") + (rating.Length > 0 ? $" [green1]{Ui.Safe(rating)}[/]" : ""));

		Ui.Out.Write(new Panel(new Rows(new Markup(string.Join("\n", lines)), new Text(""), grid))
		{
			Header = new PanelHeader(L(" [bold]MATCH REPORT[/] ")),
			Border = BoxBorder.Rounded,
			BorderStyle = new Style(Color.Grey),
			Padding = new Padding(1, 0, 1, 0),
		});
		return 0;
	}

	/// <summary>"A Player 36', 77'" for each scorer in a side's list of match events.</summary>
	private static async Task<List<string>> Goals(Bridge fm, string list)
	{
		var goals = new List<string>();
		try
		{
			var events = await fm.Pull(list, "EventType,TimeString", list: true);
			if (events.ValueKind != JsonValueKind.Array) return goals;
			foreach (var entry in events.EnumerateArray())
			{
				// 0 a goal, 1 an own goal ("o.g.  26'", listed under the side it counted for, named
				// for the player who put it in), 2 a penalty ("pen 56'"). Other types are cards and the like.
				var type = F(entry, "EventType");
				if (type is not ("0" or "1" or "2")) continue;
				// The minute is the end of the game's text ("37'", "90+2'"); what comes before it is the
				// game's word for the kind ("o.g.", "k.k.", "pen"), which the type already says.
				var shown = string.Join(' ', F(entry, "TimeString").Split(' ', StringSplitOptions.RemoveEmptyEntries));
				var minute = System.Text.RegularExpressions.Regex.Match(shown, @"\d+(\+\d+)?'$") is { Success: true } m ? m.Value : shown;
				var time = type switch { "1" => L("(own goal) ") + minute, "2" => L("(pen) ") + minute, _ => minute };
				var row = (F(entry, "Row") is { Length: > 0 } at ? at : $"fmcli.{list.Replace('.', '_')}.{F(entry, "Index")}") + ".Player";
				var name = F(await fm.Pull(row, "Name"), "Name");
				goals.Add($"{name} {time}".Trim());
			}
		}
		catch (FmError)
		{
		}
		return goals;
	}

	/// <summary>A number as the game shows it: possession as a percentage, expected goals to two places.</summary>
	private static string Shown(string field, string value) => field switch
	{
		"Possession" when value.Length > 0 && !value.Contains('%') => GameNumbers.Percent(value),
		"ExpectedGoals" => Round(value, 2),
		_ => value,
	};

	private static string Round(string value, int places) =>
		double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			? GameNumbers.Of(v, "F" + places)
			: value;

	/// <summary>"55%", "1.23" or "7" as a number to weigh the bars by.</summary>
	private static double Value(string shown) =>
		double.TryParse(shown.Trim('%').Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;

	private static string F(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value) ? value.GetString() ?? "" : "";
}
