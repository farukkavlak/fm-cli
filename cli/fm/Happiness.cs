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

	/// <summary>A row of the page; Description ("27 year-old winger") is what tells the row from the same name on the page behind.</summary>
	private sealed record HappyRow(string Name, string Description, string Level, List<string> Flags);

	/// <summary>The panel of the selected player.</summary>
	private sealed record HappyWhy(string Name, string Level, string Morale, string Hierarchy, List<string> Positives, List<string> Negatives);

	private static async Task<int> Happiness(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var players = await HappinessRows();
		if (players.Count == 0)
		{
			Console.Error.WriteLine(L("Mutluluk sayfası okunamadı."));
			return 1;
		}
		players = players.OrderBy(p => Array.IndexOf(HappinessLevels, p.Level)).ThenBy(p => p.Name).ToList();
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
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, p.Level, p.Flags })));
			return 0;
		}
		foreach (var player in players)
		{
			var say = player.Level;
			var flag = player.Flags.Count > 0 ? $"  {string.Join(" ", player.Flags)}" : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(player.Name),-24} [{LevelColour(player.Level)}]{Ui.Safe(say)}[/][dim]{Ui.Safe(flag)}[/]");
			else Console.WriteLine($"  {player.Name,-24} {say}{flag}");
		}
		Console.WriteLine(L("\nNedeni: happiness <oyuncu>   ·   herkesin şikâyetleri: happiness --why"));
		return 0;
	}

	private static string LevelColour(string level) =>
		Array.IndexOf(HappinessLevels, level) switch { <= 2 => "red", 3 or 4 => "yellow", _ => "green" };

	private static void WriteWhy(HappyWhy why, List<string> flags, bool positives)
	{
		var say = why.Level;
		var rest = $" · moral {why.Morale} · {why.Hierarchy}{(flags.Count > 0 ? $" · {string.Join(" ", flags)}" : "")}";
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(why.Name)}[/]  [{LevelColour(why.Level)}]{Ui.Safe(say)}[/][dim]{Ui.Safe(rest)}[/]");
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

	private static async Task<List<HappyRow>> HappinessRows()
	{
		// Read by each text's data path (FINDINGS §8.169), anchored on a player's description, which
		// only rows of this list carry: a row is its flags (StatusFlag.StatusTitleShort), the name
		// (binding.Name), the description (Person.PlayerDescription), then the level, which is drawn
		// without a path but is one of the game's own texts and so comes in English.
		async Task<List<(string Text, string Path)>> Texts() =>
			(await Fm.GetJson("texts", ("anchorpath", "Person.PlayerDescription"), ("up", "13"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		static bool Listed(List<(string Text, string Path)> t) => t.Count(x => x.Path == "Person.PlayerDescription") > 1;

		var texts = await Texts();
		if (!Listed(texts))
		{
			var opened = await OpenSubpage(Screen.Dynamics, Screen.Happiness, async () => Listed(await Texts()));
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(opened);
				return [];
			}
			texts = await Texts();
		}

		var players = Rows(texts);
		// The list only lays out the rows in view; the rest come as it is scrolled, by a row's description.
		var anchor = texts.LastOrDefault(t => t.Path == "Person.PlayerDescription").Text;
		if (anchor != null)
		{
			foreach (var share in new[] { "0.5", "1" })
			{
				if ((await Fm.Get("scroll", ("anchor", anchor), ("to", share))).StartsWith("error", StringComparison.Ordinal)) break;
				await Task.Delay(500);
				foreach (var row in Rows(await Texts()))
					if (players.All(p => p.Name != row.Name)) players.Add(row);
			}
			await Fm.Get("scroll", ("anchor", anchor), ("to", "0"));
		}
		return players;
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
		var anchor = (await Fm.GetJson("texts", ("anchorpath", "Person.PlayerDescription"), ("up", "0"))).EnumerateArray().Select(t => t.GetString()).FirstOrDefault();
		foreach (var share in new[] { "0", "0.5", "1" })
		{
			if (!selected.StartsWith("error", StringComparison.Ordinal) || anchor == null) break;
			await Fm.Get("scroll", ("anchor", anchor), ("to", share));
			await Task.Delay(400);
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
		return new HappyWhy(player.Name, player.Level, Bound("Person.MoraleString"), Bound("Person.HierarchyInfluenceCategoryString"),
			reasons.Where(r => r.At < mid).Select(r => r.Text).ToList(),
			reasons.Where(r => r.At > mid).Select(r => r.Text).ToList());
	}
}
