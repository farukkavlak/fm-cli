using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// Mentoring groups (Squad › Training › Mentoring, FINDINGS §8.102): which players are grouped
/// to learn from one another. Read from the data each group's graph binds — its members with
/// their personalities — and set up by asking the assistant, as the page offers.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Mentoring(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Training", "Squad");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		await Fm.Get("select", ("name", "SecondaryTabDropdownButton"), ("choice", "Mentoring"), ("fire", "1"));
		if (!await Until(async () => (await Fm.GetJson("texts", ("anchor", "Mentoring Groups"), ("up", "0"))).GetArrayLength() > 0, seconds: 8, every: 0.4))
		{
			Console.Error.WriteLine("Mentorluk sayfası açılmadı.");
			return 1;
		}

		async Task<List<(string Name, List<(string Player, string Personality)> Members)>> Groups()
		{
			List<(string Name, List<(string Player, string Personality)> Members)> groups = [];
			var previous = "?";
			await Until(async () =>
			{
				groups = MentoringGroups(await Fm.Get("tree", ("depth", "40")));
				var key = string.Join("|", groups.Select(g => g.Name + ":" + string.Join(",", g.Members.Select(m => m.Player))));
				var settled = key == previous;
				previous = key;
				return settled;
			}, seconds: 8, every: 0.6);
			return groups;
		}

		var groups = await Groups();
		if (args.Contains("--auto"))
		{
			// "Ask <assistant> to assign mentoring groups": his answer is a line to acknowledge.
			var ask = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
				.FirstOrDefault(b => Field(b, "Label").EndsWith("assign mentoring groups", StringComparison.Ordinal));
			if (ask.ValueKind != JsonValueKind.Object)
			{
				Console.Error.WriteLine("Sayfada yardımcıya grup kurdurma seçeneği yok.");
				return 1;
			}
			var said = await Fm.Get("press", ("ptr", Field(ask, "Pointer")));
			if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);
			await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Any(b => Field(b, "Label") == "Acknowledge and Move On"), seconds: 6, every: 0.4);
			await Fm.Get("press", ("text", "Acknowledge and Move On"), ("any", "1"));
			await Task.Delay(800);
			groups = await Groups();
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(groups.Select(g => new { g.Name, Members = g.Members.Select(m => new { m.Player, m.Personality }) })));
			return 0;
		}
		if (groups.Count == 0)
		{
			Console.WriteLine("Mentorluk grubu yok. Yardımcıya kurdurmak için: mentoring --auto");
			return 0;
		}
		foreach (var group in groups)
		{
			Console.WriteLine($"{group.Name} ({group.Members.Count})");
			Table.Write(["OYUNCU", "KİŞİLİK"], group.Members.Select(m => (string[])[m.Player, m.Personality]).ToList());
			Console.WriteLine();
		}
		return 0;
	}

	/// <summary>
	/// The groups in the page's bound data: each group's graph ("MentoringGroupRelationshipN")
	/// binds its members as a personality followed by a name, and the group's own name after them.
	/// </summary>
	private static List<(string Name, List<(string Player, string Personality)> Members)> MentoringGroups(string tree)
	{
		var groups = new List<(string Name, List<(string Player, string Personality)> Members)>();
		List<(string Player, string Personality)> members = null;
		string personality = null;
		foreach (var line in tree.Split('\n'))
		{
			if (Regex.IsMatch(line, @"^\s*MentoringGroupRelationship\d+\b"))
			{
				members = [];
				groups.Add(($"Mentoring Group {groups.Count + 1}", members));
				personality = null;
				continue;
			}
			if (members == null) continue;
			if (Regex.Match(line, @"^\s*Personality = \(String\) (.*)$") is { Success: true } p) { personality = p.Groups[1].Value.Trim(); continue; }
			if (Regex.Match(line, @"^\s*Name = \(String\) (.*)$") is not { Success: true } n) continue;
			var name = n.Groups[1].Value.Trim();
			if (personality != null)
			{
				if (members.All(m => m.Player != name)) members.Add((name, personality));
				personality = null;
			}
			else
			{
				// A name with no personality before it is the group's own, and ends its block.
				groups[^1] = (name, members);
				members = null;
			}
		}
		return groups.Where(g => g.Members.Count > 0).ToList();
	}
}
