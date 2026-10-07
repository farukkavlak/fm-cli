using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// How happy each player is (Squad › Dynamics › Happiness, FINDINGS §8.59): read off the page as
/// name, "N year-old …", level, then status flags (Wnt, Unr, FtT, Rst…). Least happy first.
/// </summary>
internal static partial class Program
{
	private static readonly string[] HappinessLevels =
		["Very Unhappy", "Unhappy", "Slightly Unhappy", "Content", "Slightly Happy", "Happy", "Very Happy"];

	private static readonly Dictionary<string, string> HappinessTurkish = new()
	{
		["Very Unhappy"] = "Çok mutsuz", ["Unhappy"] = "Mutsuz", ["Slightly Unhappy"] = "Biraz mutsuz", ["Content"] = "Memnun",
		["Slightly Happy"] = "Biraz mutlu", ["Happy"] = "Mutlu", ["Very Happy"] = "Çok mutlu",
	};

	private static async Task<int> Happiness(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Dynamics", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Happiness"), ("fire", "1"));

		// Any player's level is an anchor; the list sits a dozen levels above it.
		List<string> texts = [];
		await Until(async () =>
		{
			foreach (var level in HappinessLevels)
			{
				texts = (await Fm.GetJson("texts", ("anchor", level), ("up", "12"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
				if (texts.Any(t => Regex.IsMatch(t, @"^\d+ year-old"))) return true;
			}
			return false;
		}, seconds: 10, every: 0.5);

		var players = new List<(string Name, string Level, List<string> Flags)>();
		for (var i = 1; i + 1 < texts.Count; i++)
		{
			if (!Regex.IsMatch(texts[i], @"^\d+ year-old") || !HappinessLevels.Contains(texts[i + 1])) continue;
			var flags = new List<string>();
			for (var j = i + 2; j < texts.Count && texts[j].Length <= 3 && !Regex.IsMatch(texts[j], @"^\d+ year-old"); j++) flags.Add(texts[j]);
			players.Add((texts[i - 1], texts[i + 1], flags));
		}
		if (players.Count == 0)
		{
			Console.Error.WriteLine("Mutluluk sayfası okunamadı.");
			return 1;
		}
		players = players.OrderBy(p => Array.IndexOf(HappinessLevels, p.Level)).ThenBy(p => p.Name).ToList();

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(players.Select(p => new { p.Name, p.Level, p.Flags })));
			return 0;
		}
		foreach (var (name, level, flags) in players)
		{
			var say = HappinessTurkish[level];
			var flag = flags.Count > 0 ? $"  {string.Join(" ", flags)}" : "";
			if (Ui.Rich)
			{
				var colour = Array.IndexOf(HappinessLevels, level) switch { <= 2 => "red", 3 or 4 => "yellow", _ => "green" };
				Ui.Line($"  {Ui.Safe(name),-24} [{colour}]{Ui.Safe(say)}[/][dim]{Ui.Safe(flag)}[/]");
			}
			else Console.WriteLine($"  {name,-24} {say}{flag}");
		}
		return 0;
	}
}
