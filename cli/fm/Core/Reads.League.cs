using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static readonly string[] ShortDays = [L("Sun"), L("Mon"), L("Tue"), L("Wed"), L("Thu"), L("Fri"), L("Sat")];

	private static async Task<int> LeagueTable(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		// Which competition: the league unless another of ours is named. Each is held by its data path
		// (Human.Team.Division, Human.Team.CurrentComp2), never by its name.
		var ours = await ClubCompetitions();
		var record = "Human.Team.Division";
		var wanted = await LeagueName();
		if (string.Join(" ", Words(args)) is { Length: > 0 } typed)
		{
			// A competition that is not ours has no data path to hold it by; the Stages page
			// reaches all of them by name.
			if (!ours.Any(o => Fold(o.Name).Contains(Fold(typed))) && !int.TryParse(typed, out _)) return await Stages(json, ["stages", typed]);
			if (Choose(ours.Select(o => o.Name).ToList(), typed, L("Competition")) is not { } chosen) return 64;
			(wanted, record) = ours.First(o => o.Name == chosen);
		}

		// The Stages page has the whole table — wins, draws, defeats, goal difference and the
		// last five results — where the portal's tile has games and points. The competition is chosen
		// there by the record its row stands for, and the stage with a table is found by Stages itself.
		try
		{
			if (!json && await Stages(false, ["stages", "--record", record, "--bare"]) == 0) return 0;
			// As JSON the rows alone, as the tile's table gives them.
			if (json && await Captured(["stages", "--record", record, "--json", "--bare"]) is { ValueKind: JsonValueKind.Object } read
			    && read.TryGetProperty("Read", out var stages) && stages.ValueKind == JsonValueKind.Array
			    && stages.EnumerateArray().Select(r => r.TryGetProperty("Table", out var t) ? t : default)
				    .FirstOrDefault(t => t.ValueKind == JsonValueKind.Array && t.GetArrayLength() > 0) is { ValueKind: JsonValueKind.Array } rows)
			{
				Console.WriteLine(rows.ToString());
				return 0;
			}
		}
		catch (Exception e) when (e is FmError or System.Text.Json.JsonException or IOException) { }

		// The portal's tile holds the table of the competition the next match is in — the
		// league most weeks, Europe before a European night — so it is only taken when that is
		// the one asked for, told by the competition's own id.
		string Id(JsonElement read) => Field(read, "UniqueId");
		var next = "";
		var asked = "";
		try
		{
			await Until(async () => (next = Id(await Fm.Pull("Human.Team.NextFixture.Competition", "UniqueId"))).Length > 0
				& (asked = Id(await Fm.Pull(record, "UniqueId"))).Length > 0, seconds: 3, every: 0.3);
		}
		catch (FmError) { }

		if (next.Length == 0 || next != asked)
		{
			Console.Error.WriteLine(L($"{wanted}: the table did not open (a cup has no table)."));
			return 1;
		}

		var opened = await OpenPage(Screen.Home);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// Names and points come first and the other columns a moment after: read by those two alone, a
		// table counted as settled while its games were still to come, and went out with places and
		// points only (18 Nisan 2026).
		static string Key(JsonElement t) => string.Join("|", t.EnumerateArray().Select(r => Field(r, "Name") + Field(r, "LeaguePoints") + "/" + Field(r, "LeagueGamesPlayed") + "/" + Field(r, "LeagueGamesWon")));
		async Task<JsonElement?> Biggest() =>
			(await Fm.GetJson("table", ("groups", "1"))).EnumerateArray().Cast<JsonElement?>()
				.Where(t => t!.Value.GetArrayLength() >= 10)
				// Two tables of the same league can be up at once, a tile with places and points only and the
				// full one; of equals the one with the games played is the table (18 Nisan 2026).
				.OrderByDescending(t => t!.Value.GetArrayLength())
				.ThenByDescending(t => t!.Value.EnumerateArray().Count(r => Field(r, "LeagueGamesPlayed").Length > 0)).FirstOrDefault();
		JsonElement? tile = null;
		var previous = "";
		await Until(async () =>
		{
			tile = await Biggest();
			var key = tile is { } t ? Key(t) : "";
			var same = key.Length > 0 && key == previous;
			previous = key;
			return same;
		}, seconds: 8, every: 0.6);

		if (tile is not { } table)
		{
			Console.Error.WriteLine(L($"{wanted}: the table did not open (a cup has no table)."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(table.ToString());
			return 0;
		}

		if (Ui.Rich)
		{
			Standings.Draw(table, (await Fm.OwnTeam()).Name);
			return 0;
		}

		WriteStandings(table);
		return 0;
	}

	private static void WriteStandings(JsonElement table)
	{
		// The portal's tile has only games and points; the columns it leaves out are left out.
		var full = table.EnumerateArray().Any(r => Field(r, "LeagueGamesWon").Length > 0);
		// A place already settled is said beside the team in the game's words ("Champions").
		static string Team(JsonElement row) => Field(row, "Name") + (Field(row, "Fate") is { Length: > 0 } fate ? $" ({fate})" : "");
		var rows = table.EnumerateArray().Select(row => full
			? new[]
			{
				Field(row, "TablePosition"), Team(row), Field(row, "LeagueGamesPlayed"), Field(row, "LeagueGamesWon"),
				Field(row, "LeagueGamesDrawn"), Field(row, "LeagueGamesLost"), Field(row, "LeagueGoalDifference"), Field(row, "LeaguePoints"),
			}
			: [Field(row, "TablePosition"), Team(row), Field(row, "LeagueGamesPlayed"), Field(row, "LeaguePoints")]).ToList();
		string[] headers = full ? ["#", L("TEAM"), L("P"), L("W"), L("D"), L("L"), L("GD"), L("PTS")] : ["#", L("TEAM"), L("P"), L("PTS")];

		// The last five league results, oldest first, where the page binds them.
		var forms = table.EnumerateArray().Select(row => Standings.Form(Field(row, "RecentFormValues"))).ToList();
		if (forms.Any(f => f.Length > 0))
		{
			headers = [.. headers, "FORM"];
			rows = rows.Select((row, at) => (string[])[.. row, forms[at]]).ToList();
		}

		Table.Write(headers, rows);
	}

	/// <summary>
	/// The game keeps a player's ability at the last four monthly measurements as whole numbers
	/// 220 to the point; out of 200 they are what the development graph draws.
	/// </summary>
	private static List<double> AbilityHistory(string raw) =>
		raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
			.Select(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var n) ? Math.Round(n / 220, 1) : -1)
			.Where(v => v > 0).ToList();

	/// <summary>
	/// Who is getting better and who worse: every player's ability at the last four monthly
	/// measurements, and the change across them.
	/// </summary>
	private static async Task<int> Progress(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		List<(string Name, string Age, List<double> Ability, int Potential)> rows = [];
		await Until(async () =>
		{
			var pulled = await Fm.GetJson("pull", ("path", "Human.Team.Players"), ("props", "Name,Age,AbilityProgressHistory,PerceivedPotentialAbility"), ("list", "1"));
			rows = pulled.EnumerateArray().Where(p => Field(p, "Name").Length > 0)
				.Select(p => (Field(p, "Name"), Field(p, "Age"), AbilityHistory(Field(p, "AbilityProgressHistory")), int.TryParse(Field(p, "PerceivedPotentialAbility"), out var pa) ? pa : 0)).ToList();
			return rows.Count > 0 && rows.All(r => r.Item3.Count > 0);
		}, seconds: 8, every: 0.6);
		if (rows.Count == 0)
		{
			Console.Error.WriteLine(L("The squad could not be read."));
			return 1;
		}
		rows = rows.OrderByDescending(r => r.Ability.Count > 0 ? r.Ability[^1] - r.Ability[0] : 0).ThenByDescending(r => r.Ability.LastOrDefault()).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Age, r.Ability, Change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0, r.Potential })));
			return 0;
		}
		static string N(double v) => GameNumbers.Of(v, "0.#");
		Table.Write([L("PLAYER"), L("AGE"), L("ABILITY (old → new)"), L("CHANGE"), L("POTENTIAL")], rows.Select(r =>
		{
			var change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0;
			return new[]
			{
				r.Name, r.Age, string.Join(" → ", r.Ability.Select(N)),
				change > 0 ? "+" + N(change) : change < 0 ? N(change) : "=", r.Potential > 0 ? r.Potential.ToString() : "",
			};
		}).ToList());
		Console.WriteLine(L("\nAbility out of 200, measured over the last four months; potential as the club sees it."));
		return 0;
	}

	/// <summary>
	/// The team's league: its division in the game's data, which no name has to be guessed from (a
	/// German league is "Bundesliga", a cup "DFB-Pokal").
	/// </summary>
	private static async Task<string> LeagueName()
	{
		try { return Field(await Fm.Pull("Human.Team.Division", "Name"), "Name") is { Length: > 0 } name ? name : null; }
		catch (FmError) { return null; }
	}

	private static async Task<int> Fixtures(bool json, bool all = false)
	{
		if (await NotNow() is { } refused) return refused;
		if (all) return await AllFixtures(json);

		var fixtures = await TeamFixtures();
		if (fixtures == null)
		{
			Console.Error.WriteLine(L("The fixtures could not be read."));
			return 1;
		}
		if (json)
		{
			Console.WriteLine(JsonOut.Of(fixtures));
			return 0;
		}

		var rows = fixtures.Select(f => new[] { Short(f.Date), f.Played ? "" : f.Kickoff, f.Venue, f.Opponent, f.Competition, f.Result }).ToList();
		string[] headers = [L("DATE"), L("TIME"), L("VENUE"), L("OPPONENT"), L("COMPETITION"), L("RESULT")];
		if (Ui.Rich)
		{
			// The game marks each result by what it was for us: 1 a win, 3 a win on penalties,
			// 9 a draw, 10 a defeat; anything else is left uncoloured.
			Standings.Fixtures(headers, rows, fixtures.Select(f => f.Played ? f.ResultType : "").ToList());
			return 0;
		}

		Table.Write(headers, rows);
		return 0;

		static string Short(string date) =>
			DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
				? $"{day.Day} {Months[day.Month - 1][..3]} {ShortDays[(int)day.DayOfWeek]}"
				: date;
	}

	private sealed record TeamFixture(string Date, string Kickoff, string Venue, string Opponent, string Competition, bool Played, string Result, string ResultType, string VenueShort = "");

	/// <summary>
	/// The last three matches played and the next seven, from the team's own fixture list
	/// (`Team.FixtureList`), no page opened. The list is asked with the variables the Fixture
	/// Schedule page lays beside its team: how many before and after today, padding, and the
	/// season (0 this one). A fixture's venue, result mark and opponent are told relative to a
	/// `team` and `human` beside its row. Venue, opponent and competition come in the game's words.
	/// </summary>
	private static async Task<List<TeamFixture>?> TeamFixtures()
	{
		var list = await FixtureList(3, 7, "FixtureDate,HasResult");
		if (list is not { ValueKind: JsonValueKind.Array } fixtures) return null;
		var rows = fixtures.EnumerateArray().Select(f => (Date: Field(f, "FixtureDate"), Played: Field(f, "HasResult") == "True", Row: Field(f, "Row")))
			.Where(r => r.Row.Contains('.') && r.Date.Length > 0).ToList();
		if (rows.Count == 0) return [];

		var under = rows[0].Row[..rows[0].Row.LastIndexOf('.')];
		// The variable is bound by this very call when nothing has bound it yet: then a frame later.
		foreach (var (variable, from) in FixtureContext)
			if (!await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.2))
				return null;

		const string shown = "MatchVenue,TeamOpponent.Name,Competition.Name";
		var ours = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
		var read = await PullEach(rows.Select(r => (r.Row, $"KickoffTime,HomeScore,AwayScore,HomeTeamWon,AwayTeamWon,ResultType,HomeTeam.UniqueId,AwayTeam.UniqueId,{shown}")).ToList(), shown: shown);
		return rows.Zip(read).Select(x => new TeamFixture(x.First.Date, Field(x.Second, "KickoffTime"), Field(x.Second, "MatchVenue"),
			Field(x.Second, "TeamOpponent.Name"), Field(x.Second, "Competition.Name"), x.First.Played,
			x.First.Played ? $"{Field(x.Second, "HomeScore")} - {Field(x.Second, "AwayScore")}" + ResultMark(x.Second) : "", Field(x.Second, "ResultType"), Venue(ours, Field(x.Second, "HomeTeam.UniqueId"), Field(x.Second, "AwayTeam.UniqueId")))).ToList();
	}

	/// <summary>
	/// The team's own fixture list (`Team.FixtureList`), asked with the variables the Fixture Schedule
	/// page lays beside its team: how many before and after today, padding, and the season (0 this one).
	/// Null when it cannot be read.
	/// </summary>
	private static async Task<JsonElement?> FixtureList(int previous, int future, string props)
	{
		const string at = "fmcli.fixtures";
		(string Variable, string Value, bool Number)[] context =
			[("team", "Human.Team", false), ("previousamount", previous.ToString(), true), ("futureamount", future.ToString(), true), ("fixturepadding", "0", true), ("yearindex", "0", true)];
		foreach (var (variable, value, number) in context)
			if ((await Fm.Get("pathset", ("to", $"{at}.{variable}"), (number ? "int" : "from", value))).StartsWith("error", StringComparison.Ordinal))
				return null;
		try { return await Fm.Pull($"{at}.team.FixtureList", props, list: true); }
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { return null; }
	}

	/// <summary>
	/// Each of our matches this season by its day ("yyyy-MM-dd"): where it is played, told by the teams' ids,
	/// its score ours first, and the game's code for how it ended. Level with a winner is a shootout:
	/// the game's numbers say so in any language. Empty when the list cannot be read.
	/// </summary>
	private static async Task<Dictionary<string, (string Venue, string Score, string Kind)>> FixtureFacts()
	{
		// A season has well under a hundred matches either side of today.
		var list = await FixtureList(100, 100, "FixtureDate,HomeTeam.UniqueId,AwayTeam.UniqueId,HasResult,HomeScore,AwayScore,HomeTeamWon,AwayTeamWon");
		if (list is not { ValueKind: JsonValueKind.Array } rows || rows.GetArrayLength() == 0) return [];
		var ours = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
		// The result code is asked with the variables beside the rows, as `fixtures` does.
		var first = Field(rows[0], "Row");
		if (first.Contains('.'))
			foreach (var (variable, from) in FixtureContext)
				await Until(async () => !(await Fm.Get("pathset", ("to", $"{first[..first.LastIndexOf('.')]}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 5, every: 0.2);
		var codes = await PullEach(rows.EnumerateArray().Select(f => (Field(f, "Row"), "ResultType")).ToList());
		Dictionary<string, (string, string, string)> facts = [];
		foreach (var (f, code) in rows.EnumerateArray().Zip(codes))
		{
			if (Field(f, "FixtureDate") is not { Length: > 0 } date || Venue(ours, Field(f, "HomeTeam.UniqueId"), Field(f, "AwayTeam.UniqueId")) is not { Length: > 0 } venue) continue;
			var score = "";
			if (Field(f, "HasResult") == "True")
			{
				var (home, away) = (Field(f, "HomeScore"), Field(f, "AwayScore"));
				// `ResultMark` wants the code as well as the numbers.
				var mark = ResultMark(f, Field(code, "ResultType"));
				score = (venue == "A" ? $"{away}-{home}" : $"{home}-{away}") + mark;
			}
			facts[date] = (venue, score, Field(code, "ResultType"));
		}
		return facts;
	}

	/// <summary>
	/// How a played fixture was settled, beside its score. Level with a winner is a shootout, by the fixture's own
	/// numbers; the winner's code 2 is extra time, the same code as a cup tie's `ResultTypeRaw`.
	/// </summary>
	private static string ResultMark(JsonElement fixture, string? code = null)
	{
		var level = Field(fixture, "HomeScore") == Field(fixture, "AwayScore");
		if (level && (Field(fixture, "HomeTeamWon") == "True" || Field(fixture, "AwayTeamWon") == "True")) return " (pen.)";
		return (code ?? Field(fixture, "ResultType")) == ExtraTimeWon ? $" ({L("e.t.")})" : "";
	}

	private static readonly (string Variable, string From)[] FixtureContext = [("team", "Human.Team"), ("human", "Human")];

	/// <summary>
	/// The whole season's fixtures, gathered month by month from the calendar:
	/// date, competition, venue, opponent and his league place, kick-off or our score.
	/// </summary>
	private static async Task<int> AllFixtures(bool json)
	{
		if (await CalendarMonths() is not var (months, showing)) return 1;
		var matches = new List<CalendarMatch>();
		foreach (var month in months)
		{
			var days = await CalendarMonth(month);
			if (days == null) continue;
			foreach (var m in days.SelectMany(d => d.Matches))
				if (!matches.Any(x => x.Day == m.Day && x.Opponent == m.Opponent)) matches.Add(m);
		}
		if (showing != null) await Fm.Get("select", ("name", "MonthDropdown"), ("value", showing.Value.ToString()));
		matches = matches.OrderBy(m => m.Day).ToList();
		if (matches.Count == 0)
		{
			Console.Error.WriteLine(L("No match found in the calendar."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(matches));
			return 0;
		}

		var rows = matches.Select(m => new[]
		{
			$"{m.Day.Day} {Months[m.Day.Month - 1][..3]} {ShortDays[(int)m.Day.DayOfWeek]}",
			m.Score.Length > 0 ? "" : m.Time,
			m.Venue switch { "H" => L("Home"), "A" => L("Away"), "N" => L("Neutral"), _ => "" },
			m.Opponent + (m.Place.Length > 0 ? $" ({m.Place})" : ""),
			m.Competition,
			m.Score.Replace("-", " - "),
		}).ToList();
		// The game's own result marks, as `fixtures` colours them.
		var kinds = matches.Select(m => m.Score.Length == 0 ? "" : m.Kind).ToList();
		string[] headers = [L("DATE"), L("TIME"), L("VENUE"), L("OPPONENT"), L("COMPETITION"), L("RESULT")];
		if (Ui.Rich) Standings.Fixtures(headers, rows, kinds);
		else Table.Write(headers, rows);
		return 0;
	}
}
