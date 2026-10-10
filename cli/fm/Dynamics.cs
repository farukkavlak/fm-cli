using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The dressing room (FINDINGS §8.95): how many players back the manager, and where each one
/// stands — the social group he belongs to, his place in the hierarchy and his personality.
/// Read off Squad › Dynamics › Social Groups, which lists every player under his group.
/// </summary>
internal static partial class Program
{
	private static readonly string[] HierarchyOrder = ["Team Leader", "Highly Influential", "Influential", "Other"];

	private static readonly Dictionary<string, string> HierarchyTurkish = new()
	{
		["Team Leader"] = "Takım lideri", ["Highly Influential Player"] = "Çok etkili", ["Influential Player"] = "Etkili", ["Other"] = "Diğer", ["Others"] = "Diğer",
	};

	private static readonly Dictionary<string, string> SocialGroupTurkish = new()
	{
		["Core Social Group"] = "Çekirdek grup", ["Secondary Social Group A"] = "İkincil grup A", ["Secondary Social Group B"] = "İkincil grup B",
		["Secondary Social Group C"] = "İkincil grup C", ["Other Players"] = "Diğerleri", ["Others"] = "Gruba girmeyenler",
	};

	private static async Task<int> Dynamics(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage("Dynamics", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Social Groups"), ("fire", "1"));

		// The page is whole once two readings agree and at least one player is on it.
		List<string> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Core Social Group"), ("up", "8"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var key = string.Join("|", texts);
			var settled = key == previous && texts.Any(t => Regex.IsMatch(t, @"^\d+ year-old"));
			previous = key;
			return settled;
		}, seconds: 10, every: 0.5);

		// Each group: its name, a sentence on what holds it together, the column headings, then
		// for each player his name, "N year-old …", hierarchy and personality (status flags such
		// as Wnt or Inj may come before a name).
		var groups = new List<(string Name, string Why)>();
		var players = new List<(string Name, string About, string Hierarchy, string Personality, string Group)>();
		for (var i = 0; i < texts.Count; i++)
		{
			if (texts[i] == "Player" && i >= 2 && i + 2 < texts.Count && texts[i + 1] == "Hierarchy" && texts[i + 2] == "Personality")
				groups.Add(texts[i - 1].Contains(' ') && texts[i - 1].Length > 30 ? (texts[i - 2], texts[i - 1]) : (texts[i - 1], ""));
			else if (Regex.IsMatch(texts[i], @"^\d+ year-old") && groups.Count > 0 && i >= 1 && i + 2 < texts.Count)
				players.Add((texts[i - 1], texts[i], texts[i + 1], texts[i + 2], groups[^1].Name));
		}
		if (players.Count == 0)
		{
			Console.Error.WriteLine("Sosyal gruplar okunamadı.");
			return 1;
		}

		var team = await Fm.Pull("Human.Team", "ManagerSupportPlayersSupportiveCount,ManagerSupportPlayersNeutralCount,ManagerSupportPlayersOpposingCount");
		string Count(string which) => Field(team, $"ManagerSupportPlayers{which}Count");

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Support = new { Supportive = Count("Supportive"), Neutral = Count("Neutral"), Opposing = Count("Opposing") },
				Groups = groups.Select(g => new
				{
					g.Name, g.Why,
					Players = players.Where(p => p.Group == g.Name).Select(p => new { p.Name, p.About, p.Hierarchy, p.Personality }),
				}),
			}));
			return 0;
		}

		if (Count("Supportive").Length > 0)
			Console.WriteLine($"Menajere destek: {Count("Supportive")} destekliyor · {Count("Neutral")} kararsız · {Count("Opposing")} karşı\n");
		static int Rank(string hierarchy) => Array.FindIndex(HierarchyOrder, h => hierarchy.StartsWith(h, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : HierarchyOrder.Length;
		foreach (var group in groups)
		{
			var members = players.Where(p => p.Group == group.Name).OrderBy(p => Rank(p.Hierarchy)).ToList();
			if (members.Count == 0) continue;
			Console.WriteLine($"{SocialGroupTurkish.GetValueOrDefault(group.Name, group.Name)} ({members.Count})");
			if (group.Why.Length > 0) Console.WriteLine($"  {group.Why}");
			Table.Write(["OYUNCU", "HİYERARŞİ", "KİŞİLİK"],
				members.Select(p => (string[])[p.Name, HierarchyTurkish.GetValueOrDefault(p.Hierarchy, p.Hierarchy), p.Personality]).ToList());
			Console.WriteLine();
		}
		return 0;
	}
}
