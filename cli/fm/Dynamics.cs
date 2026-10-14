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
		// A Turkish game's words are each the translation of the singular and the plural; the reverse table gives the plural.
		["Team Leaders"] = "Takım lideri", ["Highly Influential Players"] = "Çok etkili", ["Influential Players"] = "Etkili",
	};

	private static readonly Dictionary<string, string> SocialGroupTurkish = new()
	{
		["Core Social Group"] = "Çekirdek grup", ["Secondary Social Group A"] = "İkincil grup A", ["Secondary Social Group B"] = "İkincil grup B",
		["Secondary Social Group C"] = "İkincil grup C", ["Other Players"] = "Diğerleri", ["Others"] = "Gruba girmeyenler",
	};

	private static async Task<int> Dynamics(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		async Task<List<(string Text, string Path)>> Read() =>
			(await Fm.GetJson("texts", ("anchorpath", "binding.PlayerDescription"), ("up", "32"), ("paths", "1"), ("most", "1")))
				.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		var opened = await OpenSubpage(Screen.Dynamics, Screen.SocialGroups,
			async () => (await Read()).Any(t => t.Path.EndsWith("SocialGroupReasons", StringComparison.Ordinal)));
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		// Read by each text's data path (FINDINGS §8.169), anchored on a player's description. A group
		// is its heading (drawn without a path, one of the game's texts and so in English), a sentence on
		// what holds it together (Team.<Group>SocialGroupReasons), the column headings, then for each
		// player his name, description, hierarchy (binding.HierarchyInfluenceCategoryString) and
		// personality. The page is whole once two readings agree and at least one player is on it.
		List<(string Text, string Path)> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = await Read();
			var key = string.Join("|", texts.Select(t => t.Text));
			var settled = key == previous && texts.Any(t => t.Path == "binding.PlayerDescription");
			previous = key;
			return settled;
		}, seconds: 10, every: 0.5);

		var groups = new List<(string Name, string Why)>();
		var players = new List<(string Name, string About, string Hierarchy, string Personality, string Group)>();
		var between = new List<(string Text, string Path)>();
		string name = null, about = null, hierarchy = null;
		foreach (var (text, path) in texts)
		{
			if (!path.StartsWith("binding", StringComparison.Ordinal))
			{
				between.Add((text, path));
				continue;
			}
			// What came between two players' rows, when anything did, is the next group's heading.
			if (between.Count > 0 && path == "binding.Name")
			{
				if (between.FirstOrDefault(t => t.Path.Length == 0) is { Text: { } heading })
					groups.Add((heading, between.FirstOrDefault(t => t.Path.EndsWith("SocialGroupReasons", StringComparison.Ordinal)).Text ?? ""));
				between.Clear();
			}
			if (groups.Count == 0) continue;
			switch (path)
			{
				case "binding.Name": name = text; about = hierarchy = null; break;
				case "binding.PlayerDescription": about = text; break;
				case "binding.HierarchyInfluenceCategoryString": hierarchy = text; break;
				case "binding" when name != null && about != null:
					players.Add((name, about, hierarchy ?? "", text, groups[^1].Name));
					name = null;
					break;
			}
		}
		if (players.Count == 0)
		{
			Console.Error.WriteLine(L("Sosyal gruplar okunamadı."));
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
			Console.WriteLine(L($"Menajere destek: {Count("Supportive")} destekliyor · {Count("Neutral")} kararsız · {Count("Opposing")} karşı\n"));
		static int Rank(string hierarchy) => Array.FindIndex(HierarchyOrder, h => hierarchy.StartsWith(h, StringComparison.OrdinalIgnoreCase)) is var i and >= 0 ? i : HierarchyOrder.Length;
		foreach (var group in groups)
		{
			var members = players.Where(p => p.Group == group.Name).OrderBy(p => Rank(p.Hierarchy)).ToList();
			if (members.Count == 0) continue;
			Console.WriteLine($"{SocialGroupTurkish.GetValueOrDefault(group.Name, group.Name)} ({members.Count})");
			if (group.Why.Length > 0) Console.WriteLine($"  {group.Why}");
			Table.Write([L("OYUNCU"), L("HİYERARŞİ"), L("KİŞİLİK")],
				members.Select(p => (string[])[p.Name, HierarchyTurkish.GetValueOrDefault(p.Hierarchy, p.Hierarchy), p.Personality]).ToList());
			Console.WriteLine();
		}
		return 0;
	}
}
