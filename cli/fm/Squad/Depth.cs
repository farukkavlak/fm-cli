namespace FmCli;

/// <summary>
/// Squad depth: who can play each position, from the first team's position masks (the game's
/// PositionCombined, the same in every language), loanees and the under-19s left out; then the age
/// profile. Nothing is opened in the game beyond the squad table (§13 of the plan).
/// </summary>
internal static partial class Program
{
	// Rows of the pitch, from goal outwards; each slot is a role code and a side.
	private static readonly string[][] DepthRows =
	[
		["GK"],
		["DL", "DC", "DR"],
		["WBL", "DM", "WBR"],
		["ML", "MC", "MR"],
		["AML", "AMC", "AMR"],
		["ST"],
	];

	/// <summary>
	/// The game's position bits (a player's PositionCombined, the positions he plays well: what
	/// PositionCombinedString writes out as "D/WB (RL), DM" in the game's language) by slot.
	/// </summary>
	private static readonly (int Bit, string Slot)[] PositionBits =
	[
		(1, "GK"), (4, "DR"), (8, "DL"), (16, "DC"), (32, "WBR"), (64, "WBL"), (128, "DM"),
		(256, "MR"), (512, "ML"), (1024, "MC"), (2048, "AMR"), (4096, "AML"), (8192, "AMC"), (16384, "ST"),
	];

	/// <summary>A PositionCombined mask as slots: 36 → DR, WBR.</summary>
	internal static HashSet<string> Positions(string mask) =>
		int.TryParse(mask, out var bits) ? PositionBits.Where(p => (bits & p.Bit) != 0).Select(p => p.Slot).ToHashSet() : [];

	private static async Task<int> Depth(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var squad = await SquadView(SquadTable.GeneralInfo, "AgreedPlayingTime");
		var youth = await YouthTeams();
		// Players out on loan are someone else's; one loaned to us has us as his loan club.
		var (team, _) = await Fm.OwnTeam();
		var masks = (await Fm.Pull("Human.Team.Players", "Name,PositionCombined,LoanClub.Name", list: true)).EnumerateArray()
			.Where(p => Field(p, "LoanClub.Name") is not { Length: > 0 } club || club == team)
			.GroupBy(p => Field(p, "Name")).ToDictionary(g => g.Key, g => Field(g.First(), "PositionCombined"));
		var players = squad.EnumerateArray()
			.Where(p => !youth.Contains(Field(p, "Team")))
			.Where(p => masks.ContainsKey(Field(p, "Name")))
			.Select(p => (Name: Field(p, "Name"), Age: int.TryParse(Field(p, "Age"), out var a) ? a : 0, Slots: Positions(masks[Field(p, "Name")])))
			.ToList();

		var depth = DepthRows.SelectMany(r => r).ToDictionary(s => s, s => players.Where(p => p.Slots.Contains(s)).Select(p => p.Name).ToList());
		if (json)
		{
			Console.WriteLine(JsonOut.Of(depth));
			return 0;
		}

		foreach (var row in DepthRows.Reverse())
			foreach (var slot in row)
			{
				var names = depth[slot];
				var mark = names.Count == 0 ? L("NONE") : names.Count == 1 ? L("one") : $"{names.Count}";
				if (Ui.Rich)
					Ui.Line($"[bold]{slot,-4}[/] {(names.Count <= 1 ? "[red]" : names.Count == 2 ? "[yellow]" : "[green]")}{mark,3}[/]  {Ui.Safe(string.Join(", ", names))}");
				else Console.WriteLine($"{slot,-4} {mark,3}  {string.Join(", ", names)}");
			}

		var ages = players.Select(p => p.Age).Where(a => a > 0).ToList();
		Console.WriteLine();
		Console.WriteLine(L($"Age: average {ages.Average():0.0} · 21 and under {ages.Count(a => a <= 21)} · 22-25 {ages.Count(a => a is >= 22 and <= 25)} · 26-29 {ages.Count(a => a is >= 26 and <= 29)} · 30 and over {ages.Count(a => a >= 30)}  ({players.Count} players, loans and youth left out)"));
		return 0;
	}
}
