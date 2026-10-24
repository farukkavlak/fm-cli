using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static readonly string[] ShortDays = [L("Paz"), L("Pzt"), L("Sal"), L("Çar"), L("Per"), L("Cum"), L("Cmt")];

	private static async Task<int> LeagueTable(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		// Which competition: the league unless another of ours is named. Read in one go: a slot the
		// club has no competition in never arrives, and asked one at a time each such slot was waited for.
		var ours = (await ClubCompetitions()).Select(c => c.Name).ToList();
		var wanted = await LeagueName();
		if (string.Join(" ", Words(args)) is { Length: > 0 } typed)
		{
			// A competition that is not ours has no button to open it from; the Stages page
			// reaches all of them.
			if (!ours.Any(o => Fold(o).Contains(Fold(typed))) && !int.TryParse(typed, out _)) return await Stages(json, ["stages", typed]);
			if (Choose(ours, typed, L("Turnuva")) is not { } chosen) return 64;
			wanted = chosen;
		}

		// The Stages page has the whole table — wins, draws, defeats, goal difference and the
		// last five results — where the portal's tile has games and points; it is used when the
		// competition can be told apart there, and what follows is the way when it cannot (or
		// when the stage it is at is a knockout round, with no table).
		// The competitions' list is walked once for each language and career and kept (Competitions).
		if (!json)
		{
			try
			{
				var nation = "";
				await Until(async () => (nation = Field(await Fm.Pull("Human.Team.Nation", "Name"), "Name")).Length > 0, seconds: 3, every: 0.5);
				var every = await Competitions(false);
				// A continental competition goes by its own name there; a domestic one by the
				// country's ("Super League" is "Turkish Super League").
				var leagues = every.Where(p => p[^1] == wanted).ToList();
				if (leagues.Count == 0)
					leagues = every.Where(p => p[^1].EndsWith(wanted, StringComparison.Ordinal) && p.Contains(nation) && p.Contains("Men's")
						&& !p.Any(step => step.StartsWith("Under", StringComparison.Ordinal))).ToList();
				if (nation.Length > 0 && leagues.Count == 1 && await Stages(false, ["stages", leagues[0][^1], "--bare"]) == 0) return 0;
				// With the league over its page opens on the stage being played, the play-offs, which is ties
				// and no table; the table is the league stage's (2 Mayıs 2026).
				if (nation.Length > 0 && leagues.Count == 1 && await Stages(false, ["stages", leagues[0][^1], "league", "--bare"]) == 0) return 0;
			}
			catch (Exception e) when (e is FmError or System.Text.Json.JsonException or IOException) { }
		}

		// The portal's tile holds the table of the competition the next match is in — the
		// league most weeks, Europe before a European night — so it is only taken when that is
		// the one asked for. Any other is on the competition's own page, opened from its name.
		var opened = await OpenPage(Screen.Home);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// Each table bound on screen apart from the others (the tile and a competition's page
		// can both be up); the biggest of those not seen before is the one just opened.
		// Names and points come first and the other columns a moment after: read by those two alone, a
		// table counted as settled while its games were still to come, and went out with places and
		// points only (18 Nisan 2026).
		static string Key(JsonElement t) => string.Join("|", t.EnumerateArray().Select(r => Field(r, "Name") + Field(r, "LeaguePoints") + "/" + Field(r, "LeagueGamesPlayed") + "/" + Field(r, "LeagueGamesWon")));
		async Task<JsonElement?> Biggest(HashSet<string> not) =>
			(await Fm.GetJson("table", ("groups", "1"))).EnumerateArray().Cast<JsonElement?>()
				.Where(t => t.Value.GetArrayLength() >= 10 && !not.Contains(Key(t.Value)))
				// Two tables of the same league can be up at once, a tile with places and points only and the
				// full one; of equals the one with the games played is the table (18 Nisan 2026).
				.OrderByDescending(t => t.Value.GetArrayLength())
				.ThenByDescending(t => t.Value.EnumerateArray().Count(r => Field(r, "LeagueGamesPlayed").Length > 0)).FirstOrDefault();
		async Task<JsonElement?> Settled(HashSet<string> not, double seconds)
		{
			JsonElement? found = null;
			var previous = "";
			return await Until(async () =>
			{
				found = await Biggest(not);
				var key = found is { } t ? Key(t) : "";
				var same = key.Length > 0 && key == previous;
				previous = key;
				return same;
			}, seconds: seconds, every: 0.6) ? found : null;
		}

		var tile = await Settled([], 8);
		string? shown = null;
		try { shown = Field(await Fm.Pull("Human.Team.NextFixture.Competition", "Name"), "Name"); } catch (FmError) { }

		JsonElement table;
		if (tile is { } onTile && (wanted == null || shown == wanted)) table = onTile;
		else
		{
			var seen = (await Fm.GetJson("table", ("groups", "1"))).EnumerateArray().Select(Key).ToHashSet();
			var pressed = await Arriving("press", [("text", wanted), ("name", "SIButton"), ("any", "1")]);
			if (pressed.StartsWith("error", StringComparison.Ordinal) || await Settled(seen, 12) is not { } onPage)
			{
				Console.Error.WriteLine(L($"{wanted}: tablo açılmadı (kupada tablo olmaz)."));
				return 1;
			}
			table = onPage;
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
		// A place already settled is said beside the team, as the page does with its "C" and "Pl".
		static string Team(JsonElement row) => Field(row, "Name") + (Field(row, "Settled") switch
		{
			"" => "", "C" => L(" (şampiyon)"), "Pl" => L(" (play-off kesin)"), "P" => L(" (yükseldi)"), "R" => L(" (düştü)"), var mark => $" ({mark})",
		});
		var rows = table.EnumerateArray().Select(row => full
			? new[]
			{
				Field(row, "TablePosition"), Team(row), Field(row, "LeagueGamesPlayed"), Field(row, "LeagueGamesWon"),
				Field(row, "LeagueGamesDrawn"), Field(row, "LeagueGamesLost"), Field(row, "LeagueGoalDifference"), Field(row, "LeaguePoints"),
			}
			: [Field(row, "TablePosition"), Team(row), Field(row, "LeagueGamesPlayed"), Field(row, "LeaguePoints")]).ToList();
		string[] headers = full ? ["#", L("TAKIM"), L("O"), L("G"), L("B"), L("M"), L("AV"), L("P")] : ["#", L("TAKIM"), L("O"), L("P")];

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
			Console.Error.WriteLine(L("Kadro okunamadı."));
			return 1;
		}
		rows = rows.OrderByDescending(r => r.Ability.Count > 0 ? r.Ability[^1] - r.Ability[0] : 0).ThenByDescending(r => r.Ability.LastOrDefault()).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Age, r.Ability, Change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0, r.Potential })));
			return 0;
		}
		static string N(double v) => GameNumbers.Of(v, "0.#");
		Table.Write([L("OYUNCU"), L("YAŞ"), L("YETENEK (eski → yeni)"), L("DEĞİŞİM"), L("POTANSİYEL")], rows.Select(r =>
		{
			var change = r.Ability.Count > 0 ? Math.Round(r.Ability[^1] - r.Ability[0], 1) : 0;
			return new[]
			{
				r.Name, r.Age, string.Join(" → ", r.Ability.Select(N)),
				change > 0 ? "+" + N(change) : change < 0 ? N(change) : "=", r.Potential > 0 ? r.Potential.ToString() : "",
			};
		}).ToList());
		Console.WriteLine(L("\nYetenek 200 üzerinden, son dört aylık ölçüm; potansiyel kulübün gözünden."));
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

		var fixtures = await Shown("fixtures", Screen.Home);
		if (json)
		{
			Console.WriteLine(fixtures.ToString());
			return 0;
		}

		// League fixtures between other clubs are bound by the same kind of record on some
		// pages, without a date of their own; the team's fixtures always carry one.
		var rows = fixtures.EnumerateArray().Where(fixture => Field(fixture, "Date") != "").Select(fixture => new[]
		{
			Short(Field(fixture, "Date")),
			Field(fixture, "KickoffTime"),
			Field(fixture, "MatchVenueShort") switch { "H" => L("İç saha"), "A" => L("Deplasman"), "N" => L("Tarafsız"), var other => other },
			Field(fixture, "ShortName"),
			Field(fixture, "Name"),
			Field(fixture, "Result"),
		}).ToList();

		if (Ui.Rich)
		{
			// The game marks each result by what it was for us: 1 a win, 3 a win on penalties,
			// 9 a draw, 10 a defeat; anything else is left uncoloured.
			var kinds = fixtures.EnumerateArray().Where(fixture => Field(fixture, "Date") != "").Select(f => Field(f, "ResultType")).ToList();
			Standings.Fixtures([L("TARİH"), L("SAAT"), L("YER"), L("RAKİP"), L("TURNUVA"), L("SONUÇ")], rows, kinds);
			return 0;
		}

		Table.Write([L("TARİH"), L("SAAT"), L("YER"), L("RAKİP"), L("TURNUVA"), L("SONUÇ")], rows);
		return 0;

		static string Short(string date) =>
			DateTime.TryParseExact(date, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
				? $"{day.Day} {Months[day.Month - 1][..3]} {ShortDays[(int)day.DayOfWeek]}"
				: date;
	}

	/// <summary>
	/// The whole season's fixtures, gathered month by month from the calendar (FINDINGS §8.66):
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
			Console.Error.WriteLine(L("Takvimde maç bulunamadı."));
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(matches));
			return 0;
		}

		var played = new System.Text.RegularExpressions.Regex(@"^\d+-\d+[pe]?$");
		var rows = matches.Select(m => new[]
		{
			$"{m.Day.Day} {Months[m.Day.Month - 1][..3]} {ShortDays[(int)m.Day.DayOfWeek]}",
			played.IsMatch(m.TimeOrScore) ? "" : m.TimeOrScore,
			m.Venue switch { "H" => L("İç saha"), "A" => L("Deplasman"), "N" => L("Tarafsız"), _ => "" },
			m.Opponent + (m.Place.Length > 0 ? $" ({m.Place})" : ""),
			m.Competition,
			played.IsMatch(m.TimeOrScore) ? Ours(m).Replace("-", " - ") : "",
		}).ToList();
		// The game's own result marks, as `fixtures` colours them: 1 a win, 9 a draw, 10 a
		// defeat. Level after penalties or extra time is not called a draw.
		var kinds = matches.Select(m => !played.IsMatch(m.TimeOrScore) ? ""
			: MatchOutcome(m) switch { > 0 => "1", < 0 => "10", _ => char.IsDigit(m.TimeOrScore[^1]) ? "9" : "" }).ToList();
		string[] headers = [L("TARİH"), L("SAAT"), L("YER"), L("RAKİP"), L("TURNUVA"), L("SONUÇ")];
		if (Ui.Rich) Standings.Fixtures(headers, rows, kinds);
		else Table.Write(headers, rows);
		return 0;
	}
}
