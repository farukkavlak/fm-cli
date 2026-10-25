using System.Text.Json;

namespace FmCli;

/// <summary>
/// The next opponent: who they are, their form and usual
/// formation, their standout players and manager, how the meeting is expected to go — all from the
/// game's data on the next fixture's opponent — then from the portal's opposition report, by each
/// text's data path, the team news (row.Text), previous meetings (prevmeetingfixture.*) and the
/// analysts' tiles (polygonobservation.*). Nothing is read by its place after a heading.
/// </summary>
internal static partial class Program
{
	private sealed record AnalystTile(string Name, string Scope, List<(string Label, string Value)> Stats, string Verdict);

	private static async Task<int> Opponent(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var fixture = await Fm.Pull("Human.Team.NextFixture", "MatchVenueShort,KickoffTime,FixtureDateDistanceInDays");
		var friendly = await NextIsFriendly();
		const string them = "Human.Team.NextFixture.TeamOpponent";
		var team = await Fm.Pull(them, "Name,Description,MostUsedFormation,RecentFormValues,NextOppositionThreatLevelAssessment");
		if (Field(team, "Name").Length == 0)
		{
			Console.Error.WriteLine(L("The opponent could not be read (there may be no next match)."));
			return 1;
		}
		async Task<string> Standout(string which)
		{
			var name = Field(await Fm.Pull($"{them}.{which}.Player", "Name"), "Name");
			var value = Field(await Fm.Pull($"{them}.{which}", "StatValue"), "StatValue");
			// Average ratings arrive as the engine's raw double (6.844444444444444); goals and
			// assists are counts and are left as they are.
			if (which == "HighestAverageRatingPlayer"
			    && double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var rating))
				value = GameNumbers.Of(rating, "0.00");
			return name.Length == 0 ? "" : value.Length > 0 ? $"{name} ({value})" : name;
		}
		var manager = Field(await Fm.Pull($"{them}.Manager", "Name"), "Name");
		var form = string.Join(" ", Standings.Form(Field(team, "RecentFormValues")));
		var venue = Field(fixture, "MatchVenueShort") switch { "H" => L("home"), "A" => L("away"), "N" => L("neutral ground"), var other => other };
		var days = Field(fixture, "FixtureDateDistanceInDays");
		var rows = new List<(string Label, string Value)>
		{
			("Form", form),
			(L("Formation"), Field(team, "MostUsedFormation")),
			(L("Top scorer"), await Standout("TopGoalscorerPlayer")),
			(L("Top assists"), await Standout("MostAssistsPlayer")),
			(L("Highest average rating"), await Standout("HighestAverageRatingPlayer")),
			(L("Agent"), manager),
		};

		// The portal's report page, read by each text's data path.
		var news = new List<string>();
		var meetings = new List<string>();
		var tiles = new List<AnalystTile>();
		if (!friendly && !(await OpenPage(Screen.OppositionReport, under: Screen.Home)).StartsWith("error", StringComparison.Ordinal))
		{
			List<(string Text, string Path)> page = [];
			// The analysts' tiles say "No Data Available" for a moment before their numbers arrive;
			// the page is read once two readings agree.
			var previous = "";
			await Until(async () =>
			{
				// Held by the opponent's description, which every language writes (the expected outcome is
				// left empty in a Turkish game); the whole report is fourteen levels above it.
				page = (await Fm.GetJson("texts", ("anchorpath", "Team.Description"), ("up", "14"), ("paths", "1"), ("most", "1")))
					.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
				var now = string.Join("|", page.Select(t => t.Text));
				var settled = now == previous && page.Any(t => t.Path.StartsWith("polygonobservation.", StringComparison.Ordinal));
				previous = now;
				return settled;
			}, seconds: 6, every: 0.5);

			string? lastPlain = null;
			AnalystTile tile = null;
			foreach (var (text, path) in page)
			{
				switch (path)
				{
					case "row.Text": news.Add(text); break;
					// A meeting's date is drawn without a path just before its result.
					case "prevmeetingfixture.ResultLinkableNoSpaces": meetings.Add(lastPlain is { } date ? $"{date} {text}" : text); break;
					case "polygonobservation.GraphType":
						tile = new AnalystTile(text, "", [], "");
						tiles.Add(tile);
						break;
					case "polygonobservation.PerformanceSummary" when tile != null:
						tiles[^1] = tile = tile with { Verdict = text };
						break;
					case "" when tile != null && tile.Verdict.Length == 0:
						// Within a tile: its scope first, then each number and its label.
						if (tile.Scope.Length == 0) tiles[^1] = tile = tile with { Scope = text };
						else if (lastPlain is { } number && IsNumber(number) && !IsNumber(text)) tile.Stats.Add((text, number));
						break;
				}
				lastPlain = path.Length == 0 ? text : null;
			}
		}
		static bool IsNumber(string text) => GameNumbers.Read(text.Trim('%')) != null;

		static string Turkish(string label) => label switch
		{
			"Gl/Gm" => L("goals per game"), "Np-xG/90" => L("non-penalty xG/90"), "Conc/Gm" => L("conceded per game"), "xGA/Gm" => L("xGA per game"),
			"Sh/Gm" => L("shots per game"), "Shot %" => L("shots on target %"), "Pas %" => "pas %", "Tck %" => L("tackles won %"), _ => label,
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Team = Field(team, "Name"), Description = GameWords.Of(Field(team, "Description")), Venue = Field(fixture, "MatchVenueShort"),
				Kickoff = Field(fixture, "KickoffTime"), InDays = days,
				Summary = rows.ToDictionary(r => r.Label, r => r.Value),
				News = news.Select(GameWords.Of).ToList(), PreviousMeetings = meetings.Select(GameWords.Of).ToList(),
				Expectation = GameWords.Of(Field(team, "NextOppositionThreatLevelAssessment")),
				Tiles = tiles.Select(t => new
				{
					Name = GameWords.Of(t.Name), Scope = GameWords.Of(t.Scope),
					Stats = t.Stats.ToDictionary(x => Turkish(x.Label), x => x.Value), Verdict = GameWords.Of(t.Verdict),
				}),
			}));
			return 0;
		}
		Console.WriteLine($"{Field(team, "Name")} · {Field(team, "Description")}");
		var inDays = days.Length > 0 ? Lf(days == "1" ? " · in {0} day" : " · in {0} days", days) : "";
		Console.WriteLine(L($"  Match: {venue}{inDays}{(Field(fixture, "KickoffTime") is { Length: > 0 } k ? $" · {k}" : "")}"));
		foreach (var (label, value) in rows)
			if (value.Length > 0) Console.WriteLine($"  {label}: {value}");
		if (meetings.Count > 0) Console.WriteLine(L($"  Previous matches: {string.Join(" · ", meetings)}"));
		if (Field(team, "NextOppositionThreatLevelAssessment") is { Length: > 0 } expected) Console.WriteLine(L($"  Expectation: {expected}"));
		foreach (var t in tiles.Where(t => t.Stats.Count > 0 || t.Verdict.Length > 0))
		{
			Console.WriteLine($"{t.Name}{(t.Scope.Length > 0 && t.Stats.Count > 0 ? $" ({t.Scope})" : "")}: {t.Verdict}");
			if (t.Stats.Count > 0) Console.WriteLine("  " + string.Join(" · ", t.Stats.Select(x => $"{Turkish(x.Label)} {x.Value}")));
		}
		if (news.Count > 0)
		{
			Console.WriteLine(L("Squad news:"));
			foreach (var n in news) Console.WriteLine($"  {n}");
		}
		if (friendly) Console.WriteLine(L("The next match is a friendly; there is no opposition report."));
		return 0;
	}
}
