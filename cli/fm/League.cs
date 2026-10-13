using System.Text.Json;

namespace FmCli;

/// <summary>
/// A competition's statistics, read with no page opened (FINDINGS §8.129). A competition answers
/// "who leads this" (`PlayerTopStats`, `TopTeamStat`) and "everyone ranked by this"
/// (`PlayerStats`, `TeamStats`) once two variables sit beside it in the tree: `Type`, the
/// statistic's number, and `Stage`, -1 for the whole competition. The club's competitions are
/// `Human.Team.CurrentComp1…8`.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> LeaderTurkish = new()
	{
		["Most Goals"] = "En çok gol", ["Most Possession"] = "Topa en çok sahip olan", ["Most Yellow Cards"] = "En çok sarı kart",
		["Most Clean Sheets"] = "En çok gol yemeden maç", ["Highest Average Rating"] = "En yüksek ortalama puan", ["Most Assists"] = "En çok asist",
		["Most Shots For"] = "En çok şut çeken", ["Fewest Shots Against"] = "En az şut yiyen", ["Most Dribbles Made"] = "En çok çalım",
		["Best Pass Completion"] = "En iyi pas yüzdesi", ["Most Tackles Won"] = "En çok top kapma", ["Fewest Conceded"] = "En az gol yiyen",
		["Most Shots"] = "En çok şut", ["Most Key Passes"] = "En çok kilit pas", ["Most Player of the Match"] = "En çok maçın oyuncusu",
	};

	/// <summary>The statistics the game's own Stats card leads with, by number: ten of teams, ten of players.</summary>
	private static readonly int[] TeamLeaderTypes = [15, 28, 24, 53, 41, 51, 50, 30, 32, 16];
	private static readonly int[] PlayerLeaderTypes = [27, 2, 5, 20, 46, 23, 63, 12, 13, 62];

	/// <summary>What a statistic is asked for by, besides the game's own name for it.</summary>
	private static readonly Dictionary<string, string> StatTurkish = new()
	{
		["gol"] = "Goals", ["asist"] = "Assists", ["puan"] = "Average Rating", ["sari"] = "Yellow Cards", ["kirmizi"] = "Red Cards",
		["sut"] = "Shots", ["mac"] = "Appearances", ["dakika"] = "Minutes", ["kurtaris"] = "Saves", ["form"] = "Form", ["seyirci"] = "Average Attendance",
	};

	private static string LeagueStatsFile => CacheFile("league-stats.json");

	private static async Task<int> League(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		leagueDay = null;

		if (args.Contains("--stats"))
		{
			var (playerStats, teamStats) = await LeagueStats(refresh: args.Contains("--refresh"));
			if (json) Console.WriteLine(JsonOut.Of(new { Players = playerStats.Keys, Teams = teamStats.Keys }));
			else
			{
				Console.WriteLine($"Oyuncu istatistikleri ({playerStats.Count}) — league --stat <ad>");
				foreach (var name in playerStats.Keys) Console.WriteLine($"  {name}");
				Console.WriteLine($"\nTakım istatistikleri ({teamStats.Count}) — league --stat <ad> --teams");
				foreach (var name in teamStats.Keys) Console.WriteLine($"  {name}");
			}
			return 0;
		}

		var competitions = await ClubCompetitions();
		if (competitions.Count == 0)
		{
			Console.Error.WriteLine("Kulübün turnuvaları okunamadı.");
			return 1;
		}

		var wanted = string.Join(" ", Words(args));
		// The league is the default: the team's division in the game's data.
		var names = competitions.Select(c => c.Name).ToList();
		var division = await LeagueName();
		var competition = wanted.Length == 0
			? names.FirstOrDefault(c => c == division) ?? names[0]
			: Choose(names, wanted, "Turnuva");
		if (competition == null) return 1;
		var at = competitions.First(c => c.Name == competition).Path;

		if (Option(args, "--stat") is { } stat) return await LeagueRanking(json, args, competition, at, stat);

		var teamScopes = new List<string>();
		foreach (var type in TeamLeaderTypes) teamScopes.Add(await StatScope(at, type));
		var playerScopes = new List<string>();
		foreach (var type in PlayerLeaderTypes) playerScopes.Add(await StatScope(at, type));

		var read = await PullEach(
		[
			.. teamScopes.Select(s => ($"{s}.comp.TopTeamStat", "LongString,StatValue.DisplayValue,Team.Name")),
			.. playerScopes.Select(s => ($"{s}.comp.PlayerTopStats", "LongString,StatValue,Player.Name")),
		]);

		// A competition barely begun names a statistic and leaves out who leads it.
		List<(string Label, string Name, string Figure)> Leaders(IEnumerable<JsonElement> records, string who, string figure) =>
			records.Where(r => Field(r, "LongString").Length > 0)
				.Select(r => (Field(r, "LongString"), Field(r, who) is { Length: > 0 } n ? n : "-", Field(r, figure) is { Length: > 0 } f ? f : "-"))
				.ToList();
		var teams = Leaders(read.Take(teamScopes.Count), "Team.Name", "StatValue.DisplayValue");
		var players = Leaders(read.Skip(teamScopes.Count), "Player.Name", "StatValue");
		if (teams.Count + players.Count == 0 || teams.Concat(players).All(l => l.Name == "-"))
		{
			Console.Error.WriteLine($"{competition}: istatistik yok (turnuva henüz başlamamış olabilir).");
			return 1;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Competition = competition,
				Teams = teams.Select(t => new { Stat = t.Label, Team = t.Name, t.Figure }),
				Players = players.Select(p => new { Stat = p.Label, Player = p.Name, p.Figure }),
			}));
			return 0;
		}

		Console.WriteLine($"{competition} — liderler");
		void Block(string title, List<(string Label, string Name, string Figure)> rows)
		{
			Console.WriteLine(title);
			foreach (var (label, name, figure) in rows)
			{
				var said = LeaderTurkish.GetValueOrDefault(label, label);
				if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(said),-26}[/] [bold]{Ui.Safe(name)}[/] [yellow]{Ui.Safe(figure)}[/]");
				else Console.WriteLine($"  {said}: {name} ({figure})");
			}
		}
		Block("Takımlar", teams);
		Block("Oyuncular", players);
		Console.WriteLine("\nTam sıralama: league --stat <gol | asist | puan | …>  (hepsi: league --stats)");
		if (competitions.Count > 1) Console.WriteLine($"Başka turnuva: league <{string.Join(" | ", names)}>");
		return 0;
	}

	/// <summary>Everyone in a competition ranked by one statistic: players, or with --teams the teams.</summary>
	private static async Task<int> LeagueRanking(bool json, string[] args, string competition, string at, string stat)
	{
		var ofTeams = args.Contains("--teams");
		var (playerStats, teamStats) = await LeagueStats(refresh: false);
		var asked = StatTurkish.GetValueOrDefault(Fold(stat), stat);
		// A name only teams are ranked by ("Form") is a team ranking without being told so.
		if (!ofTeams && !playerStats.Keys.Any(k => Fold(k) == Fold(asked)) && teamStats.Keys.Any(k => Fold(k) == Fold(asked))) ofTeams = true;
		var catalogue = ofTeams ? teamStats : playerStats;
		// Well over a hundred names: one that matches none is not answered with all of them.
		if (!catalogue.Keys.Any(k => Fold(k) == Fold(asked)) && Matching(catalogue.Keys, k => k, asked).Count == 0)
		{
			Console.Error.WriteLine($"İstatistik: '{stat}' diye bir {(ofTeams ? "takım" : "oyuncu")} istatistiği yok. Hepsi: league --stats");
			return 64;
		}
		var name = Choose(catalogue.Keys.ToList(), asked, "İstatistik");
		if (name == null) return 64;

		var scope = await StatScope(at, catalogue[name]);
		var list = $"{scope}.comp.{(ofTeams ? "TeamStats" : "PlayerStats")}";
		var headers = (await PullEach([(list, "StatValueHeader,ContextStatValueHeader1,ContextStatValueHeader2")]))[0];
		var value = ofTeams ? "StatValue.DisplayValue" : "StatValue";
		var first = ofTeams ? "ContextStatValue1.DisplayValue" : "ContextStatValue1";
		var second = ofTeams ? "ContextStatValue2.DisplayValue" : "ContextStatValue2";
		// A team's run of results has no text of its own: it is the points of each match (3, 1, 0).
		var runs = ofTeams ? ",StatValue.PropertyValue,ContextStatValue1.PropertyValue,ContextStatValue2.PropertyValue" : "";
		var rows = (await Fm.Pull($"{list}.{(ofTeams ? "Teams" : "Players")}", $"Position,{value},{first},{second},Team.Name{(ofTeams ? runs : ",Player.Name")}", list: true))
			.EnumerateArray().ToList();
		string Cell(JsonElement row, string field) =>
			Field(row, field) is { Length: > 0 } text || !ofTeams ? Field(row, field) : Standings.Form(Field(row, field.Replace(".DisplayValue", ".PropertyValue")));
		if (rows.Count == 0)
		{
			Console.Error.WriteLine($"{competition}: {name} için kayıt yok (turnuva henüz başlamamış olabilir).");
			return 1;
		}

		var total = rows.Count;
		var top = args.Contains("--all") ? total : int.TryParse(Option(args, "--top"), out var n) && n > 0 ? n : 20;
		rows = rows.Take(top).ToList();

		// "1st", "2nd", then "=" for whoever shares the place above.
		static string Place(string position) => position.TrimEnd('s', 't', 'n', 'd', 'r', 'h');
		string[] columns = [Field(headers, "StatValueHeader") is { Length: > 0 } h ? h : name, Field(headers, "ContextStatValueHeader1"), Field(headers, "ContextStatValueHeader2")];

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Competition = competition,
				Stat = name,
				Of = ofTeams ? "teams" : "players",
				Total = total,
				Columns = columns,
				Rows = rows.Select(r => new
				{
					Place = Place(Field(r, "Position")),
					Player = ofTeams ? null : Field(r, "Player.Name"),
					Team = Field(r, "Team.Name"),
					Value = Cell(r, value),
					First = Cell(r, first),
					Second = Cell(r, second),
				}),
			}));
			return 0;
		}

		Console.WriteLine($"{competition} — {name} ({(rows.Count < total ? $"ilk {rows.Count} / {total}" : $"{total}")} {(ofTeams ? "takım" : "oyuncu")})");
		Table.Write(
			ofTeams ? ["#", "TAKIM", .. columns] : ["#", "OYUNCU", "TAKIM", .. columns],
			rows.Select(r => ofTeams
				? new[] { Place(Field(r, "Position")), Field(r, "Team.Name"), Cell(r, value), Cell(r, first), Cell(r, second) }
				: [Place(Field(r, "Position")), Field(r, "Player.Name"), Field(r, "Team.Name"), Cell(r, value), Cell(r, first), Cell(r, second)]).ToList());
		if (rows.Count < total) Console.WriteLine($"Devamı: --top <n> | --all");
		return 0;
	}

	/// <summary>The club's competitions by name, each with the path its reference is at.</summary>
	private static async Task<List<(string Name, string Path)>> ClubCompetitions()
	{
		var props = string.Join(",", Enumerable.Range(1, 8).Select(i => $"CurrentComp{i}.Name"));
		var read = (await PullEach([("Human.Team", props)]))[0];
		return Enumerable.Range(1, 8)
			.Select(i => (Name: Field(read, $"CurrentComp{i}.Name"), Path: $"Human.Team.CurrentComp{i}"))
			.Where(c => c.Name.Length > 0)
			.ToList();
	}

	/// <summary>
	/// A place in the tree where a competition sits beside the two variables its statistics ask
	/// for. Each day gets places of its own, so nothing answers with an earlier day's figures.
	/// </summary>
	private static async Task<string> StatScope(string competition, int type)
	{
		leagueDay ??= string.Concat(Field(await Fm.GetJson("state"), "Date").Where(char.IsDigit));
		var scope = $"fmcli.lg{leagueDay}.{competition[(competition.LastIndexOf('.') + 1)..]}.t{type}";
		await Fm.Get("pathset", ("to", $"{scope}.Type"), ("int", type.ToString()));
		await Fm.Get("pathset", ("to", $"{scope}.Stage"), ("int", "-1"));
		// A reference bound by this very call holds nothing until the next frame.
		await Until(async () => !(await Fm.Get("pathset", ("to", $"{scope}.comp"), ("from", competition))).StartsWith("error", StringComparison.Ordinal), seconds: 3, every: 0.2);
		return scope;
	}

	private static string leagueDay;

	/// <summary>
	/// Several readings at once: each is bound by the first round and arrives on a later one,
	/// so the rounds go on until two agree — a property that never comes (the leader of a
	/// competition not yet begun) is not waited for one reading at a time.
	/// </summary>
	private static async Task<List<JsonElement>> PullEach(List<(string Path, string Props)> readings)
	{
		List<JsonElement> read = [];
		var previous = "";
		for (var round = 0; round < 8; round++)
		{
			read = [];
			foreach (var (path, props) in readings)
			{
				try { read.Add(await Fm.GetJson("pull", ("path", path), ("props", props))); }
				catch (FmError) { read.Add(JsonDocument.Parse("{}").RootElement); }
			}
			var now = string.Join("|", read.Select(r => r.GetRawText()));
			var whole = read.Zip(readings).All(pair => pair.First.ValueKind == JsonValueKind.Object && pair.First.EnumerateObject().Count() >= pair.Second.Props.Split(',').Length);
			if (whole || (round >= 2 && now == previous && read.Any(r => r.ValueKind == JsonValueKind.Object && r.EnumerateObject().Any()))) break;
			previous = now;
			await Task.Delay(250);
		}
		return read;
	}

	/// <summary>
	/// Every statistic the game ranks players and teams by, name to number. The game has no
	/// list of them to read, so each number is asked what it is called once and the answer kept
	/// (--refresh asks again).
	/// </summary>
	private static async Task<(Dictionary<string, int> Players, Dictionary<string, int> Teams)> LeagueStats(bool refresh)
	{
		if (!refresh && File.Exists(LeagueStatsFile))
		{
			try
			{
				var kept = JsonDocument.Parse(File.ReadAllText(LeagueStatsFile)).RootElement;
				Dictionary<string, int> Of(string side) => kept.GetProperty(side).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());
				var (p, t) = (Of("Players"), Of("Teams"));
				if (p.Count > 0 && t.Count > 0) return (p, t);
			}
			catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or IOException) { }
		}

		if (!Ui.Json) Console.Error.WriteLine("İstatistik türleri oyundan okunuyor (bir kez)…");
		var league = (await ClubCompetitions()).FirstOrDefault().Path ?? "Human.Team.Division";
		var types = Enumerable.Range(0, 160).ToList();
		var scopes = new List<string>();
		foreach (var type in types) scopes.Add(await StatScope(league, type));
		var read = await PullEach(
		[
			.. scopes.Select(s => ($"{s}.comp.PlayerTopStats", "ShortString")),
			.. scopes.Select(s => ($"{s}.comp.TopTeamStat", "ShortString")),
		]);

		// Two numbers can answer to one name ("Goals"); the lower is the one the game's pages use.
		Dictionary<string, int> Named(IEnumerable<JsonElement> records)
		{
			var named = new Dictionary<string, int>();
			foreach (var (record, type) in records.Zip(types))
				if (Field(record, "ShortString").Trim() is { Length: > 0 } name) named.TryAdd(name, type);
			return named;
		}
		var players = Named(read.Take(types.Count));
		var teams = Named(read.Skip(types.Count));
		if (players.Count > 0 && teams.Count > 0)
		{
			Directory.CreateDirectory(DiffFolder);
			File.WriteAllText(LeagueStatsFile, JsonOut.Of(new { Players = players, Teams = teams }));
		}
		return (players, teams);
	}
}
