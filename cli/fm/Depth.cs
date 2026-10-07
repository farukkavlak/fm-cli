using System.Text.Json;

namespace FmCli;

/// <summary>
/// Squad depth: who can play each position, from the first team's position strings
/// ("D (RLC), WB (R)", "M/AM (C)", "GK"), loanees and the under-19s left out; then the age
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

	/// <summary>"D/WB (RL), DM" → DL, DR, WBL, WBR, DM.</summary>
	internal static HashSet<string> Positions(string text)
	{
		var slots = new HashSet<string>();
		foreach (var part in text.Split(", ", StringSplitOptions.RemoveEmptyEntries))
		{
			var open = part.IndexOf('(');
			var roles = (open < 0 ? part : part[..open]).Trim().Split('/');
			var sides = open < 0 ? "C" : part[(open + 1)..].TrimEnd(')');
			foreach (var role in roles)
			{
				if (role is "GK" or "DM" or "ST") { slots.Add(role); continue; }
				foreach (var side in sides)
				{
					// Wing-backs have no centre; a centre striker is the striker.
					if (role == "WB" && side == 'C') continue;
					slots.Add($"{role}{side}");
				}
			}
		}
		return slots;
	}

	private static async Task<int> Depth(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var squad = await SquadView("Overview", "PositionCombinedString");
		var players = squad.EnumerateArray()
			.Where(p => !Field(p, "Team").Contains("U19", StringComparison.Ordinal) && Field(p, "AgreedPlayingTime") != "Out On Loan")
			.Where(p => Field(p, "PositionCombinedString").Length > 0)
			.Select(p => (Name: Field(p, "Name"), Age: int.TryParse(Field(p, "Age"), out var a) ? a : 0, Slots: Positions(Field(p, "PositionCombinedString"))))
			.ToList();

		var depth = DepthRows.SelectMany(r => r).ToDictionary(s => s, s => players.Where(p => p.Slots.Contains(s)).Select(p => p.Name).ToList());
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(depth));
			return 0;
		}

		foreach (var row in DepthRows.Reverse())
			foreach (var slot in row)
			{
				var names = depth[slot];
				var mark = names.Count == 0 ? "YOK" : names.Count == 1 ? "tek" : $"{names.Count}";
				if (Ui.Rich)
					Ui.Line($"[bold]{slot,-4}[/] {(names.Count <= 1 ? "[red]" : names.Count == 2 ? "[yellow]" : "[green]")}{mark,3}[/]  {Ui.Safe(string.Join(", ", names))}");
				else Console.WriteLine($"{slot,-4} {mark,3}  {string.Join(", ", names)}");
			}

		var ages = players.Select(p => p.Age).Where(a => a > 0).ToList();
		Console.WriteLine();
		Console.WriteLine($"Yaş: ortalama {ages.Average():0.0} · 21 ve altı {ages.Count(a => a <= 21)} · 22-25 {ages.Count(a => a is >= 22 and <= 25)} · 26-29 {ages.Count(a => a is >= 26 and <= 29)} · 30 ve üstü {ages.Count(a => a >= 30)}  ({players.Count} oyuncu, kiralıklar ve U19 hariç)");
		return 0;
	}
}
