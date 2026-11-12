using System.Text.Json;

namespace FmCli;

/// <summary>
/// The dressing room: how many players back the manager, and where each one
/// stands — the social group he belongs to, his place in the hierarchy and his personality.
///
/// Held by the team's own data, not the page's layout: each kind of group is a list of its own under
/// Human.Team.SocialGroupList, what holds it together is Team.&lt;Kind&gt;SocialGroupReasons, and a member's
/// standing is his HierarchyInfluenceCategory (9000 team leader, 7500 highly influential, 6000 influential…),
/// read with the team beside his row as Happiness does. The groups' names are the CLI's own words.
/// </summary>
internal static partial class Program
{
	private sealed record SocialKind(string List, string Reasons, string Shown);

	/// <summary>The game's kinds of social group, by their data fields.</summary>
	private static SocialKind[] SocialKinds() =>
	[
		new("CoreSocialGroupPlayersList", "CoreSocialGroupReasons", L("Core group")),
		new("SecondarySocialGroup1PlayersList", "Secondary1SocialGroupReasons", L("Second group")),
		new("SecondarySocialGroup2PlayersList", "Secondary2SocialGroupReasons", L("Third group")),
		new("OtherSocialGroupPlayersList", "OtherSocialGroupReasons", L("In no group")),
	];

	private sealed record SocialMember(string Name, string Id, string About, string Hierarchy, int Standing, string Personality);

	private static async Task<int> Dynamics(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var kinds = SocialKinds();
		var groups = await SocialGroups(kinds);
		// The lists are bound once the game has drawn them; a session that never showed the page has them empty.
		if (groups.All(g => g.Members.Count == 0))
		{
			var opened = await OpenSubpage(Screen.Dynamics, Screen.SocialGroups, async () => (await SocialGroups(kinds)).Any(g => g.Members.Count > 0));
			if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
			groups = await SocialGroups(kinds);
		}
		if (groups.All(g => g.Members.Count == 0))
		{
			Console.Error.WriteLine(L("The social groups could not be read."));
			return 1;
		}

		var team = await Fm.Pull("Human.Team", "ManagerSupportPlayersSupportiveCount,ManagerSupportPlayersNeutralCount,ManagerSupportPlayersOpposingCount");
		string Count(string which) => Field(team, $"ManagerSupportPlayers{which}Count");
		var shown = groups.Where(g => g.Members.Count > 0).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Support = new { Supportive = Count("Supportive"), Neutral = Count("Neutral"), Opposing = Count("Opposing") },
				Groups = shown.Select(g => new
				{
					Kind = g.Kind.List, Name = g.Kind.Shown, g.Why,
					Players = g.Members.Select(p => new { p.Name, p.Id, p.About, p.Hierarchy, p.Standing, p.Personality }),
				}),
			}));
			return 0;
		}

		if (Count("Supportive").Length > 0)
			Console.WriteLine(L($"Support for the manager: {Count("Supportive")} supportive · {Count("Neutral")} undecided · {Count("Opposing")} against\n"));
		foreach (var group in shown)
		{
			Console.WriteLine($"{group.Kind.Shown} ({group.Members.Count})");
			if (group.Why.Length > 0) Console.WriteLine($"  {group.Why}");
			Table.Write([L("PLAYER"), L("HIERARCHY"), L("PERSONALITY")],
				group.Members.Select(p => (string[])[p.Name, p.Hierarchy, p.Personality]).ToList());
			Console.WriteLine();
		}
		return 0;
	}

	/// <summary>Each kind of group with its members, most influential first; a kind the game has no list for is empty.</summary>
	private static async Task<List<(SocialKind Kind, string Why, List<SocialMember> Members)>> SocialGroups(SocialKind[] kinds)
	{
		JsonElement reasons;
		try { reasons = await Fm.Pull("Human.Team", string.Join(",", kinds.Select(k => k.Reasons))); }
		catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { return kinds.Select(k => (k, "", new List<SocialMember>())).ToList(); }

		var groups = new List<(SocialKind, string, List<SocialMember>)>();
		foreach (var kind in kinds)
		{
			var members = new List<SocialMember>();
			JsonElement rows = default;
			try { rows = await Fm.Pull($"Human.Team.SocialGroupList.{kind.List}", "Name,UniqueId,Personality,PlayerDescription", list: true); }
			catch (Exception e) when (e is FmError or FmUnavailable or FmBusy) { }
			var items = rows.ValueKind == JsonValueKind.Array
				? rows.EnumerateArray().Where(r => Field(r, "Row").Contains('.') && Field(r, "UniqueId").Length > 0).ToList()
				: [];
			if (items.Count > 0)
			{
				// A member's standing is his in a team: the team (and the manager, as the one asking) beside the rows.
				var under = Field(items[0], "Row")[..Field(items[0], "Row").LastIndexOf('.')];
				foreach (var (variable, from) in HappinessContext)
					await Until(async () => !(await Fm.Get("pathset", ("to", $"{under}.{variable}"), ("from", from))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.2);
				var standing = await PullEach(items.Select(r => (Field(r, "Row"), "HierarchyInfluenceCategory,HierarchyInfluenceCategoryString")).ToList(),
					r => Field(r, "HierarchyInfluenceCategory").Length > 0);
				members = items.Zip(standing, (r, s) => new SocialMember(Field(r, "Name"), Field(r, "UniqueId"), Field(r, "PlayerDescription"),
						Field(s, "HierarchyInfluenceCategoryString"), int.TryParse(Field(s, "HierarchyInfluenceCategory"), out var n) ? n : 0, Field(r, "Personality")))
					.OrderByDescending(m => m.Standing).ToList();
			}
			groups.Add((kind, Field(reasons, kind.Reasons), members));
		}
		return groups;
	}
}
