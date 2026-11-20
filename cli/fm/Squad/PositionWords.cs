namespace FmCli;

/// <summary>
/// A position typed the way the game writes a player's positions ("OS (M)", "D (SğSl)", "AM (RLC)").
/// The game writes them as groups of roles that share sides: "KB/OS/OOS (Sl)" is the left wing-back, left
/// midfielder and left attacking midfielder. Its words for the roles and the sides are learned from the squad
/// itself: each player's position bits (PositionCombined, the same in every language) beside the words the
/// game shows for them (PositionCombinedString). No word of any language is written here.
/// </summary>
internal static partial class Program
{
	// Each slot as a role and a side; the goalkeeper and the defensive midfielder are written with no side.
	private static readonly Dictionary<string, (string Role, string Side)> SlotParts = new()
	{
		["GK"] = ("GK", ""), ["DR"] = ("D", "R"), ["DL"] = ("D", "L"), ["DC"] = ("D", "C"),
		["WBR"] = ("WB", "R"), ["WBL"] = ("WB", "L"), ["DM"] = ("DM", ""),
		["MR"] = ("M", "R"), ["ML"] = ("M", "L"), ["MC"] = ("M", "C"),
		["AMR"] = ("AM", "R"), ["AML"] = ("AM", "L"), ["AMC"] = ("AM", "C"), ["ST"] = ("ST", "C"),
	};

	private static Dictionary<string, string>? roleWords, sideWords;

	/// <summary>A shown position text as its groups: the roles (split at '/') and the sides' text, empty when it has none.</summary>
	private static List<(List<string> Roles, string Sides)> PositionGroups(string shown) =>
		shown.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(group =>
		{
			var open = group.IndexOf('(');
			var roles = (open < 0 ? group : group[..open]).Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(Fold).ToList();
			var sides = open < 0 ? "" : Fold(group[(open + 1)..].TrimEnd(')', ' '));
			return (roles, sides);
		}).ToList();

	/// <summary>
	/// The sides' text cut into the side words already learned, longest first; null when some of it is not one of them.
	/// </summary>
	private static List<string>? SideTokens(string text, IReadOnlyDictionary<string, string> known)
	{
		var found = new List<string>();
		var at = 0;
		while (at < text.Length)
		{
			var word = known.Keys.Where(k => k.Length > 0 && string.CompareOrdinal(text, at, k, 0, k.Length) == 0).MaxBy(k => k.Length);
			if (word == null) return null;
			found.Add(known[word]);
			at += word.Length;
		}
		return found;
	}

	/// <summary>Learns the game's words for roles and sides from the squad; once per run.</summary>
	private static async Task LearnPositionWords()
	{
		if (roleWords != null) return;
		roleWords = [];
		sideWords = [];
		List<(HashSet<(string Role, string Side)> Parts, List<(List<string> Roles, string Sides)> Groups)> players = [];
		try
		{
			foreach (var p in (await Fm.Pull("Human.Team.Players", "PositionCombined,PositionCombinedString", list: true)).EnumerateArray())
			{
				var shown = GameWords.Of(Field(p, "PositionCombinedString"));
				var parts = Positions(Field(p, "PositionCombined")).Where(SlotParts.ContainsKey).Select(s => SlotParts[s]).ToHashSet();
				if (shown.Length > 0 && parts.Count > 0) players.Add((parts, PositionGroups(shown)));
			}
		}
		catch (FmError) { return; }

		// Each pass settles what one player's groups leave only one way to read; it stops when a pass learns nothing.
		for (var pass = 0; pass < 12; pass++)
		{
			var learned = false;
			foreach (var (parts, groups) in players)
			{
				// Roles the player's other groups already account for.
				var named = groups.SelectMany(g => g.Roles).Where(roleWords.ContainsKey).Select(r => roleWords[r]).ToHashSet();
				foreach (var (roles, sides) in groups)
				{
					if (sides.Length == 0)
					{
						// A role with no side: the goalkeeper or the defensive midfielder.
						var open = parts.Where(p => p.Side == "" && !named.Contains(p.Role)).Select(p => p.Role).Distinct().ToList();
						if (roles.Where(r => !roleWords.ContainsKey(r)).ToList() is [var unknownRole] && open.Count == 1)
						{
							roleWords[unknownRole] = open[0];
							named.Add(open[0]);
							learned = true;
						}
						continue;
					}
					// The group's sides: by a role in it already known, or by its text cut into known side words.
					var knownRole = roles.Where(roleWords.ContainsKey).Select(r => roleWords[r]).FirstOrDefault();
					// With nothing known yet, a player whose only group with sides is this one has exactly its sides.
					var sideSet = knownRole != null
						? parts.Where(p => p.Role == knownRole).Select(p => p.Side).ToHashSet()
						: SideTokens(sides, sideWords)?.ToHashSet()
						  ?? (groups.Count(g => g.Sides.Length > 0) == 1 ? parts.Where(p => p.Side != "").Select(p => p.Side).ToHashSet() : null);
					if (sideSet is not { Count: > 0 }) continue;

					// A side word still unknown: the text left once the known ones are taken out, for the one side not yet named.
					if (SideTokens(sides, sideWords) == null)
					{
						var rest = sides;
						foreach (var word in sideWords.Where(w => sideSet.Contains(w.Value)).Select(w => w.Key).OrderByDescending(k => k.Length))
							rest = rest.Replace(word, "", StringComparison.Ordinal);
						var unnamed = sideSet.Where(s => !sideWords.ContainsValue(s)).ToList();
						if (rest.Length > 0 && unnamed.Count == 1)
						{
							sideWords[rest] = unnamed[0];
							learned = true;
						}
					}
					// A role word still unknown: the one role of these sides the player has that no other word accounts for.
					if (roles.Where(r => !roleWords.ContainsKey(r)).ToList() is [var unknown])
					{
						var left = parts.Where(p => sideSet.Contains(p.Side) && p.Side != "" && !named.Contains(p.Role)).Select(p => p.Role).Distinct().ToList();
						if (left.Count == 1)
						{
							roleWords[unknown] = left[0];
							named.Add(left[0]);
							learned = true;
						}
					}
				}
			}
			if (!learned) break;
		}
	}

	/// <summary>
	/// The slots a position typed as the game writes it stands for ("OS (M)" → MC); null when its words are not
	/// ones the squad has taught.
	/// </summary>
	internal static async Task<List<string>?> PositionsFromShown(string typed)
	{
		await LearnPositionWords();
		if (roleWords is not { Count: > 0 } || sideWords == null) return null;
		var slots = new List<string>();
		foreach (var (roles, sides) in PositionGroups(typed))
		{
			var sideList = sides.Length == 0 ? [""] : SideTokens(sides, sideWords);
			if (sideList == null) return null;
			foreach (var role in roles)
			{
				if (!roleWords.TryGetValue(role, out var english)) return null;
				foreach (var side in sideList)
				{
					var slot = SlotParts.FirstOrDefault(s => s.Value == (english, side)).Key;
					if (slot == null) return null;
					slots.Add(slot);
				}
			}
		}
		return slots.Count > 0 ? slots.Distinct().ToList() : null;
	}
}
