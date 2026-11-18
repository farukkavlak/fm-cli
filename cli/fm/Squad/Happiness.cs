using System.Text.Json;

namespace FmCli;

/// <summary>
/// How happy each player is, and why (Squad › Dynamics › Happiness).
/// Everything but the level's word is the players' own data: the game's score for the level
/// (HierarchyOverallHappinessLevel), morale, standing and the reasons (HappinessPositives /
/// HappinessNegatives, each a list of reasons with a Name). The level's word has no path: it is
/// read off the page's row, which the row's own item says whose it is.
/// </summary>
internal static partial class Program
{
	/// <summary>One first-team player's happiness; Level is the page's word, empty when his row was not found.</summary>
	private sealed record HappyPlayer(string Name, string Id, string Row, int Score, string Morale, string Standing, int PositivesCount, int NegativesCount)
	{
		public string Level { get; init; } = "";
		public List<string> Flags { get; init; } = [];
		public List<string> Positives { get; init; } = [];
		public List<string> Negatives { get; init; } = [];
	}

	private static async Task<int> Happiness(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var players = await HappinessData();
		if (players.Count == 0)
		{
			Console.Error.WriteLine(L("The happiness page could not be read."));
			return 1;
		}
		var levels = await HappinessLevels(players);
		await GameWords.Ask(Fm, levels.Values.Concat(players.SelectMany(p => new[] { p.Morale, p.Standing })).ToArray());
		// Ordered and coloured by the game's own score for a level, not by the level's word.
		players = players.Select(p => p with { Level = levels.TryGetValue(p.Id, out var level) ? GameWords.Of(level) : "" })
			.OrderBy(p => p.Score).ThenBy(p => p.Name).ToList();
		// The page has three letters for each flag; the players' own lists say them in full.
		var flags = await SquadFlags();
		players = players.Select(p => flags.TryGetValue(p.Name, out var f) && f.Count > 0 ? p with { Flags = [FlagWords(f)] } : p).ToList();
		if (players.Any(p => p.Level.Length == 0))
			Console.Error.WriteLine(L($"The level of {players.Count(p => p.Level.Length == 0)} players was not found on the page; their colour still follows the game's score."));

		var typed = string.Join(" ", Words(args));
		if (typed.Length > 0)
		{
			var matches = Matching(players, p => p.Name, typed);
			if (matches.Count != 1)
			{
				Console.Error.WriteLine(matches.Count == 0 ? L($"'{typed}' is not in the happiness list.") : L($"'{typed}' matches more than one player: {string.Join(", ", matches.Select(p => p.Name))}"));
				return 1;
			}
			if (await WithReasons(matches[0]) is not { } why)
			{
				Console.Error.WriteLine(L($"{matches[0].Name}: the reasons could not be read."));
				return 1;
			}
			if (json) Console.WriteLine(JsonOut.Of(Shown(why)));
			else WriteWhy(why, positives: true);
			return 0;
		}

		if (args.Contains("--why"))
		{
			var all = new List<HappyPlayer>();
			foreach (var player in players)
				if (await WithReasons(player, positives: json) is { } why) all.Add(why);
			if (json)
			{
				Console.WriteLine(JsonOut.Of(all.Select(Shown)));
				return 0;
			}
			var upset = all.Where(p => p.Negatives.Count > 0).ToList();
			foreach (var player in upset) WriteWhy(player, positives: false);
			Console.WriteLine(upset.Count == 0
				? L($"None of the {all.Count} players has a complaint.")
				: L($"\n{upset.Count} of {all.Count} players have a complaint; the other {all.Count - upset.Count} do not."));
			if (all.Count < players.Count) Console.Error.WriteLine(L($"The reasons of {players.Count - all.Count} players could not be read."));
			return 0;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, p.Level, p.Score, p.Flags, Complaints = p.NegativesCount })));
			return 0;
		}
		foreach (var player in players)
		{
			var flag = player.Flags.Count > 0 ? $"  {string.Join(" ", player.Flags)}" : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(player.Name),-24} [{LevelColour(player.Score)}]{Ui.Safe(player.Level)}[/][dim]{Ui.Safe(flag)}[/]");
			else Console.WriteLine($"  {player.Name,-24} {player.Level}{flag}");
		}
		Console.WriteLine(L("\nWhy: happiness <player>   ·   everyone's complaints: happiness --why"));
		return 0;
	}

	private static object Shown(HappyPlayer p) =>
		new { p.Name, p.Level, p.Score, Morale = GameWords.Of(p.Morale), Standing = GameWords.Of(p.Standing), p.Flags, p.Positives, p.Negatives };

	/// <summary>
	/// The band a level falls in, by the game's score for it (80 Delighted … -65 Very Unhappy, as
	/// measured): the two boundaries sit in the gaps the game leaves (Content -20 down to Slightly
	/// Unhappy -40; Slightly Happy 20 up to Happy 40).
	/// </summary>
	private static string LevelColour(int score) => score < -30 ? "red" : score < 30 ? "yellow" : "green";

	/// <summary>
	/// What the game tells only the manager about a player is asked with him beside the rows: a
	/// human and a team, and the Judge looking (as for PlayerTraining). Without them a player's
	/// morale and reasons answer nothing.
	/// </summary>
	private static readonly (string Variable, string From)[] HappinessContext = [("human", "Human"), ("team", "Human.Team"), ("Judge", "Human")];

	private const string HappinessProps = "HierarchyOverallHappinessLevel,MoraleString,HierarchyInfluenceCategoryString,HappinessPositivesCount,HappinessNegativesCount";

	/// <summary>
	/// Every first-team player with what the game tells the manager about him, from the team's own list and with
	/// <see cref="HappinessContext"/> beside the rows: no table is opened, so no row can be out of view. Empty when
	/// the list cannot be read.
	/// </summary>
	/// <param name="shown">Properties read in the game's own words.</param>
	private static async Task<List<((string Name, string Id, string Row) First, JsonElement Second)>> JudgedPlayers(string props, string shown = "")
	{
		JsonElement players;
		try { players = await Fm.Pull("Human.Team.Players", "Name,UniqueId", list: true); }
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { return []; }
		if (players.ValueKind != JsonValueKind.Array) return [];
		var rows = players.EnumerateArray().Select(p => (Name: Field(p, "Name"), Id: Field(p, "UniqueId"), Row: Field(p, "Row")))
			.Where(r => r.Row.Contains('.') && r.Name.Length > 0 && r.Id.Length > 0).ToList();
		if (rows.Count == 0) return [];

		var under = rows[0].Row[..rows[0].Row.LastIndexOf('.')];
		// The variable is bound by this very call when nothing has bound it yet: then a frame later.
		foreach (var (variable, from) in HappinessContext)
			if (!await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.1))
				return [];

		var read = await PullEach(rows.Select(r => (r.Row, props)).ToList(), shown: shown);
		return rows.Zip(read).ToList();
	}

	/// <summary>Each first-team player's happiness from his own data; a player the game tells nothing of is left out.</summary>
	private static async Task<List<HappyPlayer>> HappinessData()
	{
		return (await JudgedPlayers(HappinessProps)).AsEnumerable()
			.Where(x => int.TryParse(NumberText(x.Second, "HierarchyOverallHappinessLevel"), out _))
			.Select(x => new HappyPlayer(x.First.Name, x.First.Id, x.First.Row, int.Parse(NumberText(x.Second, "HierarchyOverallHappinessLevel")),
				Field(x.Second, "MoraleString"), Field(x.Second, "HierarchyInfluenceCategoryString"),
				int.TryParse(NumberText(x.Second, "HappinessPositivesCount"), out var plus) ? plus : 0,
				int.TryParse(NumberText(x.Second, "HappinessNegativesCount"), out var minus) ? minus : 0))
			.ToList();
	}

	/// <summary>A value the bridge answers as a number, or as the same number written out.</summary>
	private static string NumberText(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value)
			? value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString() : value.GetString() ?? ""
			: "";

	/// <summary>
	/// What pleases him and what he holds against the club, each a reason's Name in the game's own
	/// words; null when a list does not come as long as its count says.
	/// </summary>
	private static async Task<HappyPlayer?> WithReasons(HappyPlayer player, bool positives = true)
	{
		async Task<List<string>?> Reasons(string list, int count)
		{
			if (count == 0) return [];
			List<string> names = [];
			await Until(async () =>
			{
				try { names = (await Fm.Pull($"{player.Row}.{list}", "Name", list: true)).EnumerateArray().Select(r => Field(r, "Name")).Where(n => n.Length > 0).ToList(); }
				catch (FmError) { names = []; }
				return names.Count == count;
			}, seconds: 5, every: 0.25);
			return names.Count == count ? names : null;
		}

		if (await Reasons("HappinessPositives", positives ? player.PositivesCount : 0) is not { } pleased) return null;
		if (await Reasons("HappinessNegatives", player.NegativesCount) is not { } negatives) return null;
		return player with { Positives = pleased, Negatives = negatives };
	}

	private static void WriteWhy(HappyPlayer why, bool positives)
	{
		var rest = L($" · morale {GameWords.Of(why.Morale)} · {GameWords.Of(why.Standing)}") + (why.Flags.Count > 0 ? $" · {string.Join(" ", why.Flags)}" : "");
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(why.Name)}[/]  [{LevelColour(why.Score)}]{Ui.Safe(why.Level)}[/][dim]{Ui.Safe(rest)}[/]");
		else Console.WriteLine($"{why.Name}  {why.Level}{rest}");

		foreach (var negative in why.Negatives)
		{
			if (Ui.Rich) Ui.Line($"  [red]−[/] {Ui.Safe(negative)}");
			else Console.WriteLine($"  - {negative}");
		}
		if (!positives) return;
		foreach (var positive in why.Positives)
		{
			if (Ui.Rich) Ui.Line($"  [green]+[/] {Ui.Safe(positive)}");
			else Console.WriteLine($"  + {positive}");
		}
		if (why.Negatives.Count == 0) Console.WriteLine(L("  No complaints."));
	}

	/// <summary>The page's rows are told apart from any other table by the player's description, which only they carry.</summary>
	private const string HappinessRowMark = "Person.PlayerDescription";

	/// <summary>
	/// The level's word of each player by UniqueId, as the page writes it. The page's row for a
	/// player is found by the item the table keeps for it, not by its name or place; the level is
	/// the row's one text bound to no path (the others: flags, name, description).
	/// </summary>
	private static async Task<Dictionary<string, string>> HappinessLevels(List<HappyPlayer> players)
	{
		var levels = new Dictionary<string, string>(StringComparer.Ordinal);
		var wanted = players.Select(p => p.Id).ToHashSet();
		var generation = 0;

		async Task<List<(string Item, string Level)>> Read()
		{
			JsonElement rows;
			try { rows = await Fm.GetJson("rowtexts", ("has", HappinessRowMark), ("gen", $"h{++generation}")); }
			catch (FmError) { return []; }
			if (rows.ValueKind != JsonValueKind.Array) return [];
			return rows.EnumerateArray()
				.Select(r => (Item: Field(r, "Item"), Unbound: r.GetProperty("Texts").EnumerateArray().Where(t => Field(t, "Path") == "").Select(t => Field(t, "Text")).ToList()))
				.Where(r => r.Item.Length > 0 && r.Unbound.Count == 1)
				.Select(r => (r.Item, r.Unbound[0]))
				.ToList();
		}

		// Rows in view once the list has laid them out: a page just opened lays out its names before
		// their levels, and a scroll rebuilds the rows in more than one step. Two equal readings in a
		// row are taken as laid out.
		async Task<List<(string Item, string Level)>> Settled()
		{
			var last = await Read();
			await Until(async () =>
			{
				var now = await Read();
				var same = now.Count > 0 && now.Select(r => r.Level).SequenceEqual(last.Select(r => r.Level)) && now.Count == last.Count;
				last = now;
				return same;
			}, seconds: 5, every: 0.1);
			return last;
		}

		async Task Take(List<(string Item, string Level)> rows)
		{
			if (rows.Count == 0) return;
			var ids = await PullEach(rows.Select(r => (r.Item, "UniqueId")).ToList());
			foreach (var (row, id) in rows.Zip(ids))
				if (Field(id, "UniqueId") is { Length: > 0 } uid && wanted.Contains(uid)) levels[uid] = row.Level;
		}
		bool Whole() => wanted.All(levels.ContainsKey);

		if ((await Read()).Count == 0)
		{
			var opened = await OpenSubpage(Screen.Dynamics, Screen.Happiness, async () => (await Read()).Count > 0);
			if (opened.StartsWith("error", StringComparison.Ordinal)) return levels;
		}
		await Take(await Settled());
		if (Whole()) return levels;

		// The list only lays out the rows in view: it is scrolled until every player has been seen.
		foreach (var share in Sweep)
		{
			if (Whole()) break;
			var moved = await Fm.Get("scroll", ("anchorpath", HappinessRowMark), ("to", share));
			if (moved.StartsWith("error", StringComparison.Ordinal)) break;
			await Take(await Settled());
		}
		await Fm.Get("scroll", ("anchorpath", HappinessRowMark), ("to", "0"));
		return levels;
	}

	/// <summary>
	/// Where the list is scrolled to, as shares of its length: the ends and the middle first, which
	/// on most screens reach every row, then the steps between for a long list on a small one.
	/// </summary>
	private static readonly string[] Sweep = ["0", "0.5", "1", "0.1", "0.2", "0.3", "0.4", "0.6", "0.7", "0.8", "0.9"];
}
