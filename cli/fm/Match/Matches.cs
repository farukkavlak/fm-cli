using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// A player's season match by match, as his Match Ratings page shows it: the game's number,
/// date, position, role, opponent, score, when he came on or off, xG, assists, goals and
/// rating — for a goalkeeper xG against and goals conceded instead; above them his games, best,
/// worst and average rating.
/// </summary>
internal static partial class Program
{
	private sealed record MatchRow(int Number, string Date, string Position, string Role, string Opponent, string Score, string OnOff, string Xg, string Assists, string Goals, string Conceded, string Rating);

	/// <summary>
	/// The page's numbers are the player's own form record (`Form`): the summary is its
	/// `MatchRatingsStats` and the table its `MatchesData`, one record a match. Both answer without
	/// the page being drawn, so nothing is opened, scrolled or recognised by its words or by where
	/// it stands on the page.
	/// </summary>
	private static async Task<int> PlayerMatches(JsonElement card, bool json)
	{
		var player = card.GetProperty("Player");
		var name = Field(player, "Name");
		var path = Field(player, "Path");
		if (path.Length == 0)
		{
			Console.Error.WriteLine(L($"{name}: the match ratings page did not open."));
			return 1;
		}

		var keeper = Field(await Fm.Pull(path, "IsGoalkeeper"), "IsGoalkeeper") == "True";
		var positions = await EngineTexts.Positions(Fm);
		var stats = await Fm.Pull($"{path}.Form.MatchRatingsStats", "GamesPlayed,MaxValue,MinValue,AverageRating,Goals,Assists,CleanSheets");
		if (Field(stats, "GamesPlayed").Length == 0)
		{
			Console.Error.WriteLine(L($"There is no form box for {name} (they may have no matches this season)."));
			return 1;
		}
		// GameTime is the game's own words ("off 72"); the rest are numbers and codes.
		var records = (await Fm.Pull($"{path}.Form.MatchesData",
			"Index,Date,Position,Role,GameTime,ExpectedGoals,ExpectedGoalsAgainst,Assists,Goals,Conceded,TotalRating,Team.Name,Fixture.ResultLinkableNoSpaces",
			list: true, shown: "GameTime")).EnumerateArray().ToList();

		// A match he sat out, or came on too late to be rated in, has no rating (0).
		static string Rating(string value) => GameNumbers.Read(value) is > 0 and <= 10 and var r ? GameNumbers.Of(r, "0.00") : "-";
		static string Decimal(string value) => GameNumbers.Read(value) is { } x ? GameNumbers.Of(x, "0.00") : value;
		var all = records
			.Select(r => (Number: int.TryParse(Field(r, "Index"), out var n) ? n : 0, Record: r))
			.Where(r => r.Number > 0)
			.OrderByDescending(r => r.Number)
			.Select(r =>
			{
				var played = Field(r.Record, "Position") is { Length: > 0 } p && p != "0";
				return new MatchRow(r.Number, MatchDate(Field(r.Record, "Date")),
					played ? Position(Field(r.Record, "Position"), positions) : "-", played ? RoleName(Field(r.Record, "Role")) : "",
					Field(r.Record, "Team.Name"), Field(r.Record, "Fixture.ResultLinkableNoSpaces"), Field(r.Record, "GameTime"),
					Decimal(Field(r.Record, keeper ? "ExpectedGoalsAgainst" : "ExpectedGoals")),
					keeper ? "" : Field(r.Record, "Assists"), keeper ? "" : Field(r.Record, "Goals"),
					keeper ? Field(r.Record, "Conceded") : "", Rating(Field(r.Record, "TotalRating")));
			})
			.ToList();
		var summary = new
		{
			Apps = Field(stats, "GamesPlayed"), Best = Rating(Field(stats, "MaxValue")), Worst = Rating(Field(stats, "MinValue")),
			Average = Rating(Field(stats, "AverageRating")), Goals = Field(stats, "Goals"), Assists = Field(stats, "Assists"),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Goalkeeper = keeper, Summary = summary, Matches = all }));
			return 0;
		}

		Console.WriteLine(L($"{name} — {summary.Apps} matches · average {summary.Average} · best {summary.Best} · worst {summary.Worst} · ")
			+ (keeper ? L($"{Field(stats, "CleanSheets")} clean sheets") : L($"{summary.Goals} goals · {summary.Assists} assists")));
		var subs = all.Any(r => r.OnOff.Length > 0);
		var rich = Ui.Rich;

		// His ratings as a row of bars, oldest first, on the scale ratings mostly live on.
		var ratings = all.AsEnumerable().Reverse()
			.Select(r => GameNumbers.Read(r.Rating))
			.Where(r => r != null).Select(r => r!.Value).ToList();
		if (ratings.Count > 1)
		{
			var bars = ratings.Select(r => (Bar: Ui.Bar(r, 5.5, 9), Rating: r)).ToList();
			if (rich) Ui.Line(L($"[dim]rating[/]  {string.Concat(bars.Select(b => b.Rating >= 7.5 ? $"[green]{b.Bar}[/]" : b.Rating < 6.5 ? $"[red]{b.Bar}[/]" : b.Bar.ToString()))}  [dim]old → new[/]"));
			else Console.WriteLine(L($"rating  {string.Concat(bars.Select(b => b.Bar))}  old → new"));
		}
		// The game gives the score home side first and does not say which side he was on.
		var heads = new List<string> { "#", L("DATE"), L("OPPONENT"), L("SCORE (H-A)"), L("POSITION") };
		if (subs) heads.Add(rich ? "" : L("ON/OFF"));
		heads.AddRange(keeper ? ["xGA", L("CONCEDED")] : ["xG", L("GOALS"), L("AST")]);
		heads.Add(L("RATING"));
		List<string> Cells(MatchRow r)
		{
			var cells = new List<string>
			{
				rich ? $"[dim]{r.Number}[/]" : r.Number.ToString(CultureInfo.InvariantCulture), r.Date,
				rich ? Ui.Safe(r.Opponent) : r.Opponent, r.Score, r.Position == "-" ? (rich ? L("[dim]did not play[/]") : L("did not play")) : rich ? $"{r.Position} [dim]{Ui.Safe(r.Role)}[/]" : $"{r.Position} {r.Role}",
			};
			if (subs) cells.Add(rich ? $"[dim]{Ui.Safe(r.OnOff)}[/]" : r.OnOff);
			if (keeper) cells.AddRange([r.Xg, rich && r.Conceded == "0" ? "[green bold]0[/]" : r.Conceded]);
			else cells.AddRange(rich ? [r.Xg, Count(r.Goals, "green"), Count(r.Assists, "cyan")] : [r.Xg, r.Goals, r.Assists]);
			cells.Add(rich ? Colour(r.Rating) : r.Rating);
			return cells;
		}
		if (rich)
		{
			var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
			foreach (var head in heads) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{head}[/]"));
			foreach (var r in all) Spectre.Console.TableExtensions.AddRow(table, Cells(r).ToArray());
			Ui.Out.Write(table);
		}
		else
			Table.Write(heads, all.Select(r => Cells(r).ToArray()).ToList());
		return 0;

		static string Count(string n, string colour) => n is "" or "0" ? "[dim]0[/]" : $"[{colour} bold]{n}[/]";
		static string Colour(string rating) => GameNumbers.Read(rating) is { } r
			? r >= 7.5 ? $"[green bold]{rating}[/]" : r < 6.5 ? $"[red]{rating}[/]" : rating : rating;
	}

	/// <summary>A match's day as the CLI writes dates ("25 Oct 2026"), from the game's yyyy-MM-dd.</summary>
	private static string MatchDate(string date) =>
		DateTime.TryParseExact(date, "yyyy-MM-dd", null, DateTimeStyles.None, out var day)
			? $"{day.Day} {Months[day.Month - 1][..3]} {day.Year}"
			: date;

	/// <summary>
	/// Where he played, as the game writes the position in its language ("OOSM"; "AMC" in English). The side
	/// a central player stood on (DCL, DCR) has no code of its own in the game, and is not said.
	/// </summary>
	private static string Position(string mask, IReadOnlyDictionary<string, string> positions)
	{
		if (!long.TryParse(mask, out var bits)) return mask;
		var code = Spot((bits & ~(1048576L | 2097152L)).ToString(CultureInfo.InvariantCulture));
		return positions.GetValueOrDefault(code, code);
	}

	/// <summary>
	/// The role he played, by its code in the game's own words: the names the tactic's role lists
	/// give it (kept per language), or nothing while no list has named it yet.
	/// </summary>
	private static string RoleName(string code) => RoleNames.TryGetValue($"ip:{code}", out var role) ? role : "";
}
