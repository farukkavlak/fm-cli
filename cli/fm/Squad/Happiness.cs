using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// How happy each player is, and why (Squad › Dynamics › Happiness, FINDINGS §8.59, §8.127).
/// The list is read off the page as status flags (Wnt, Unr, FtT, Rst…), name, "N year-old …",
/// level; the reasons — what pleases him, what he holds against the club — come from the panel
/// the page fills when his row is selected.
/// </summary>
internal static partial class Program
{
	private static readonly string[] HappinessLevels =
		["Very Unhappy", "Unhappy", "Slightly Unhappy", "Content", "Slightly Happy", "Happy", "Very Happy", "Delighted"];

	/// <summary>
	/// The game's own score for each level, by its place in HappinessLevels, as measured (80
	/// Delighted, 60 Very Happy, 40 Happy, 20 Slightly Happy, -20 Content, -40 Slightly Unhappy,
	/// -65 Very Unhappy, Unhappy between): the middle of a level, for a row whose score the team's
	/// data did not give.
	/// </summary>
	private static readonly int[] LevelScores = [-65, -52, -40, -20, 20, 40, 60, 80];

	/// <summary>A row of the page; Description ("27 year-old winger") is what tells the row from the same name on the page behind.</summary>
	private sealed record HappyRow(string Name, string Description, string Level, List<string> Flags)
	{
		/// <summary>The game's score for his level (HierarchyOverallHappinessLevel), which orders and colours him without the level's word.</summary>
		public int Score { get; init; }
	}

	/// <summary>The panel of the selected player.</summary>
	private sealed record HappyWhy(string Name, string Level, string Morale, string Hierarchy, List<string> Positives, List<string> Negatives)
	{
		/// <summary>The game's score for his level, which colours him without the level's word.</summary>
		public int Score { get; init; }
	}

	private static async Task<int> Happiness(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var players = await HappinessRows();
		// A level the page writes without its translation ("Content") is shown in the game's words.
		await GameWords.Ask(Fm, HappinessLevels);
		if (players.Count == 0)
		{
			Console.Error.WriteLine(L("Mutluluk sayfası okunamadı."));
			return 1;
		}
		// Ordered and coloured by the game's own score for a level (HierarchyOverallHappinessLevel),
		// not by the level's word: a Turkish game leaves two levels in its own words ("Biraz Mutlu"/
		// "Biraz Mutsuz"), which no English word matches.
		var scores = await HappinessScores();
		players = players.Select(p => p with { Score = scores.TryGetValue(p.Name, out var score) ? score : FallbackScore(p.Level) })
			.OrderBy(p => p.Score).ThenBy(p => p.Name).ToList();
		// The page has three letters for each flag; the players' own lists say them in full.
		var flags = await SquadFlags();
		players = players.Select(p => flags.TryGetValue(p.Name, out var f) && f.Count > 0 ? p with { Flags = [FlagWords(f)] } : p).ToList();

		var typed = string.Join(" ", Words(args));
		if (typed.Length > 0)
		{
			var matches = Matching(players, p => p.Name, typed);
			if (matches.Count != 1)
			{
				Console.Error.WriteLine(matches.Count == 0 ? L($"'{typed}' mutluluk listesinde yok.") : L($"'{typed}' birden çok oyuncuya uyuyor: {string.Join(", ", matches.Select(p => p.Name))}"));
				return 1;
			}
			if (await WhyHappy(matches[0]) is not { } why)
			{
				Console.Error.WriteLine(L($"{matches[0].Name}: nedenler okunamadı."));
				return 1;
			}
			if (json) Console.WriteLine(JsonOut.Of(why));
			else WriteWhy(why, matches[0].Flags, positives: true);
			return 0;
		}

		if (args.Contains("--why"))
		{
			var all = new List<(HappyRow Row, HappyWhy Why)>();
			foreach (var player in players)
				if (await WhyHappy(player) is { } why) all.Add((player, why));
			if (json)
			{
				Console.WriteLine(JsonOut.Of(all.Select(a => a.Why)));
				return 0;
			}
			var upset = all.Where(a => a.Why.Negatives.Count > 0).ToList();
			foreach (var (row, why) in upset) WriteWhy(why, row.Flags, positives: false);
			Console.WriteLine(upset.Count == 0
				? L($"{all.Count} oyuncunun hiçbirinin bir şikâyeti yok.")
				: L($"\n{all.Count} oyuncudan {upset.Count} tanesinin şikâyeti var; kalan {all.Count - upset.Count} oyuncunun yok."));
			if (all.Count < players.Count) Console.Error.WriteLine(L($"{players.Count - all.Count} oyuncunun nedenleri okunamadı."));
			return 0;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, Level = GameWords.Of(p.Level), p.Flags })));
			return 0;
		}
		foreach (var player in players)
		{
			var say = GameWords.Of(player.Level);
			var flag = player.Flags.Count > 0 ? $"  {string.Join(" ", player.Flags)}" : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(player.Name),-24} [{LevelColour(player.Score)}]{Ui.Safe(say)}[/][dim]{Ui.Safe(flag)}[/]");
			else Console.WriteLine($"  {player.Name,-24} {say}{flag}");
		}
		Console.WriteLine(L("\nNedeni: happiness <oyuncu>   ·   herkesin şikâyetleri: happiness --why"));
		return 0;
	}

	/// <summary>
	/// The band a level falls in, by the game's score for it: the two boundaries sit in the gaps the
	/// game leaves (Content -20 down to Slightly Unhappy -40; Slightly Happy 20 up to Happy 40), so
	/// the levels a Turkish game does not translate still colour aright.
	/// </summary>
	private static string LevelColour(int score) => score < -30 ? "red" : score < 30 ? "yellow" : "green";

	/// <summary>The score of a level by its word, for a row the team's data did not give one.</summary>
	private static int FallbackScore(string level) => Array.IndexOf(HappinessLevels, level) is var at and >= 0 ? LevelScores[at] : 0;

	/// <summary>
	/// Each player's happiness score from the team's own data, keyed by name — the game keeps it as
	/// a number (80 Delighted … -65 Very Unhappy), the same in every language, unlike the level's word.
	/// </summary>
	private static async Task<Dictionary<string, int>> HappinessScores()
	{
		var scores = new Dictionary<string, int>(StringComparer.Ordinal);
		try
		{
			foreach (var player in (await Fm.Pull("Human.Team.Players", "Name,HierarchyOverallHappinessLevel", list: true)).EnumerateArray())
			{
				var name = Field(player, "Name");
				if (name.Length > 0 && int.TryParse(NumberText(player, "HierarchyOverallHappinessLevel"), out var score))
					scores[name] = score;
			}
		}
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { }
		return scores;
	}

	/// <summary>A value the bridge answers as a number, or as the same number written out.</summary>
	private static string NumberText(JsonElement element, string name) =>
		element.TryGetProperty(name, out var value)
			? value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString() : value.GetString() ?? ""
			: "";

	private static void WriteWhy(HappyWhy why, List<string> flags, bool positives)
	{
		var say = why.Level;
		var rest = $" · moral {why.Morale} · {why.Hierarchy}{(flags.Count > 0 ? $" · {string.Join(" ", flags)}" : "")}";
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(why.Name)}[/]  [{LevelColour(why.Score)}]{Ui.Safe(say)}[/][dim]{Ui.Safe(rest)}[/]");
		else Console.WriteLine($"{why.Name}  {say}{rest}");

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
		if (why.Negatives.Count == 0) Console.WriteLine(L("  Şikâyeti yok."));
	}

	// Read by each text's data path (FINDINGS §8.169), anchored on a player's description, which
	// only rows of this list carry: a row is its flags (StatusFlag.StatusTitleShort), the name
	// (binding.Name), the description (Person.PlayerDescription), then the level, which is drawn
	// without a path but is one of the game's own texts and so comes in English.
	private static async Task<List<(string Text, string Path)>> HappinessTexts() =>
		(await Fm.GetJson("texts", ("anchorpath", "Person.PlayerDescription"), ("up", "13"), ("paths", "1"))).EnumerateArray()
			.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

	private static async Task<List<HappyRow>> HappinessRows()
	{
		static bool Listed(List<(string Text, string Path)> t) => t.Count(x => x.Path == "Person.PlayerDescription") > 1;

		var texts = await HappinessTexts();
		if (!Listed(texts))
		{
			var opened = await OpenSubpage(Screen.Dynamics, Screen.Happiness, async () => Listed(await HappinessTexts()));
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(opened);
				return [];
			}
		}

		// The list is the first team, and only lays out the rows in view: it is scrolled down until
		// every player of the team has had his row read. One the page leaves out costs a pass, not
		// a wrong answer.
		var team = new HashSet<string>();
		try
		{
			foreach (var p in (await Fm.Pull("Human.Team.Players", "Name", list: true)).EnumerateArray())
				if (Field(p, "Name") is { Length: > 0 } name) team.Add(name);
		}
		catch (FmError) { }
		var players = new List<HappyRow>();
		void Take(List<HappyRow> rows)
		{
			foreach (var row in rows)
				if (players.All(p => p.Name != row.Name)) players.Add(row);
		}
		bool Whole() => team.Count > 0 && team.All(n => players.Any(p => p.Name == n));

		var shown = await LaidOut([], moved: false);
		Take(shown);
		var anchor = Anchor(shown);
		if (anchor == null || Whole()) return players;
		foreach (var share in Sweep)
		{
			if (Whole()) break;
			if (await ScrollList(anchor, share) is not { } moved) break;
			shown = await LaidOut(shown, moved);
			Take(shown);
			anchor = Anchor(shown) ?? anchor;
		}
		await ScrollList(anchor, "0");
		return players;
	}

	/// <summary>
	/// Where the list is scrolled to, as shares of its length: the ends and the middle first, which
	/// on most screens reach every row, then the steps between for a long list on a small one.
	/// </summary>
	private static readonly string[] Sweep = ["0", "0.5", "1", "0.1", "0.2", "0.3", "0.4", "0.6", "0.7", "0.8", "0.9"];

	/// <summary>A description in view, to scroll the list by.</summary>
	private static string Anchor(List<HappyRow> shown) => shown.Count > 0 ? shown[^1].Description : null;

	/// <summary>Scrolls the list to a share of its length; whether it moved, or null when it cannot be scrolled.</summary>
	private static async Task<bool?> ScrollList(string anchor, string share)
	{
		var before = await Fm.Get("scroll", ("anchor", anchor));
		if (before.StartsWith("error", StringComparison.Ordinal)) return null;
		var after = await Fm.Get("scroll", ("anchor", anchor), ("to", share));
		if (after.StartsWith("error", StringComparison.Ordinal)) return null;
		return after.Trim() != before.Trim();
	}

	/// <summary>
	/// The rows in view once the list has laid them out. After a scroll the rows are rebuilt in more
	/// than one step (15 rows, then 21), so a reading counts once it differs from the one before the
	/// scroll and the next reading gives the same; not moved, the rows in view are already there. A
	/// page just opened lays out its names before their levels: no row yet is not a list laid out.
	/// </summary>
	private static async Task<List<HappyRow>> LaidOut(List<HappyRow> before, bool moved)
	{
		var last = Rows(await HappinessTexts());
		await Until(async () =>
		{
			var now = Rows(await HappinessTexts());
			var settled = now.Count > 0 && now.SequenceEqual(last, RowSame.Instance) && (!moved || !now.SequenceEqual(before, RowSame.Instance));
			last = now;
			return settled;
		}, seconds: 5, every: 0.05);
		return last;
	}

	private sealed class RowSame : IEqualityComparer<HappyRow>
	{
		public static readonly RowSame Instance = new();
		public bool Equals(HappyRow? a, HappyRow? b) => a?.Name == b?.Name && a?.Description == b?.Description && a?.Level == b?.Level;
		public int GetHashCode(HappyRow r) => r.Name.GetHashCode();
	}

	private static List<HappyRow> Rows(List<(string Text, string Path)> texts)
	{
		var players = new List<HappyRow>();
		var flags = new List<string>();
		string name = null, description = null;
		foreach (var (text, path) in texts)
		{
			switch (path)
			{
				case "StatusFlag.StatusTitleShort": flags.Add(text); break;
				case "binding.Name": name = text; description = null; break;
				case "Person.PlayerDescription": description = text; break;
				case "" when name != null && description != null:
					// A Turkish game's "Memnun" is the translation of both "Content" and "Pleased"; the
					// reverse table gives the latter, which is not one of the levels.
					players.Add(new HappyRow(name, description, text == "Pleased" ? "Content" : text, flags));
					flags = [];
					name = description = null;
					break;
			}
		}
		return players;
	}

	/// <summary>
	/// Selects his row and reads the panel it fills, by each text's data path (FINDINGS §8.169): his
	/// name (Person.Name), morale (Person.MoraleString), standing (Person.HierarchyInfluenceCategoryString)
	/// and the reasons (Reason.Name), split by the Positives and Negatives headings (game texts). A row
	/// out of view is not laid out, so the list is scrolled until it can be chosen.
	/// </summary>
	private static async Task<HappyWhy> WhyHappy(HappyRow player)
	{
		var selected = await Fm.Get("rowselect", ("text", player.Description), ("and", player.Name));
		// A row out of view is not laid out: the list is scrolled until his row is among those in view.
		var shown = Rows(await HappinessTexts());
		var anchor = Anchor(shown);
		foreach (var share in Sweep)
		{
			if (anchor == null || !selected.StartsWith("error", StringComparison.Ordinal)) break;
			if (await ScrollList(anchor, share) is not { } moved) break;
			shown = await LaidOut(shown, moved);
			anchor = Anchor(shown) ?? anchor;
			if (shown.Any(r => r.Name == player.Name && r.Description == player.Description))
				selected = await Fm.Get("rowselect", ("text", player.Description), ("and", player.Name));
		}
		if (selected.StartsWith("error", StringComparison.Ordinal)) return null;

		List<(string Text, string Path)> panel = [];
		if (!await Until(async () =>
		    {
			    panel = (await Fm.GetJson("texts", ("anchorpath", "Person.MoraleString"), ("up", "5"), ("paths", "1"))).EnumerateArray()
				    .Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			    return panel.Any(t => t.Path == "Person.Name" && t.Text == player.Name) && panel.Any(t => t.Text == "Negatives");
		    }, seconds: 5, every: 0.25)) return null;

		string Bound(string path) => panel.FirstOrDefault(t => t.Path == path).Text ?? "";
		var mid = panel.FindIndex(t => t.Text == "Negatives");
		var reasons = panel.Select((t, at) => (t.Text, t.Path, At: at)).Where(t => t.Path == "Reason.Name").ToList();
		// Morale and standing come in English (the plugin's reading language); a game reads in its own.
		return new HappyWhy(player.Name, GameWords.Of(player.Level), GameWords.Of(Bound("Person.MoraleString")), GameWords.Of(Bound("Person.HierarchyInfluenceCategoryString")),
			reasons.Where(r => r.At < mid).Select(r => r.Text).ToList(),
			reasons.Where(r => r.At > mid).Select(r => r.Text).ToList())
		{ Score = player.Score };
	}
}
