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

	private static readonly Dictionary<string, string> HappinessTurkish = new()
	{
		["Very Unhappy"] = "Çok mutsuz", ["Unhappy"] = "Mutsuz", ["Slightly Unhappy"] = "Biraz mutsuz", ["Content"] = "Memnun",
		["Slightly Happy"] = "Biraz mutlu", ["Happy"] = "Mutlu", ["Very Happy"] = "Çok mutlu", ["Delighted"] = "Keyfi yerinde",
	};

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
			Console.Error.WriteLine("Mutluluk sayfası okunamadı.");
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
				Console.Error.WriteLine(matches.Count == 0 ? $"'{typed}' mutluluk listesinde yok." : $"'{typed}' birden çok oyuncuya uyuyor: {string.Join(", ", matches.Select(p => p.Name))}");
				return 1;
			}
			if (await WhyHappy(matches[0]) is not { } why)
			{
				Console.Error.WriteLine($"{matches[0].Name}: nedenler okunamadı.");
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
				? $"{all.Count} oyuncunun hiçbirinin bir şikâyeti yok."
				: $"\n{all.Count} oyuncudan {upset.Count} tanesinin şikâyeti var; kalan {all.Count - upset.Count} oyuncunun yok.");
			if (all.Count < players.Count) Console.Error.WriteLine($"{players.Count - all.Count} oyuncunun nedenleri okunamadı.");
			return 0;
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(players.Select(p => new { p.Name, p.Level, p.Flags })));
			return 0;
		}
		foreach (var player in players)
		{
			var say = HappinessTurkish.GetValueOrDefault(player.Level, player.Level);
			var flag = player.Flags.Count > 0 ? $"  {string.Join(" ", player.Flags)}" : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(player.Name),-24} [{LevelColour(player.Level)}]{Ui.Safe(say)}[/][dim]{Ui.Safe(flag)}[/]");
			else Console.WriteLine($"  {player.Name,-24} {say}{flag}");
		}
		Console.WriteLine("\nNedeni: happiness <oyuncu>   ·   herkesin şikâyetleri: happiness --why");
		return 0;
	}

	private static string LevelColour(string level) =>
		Array.IndexOf(HappinessLevels, level) switch { <= 2 => "red", 3 or 4 => "yellow", _ => "green" };

	private static void WriteWhy(HappyWhy why, List<string> flags, bool positives)
	{
		var say = HappinessTurkish.GetValueOrDefault(why.Level, why.Level);
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
		if (why.Negatives.Count == 0) Console.WriteLine("  Şikâyeti yok.");
	}

	private static async Task<List<HappyRow>> HappinessRows()
	{
		// Any player's level is an anchor; the list sits a dozen levels above it.
		async Task<List<string>> Texts()
		{
			foreach (var level in HappinessLevels)
			{
				var texts = (await Fm.GetJson("texts", ("anchor", level), ("up", "12"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
				if (texts.Any(t => Regex.IsMatch(t, @"^\d+ year-old"))) return texts;
			}
			return [];
		}

		var texts = await Texts();
		if (texts.Count == 0)
		{
			var opened = await OpenPage("Dynamics", "Squad");
			if (opened.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(opened);
				return [];
			}
			await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Happiness"), ("fire", "1"));
			await Until(async () => (texts = await Texts()).Count > 0, seconds: 10, every: 0.5);
		}

		// The status column comes first, so a row's flags stand before its name: they are what
		// lies between the row before's level and this name.
		var players = new List<HappyRow>();
		var last = -1;
		for (var i = 1; i + 1 < texts.Count; i++)
		{
			if (!Regex.IsMatch(texts[i], @"^\d+ year-old") || !HappinessLevels.Contains(texts[i + 1])) continue;
			players.Add(new HappyRow(texts[i - 1], texts[i], texts[i + 1], texts.Skip(last + 1).Take(i - 1 - (last + 1)).ToList()));
			last = i + 1;
		}
		return players;
	}

	/// <summary>Selects his row and reads the panel it fills: happiness, morale, standing, positives, negatives.</summary>
	private static async Task<HappyWhy> WhyHappy(HappyRow player)
	{
		var selected = await Fm.Get("rowselect", ("text", player.Description), ("and", player.Name));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return null;

		List<string> panel = [];
		if (!await Until(async () =>
		    {
			    panel = (await Fm.GetJson("texts", ("anchor", "Morale"), ("up", "3"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			    return panel.Count > 0 && panel[0] == player.Name && panel.Contains("Negatives");
		    }, seconds: 5, every: 0.25)) return null;

		string After(string label) => panel.IndexOf(label) is >= 0 and var at && at + 1 < panel.Count ? panel[at + 1] : "";
		var from = panel.IndexOf("Positives");
		var mid = panel.IndexOf("Negatives");
		if (from < 0 || mid < from) return null;
		return new HappyWhy(player.Name, After("Happiness"), After("Morale"), After("Hierarchy"),
			panel.Skip(from + 1).Take(mid - from - 1).ToList(),
			panel.Skip(mid + 1).Where(t => t != "Actions").ToList());
	}
}
