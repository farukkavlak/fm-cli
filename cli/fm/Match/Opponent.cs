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
	private sealed record AnalystTile(string Name, List<(string Label, string Short, string Value)> Stats, string Verdict);

	/// <summary>
	/// The analysts' tiles on the report, from the data each is drawn from (a polygonobservation): its kind
	/// (GraphType), its verdict (PerformanceSummary) and each figure of its DataPointList by the game's own name for
	/// it, short name and number. A tile drawn twice (the page binds some twice) is one.
	/// </summary>
	private static async Task<List<AnalystTile>> AnalystTiles()
	{
		List<string> lists = [];
		try
		{
			lists = (await Fm.Get("find", ("name", "DataPointList"), ("limit", "40"))).Split('\n').Select(l => l.Trim())
				.Where(l => l.StartsWith($"..{Screen.OppositionReport}", StringComparison.Ordinal) && l.EndsWith(".polygonobservation.DataPointList", StringComparison.Ordinal))
				.Distinct().ToList();
		}
		catch (FmError) { return []; }
		var tiles = new List<AnalystTile>();
		var seen = new HashSet<string>();
		foreach (var list in lists)
		{
			var tile = list[..^".DataPointList".Length];
			var head = await Fm.Pull(tile, "GraphType,PerformanceSummary");
			var points = await Fm.Pull(list, "StatNameLabel,StatNameLabelShort,SubjectStatValue", list: true);
			if (points.ValueKind != JsonValueKind.Array || Field(head, "GraphType").Length == 0) continue;
			var stats = points.EnumerateArray()
				.Where(x => double.TryParse(Field(x, "SubjectStatValue"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _))
				.Select(x => (Label: Field(x, "StatNameLabel"), Short: Field(x, "StatNameLabelShort"),
					Value: GameNumbers.Of(double.Parse(Field(x, "SubjectStatValue"), System.Globalization.CultureInfo.InvariantCulture), "0.0")))
				.ToList();
			if (!seen.Add(Field(head, "GraphType") + "|" + string.Join("|", stats))) continue;
			tiles.Add(new AnalystTile(Field(head, "GraphType"), stats, Field(head, "PerformanceSummary")));
		}
		await GameWords.Ask(Fm, tiles.SelectMany(t => t.Stats.Select(x => x.Label)).Append("").ToArray());
		return tiles.Select(t => t with { Stats = t.Stats.Select(x => (GameWords.Of(x.Label), x.Short, x.Value)).ToList() }).ToList();
	}

	private static async Task<int> Opponent(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var fixture = await Fm.Pull("Human.Team.NextFixture", "KickoffTime,FixtureDateDistanceInDays");
		var where = await FixtureVenue();
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
		var venue = VenueWord(where);
		var days = Field(fixture, "FixtureDateDistanceInDays");
		// Each line with a key of its own for --json (the same in every language) and its label in the CLI's words.
		var rows = new List<(string Key, string Label, string Value)>
		{
			("Form", L("Form"), form),
			("Formation", L("Formation"), Field(team, "MostUsedFormation")),
			("TopScorer", L("Top scorer"), await Standout("TopGoalscorerPlayer")),
			("TopAssists", L("Top assists"), await Standout("MostAssistsPlayer")),
			("HighestAverageRating", L("Highest average rating"), await Standout("HighestAverageRatingPlayer")),
			("Manager", L("Manager"), manager),
		};

		// The report page (Tactics › Opponent › Opposition Report): the team news and previous meetings by each text's
		// data path, the analysts' tiles from the data they are drawn from.
		var news = new List<string>();
		var meetings = new List<string>();
		var tiles = new List<AnalystTile>();
		if (!friendly && !(await OpenPage(Screen.OppositionReport)).StartsWith("error", StringComparison.Ordinal))
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
			foreach (var (text, path) in page)
			{
				if (path == "row.Text") news.Add(text);
				// A meeting's date is drawn with no path in the meeting's row; it is taken only when it is a date by its
				// shape (the game's own order of day, month and year), never as whatever text came before.
				else if (path == "prevmeetingfixture.ResultLinkableNoSpaces")
					meetings.Add(lastPlain is { } date && IsDate(date) ? $"{date} {text}" : text);
				lastPlain = path.Length == 0 ? text : null;
			}
			tiles = await AnalystTiles();
		}
		static bool IsDate(string text) => System.Text.RegularExpressions.Regex.IsMatch(text, @"^(\d{4}([./-])\d{1,2}\2\d{1,2}|\d{1,2}([./-])\d{1,2}\3\d{4})$");

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Team = Field(team, "Name"), Description = GameWords.Of(Field(team, "Description")), Venue = where,
				Kickoff = Field(fixture, "KickoffTime"), InDays = days,
				Summary = rows.ToDictionary(r => r.Key, r => r.Value),
				News = news.Select(GameWords.Of).ToList(), PreviousMeetings = meetings.Select(GameWords.Of).ToList(),
				Expectation = GameWords.Of(Field(team, "NextOppositionThreatLevelAssessment")),
				Tiles = tiles.Select(t => new
				{
					Name = GameWords.Of(t.Name),
					Stats = t.Stats.Select(x => new { x.Label, x.Short, x.Value }), Verdict = GameWords.Of(t.Verdict),
				}),
			}));
			return 0;
		}
		Console.WriteLine($"{Field(team, "Name")} · {Field(team, "Description")}");
		var inDays = days.Length > 0 ? Lf(days == "1" ? " · in {0} day" : " · in {0} days", days) : "";
		Console.WriteLine(L($"  Match: {venue}{inDays}{(Field(fixture, "KickoffTime") is { Length: > 0 } k ? $" · {k}" : "")}"));
		foreach (var (_, label, value) in rows)
			if (value.Length > 0) Console.WriteLine($"  {label}: {value}");
		if (meetings.Count > 0) Console.WriteLine(L($"  Previous matches: {string.Join(" · ", meetings)}"));
		if (Field(team, "NextOppositionThreatLevelAssessment") is { Length: > 0 } expected) Console.WriteLine(L($"  Expectation: {expected}"));
		foreach (var t in tiles.Where(t => t.Stats.Count > 0 || t.Verdict.Length > 0))
		{
			// A tile the analysts have no word on yet is its name over its figures.
			Console.WriteLine(t.Verdict.Length > 0 ? $"{GameWords.Of(t.Name)}: {GameWords.Of(t.Verdict)}" : GameWords.Of(t.Name));
			if (t.Stats.Count > 0) Console.WriteLine("  " + string.Join(" · ", t.Stats.Select(x => $"{x.Label} {x.Value}")));
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
