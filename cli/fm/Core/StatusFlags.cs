using System.Text.Json;

namespace FmCli;

/// <summary>
/// What the squad table's status column abbreviates — "Unr", "Rst", "Loa" — in full and with its
/// reason. Each player keeps a list of flags for a team; the table shows only the first and only
/// three letters of it. The list wants the team and a sort order beside it.
/// </summary>
internal static partial class Program
{
	/// <param name="Id">The game's own number for the flag (FM.UI.PlayerStatusFlag: 20 retiring, 75 free agent), the same in every
	/// language; -1 for a status that is not a flag (on holiday).</param>
	/// <param name="Type">The game's kind of status (StatusType), set for every one.</param>
	/// <param name="Ineligible">Why he cannot play, by the game's number (FM.UI.PlayerIneligibilityReasonFlag: 3 joining his
	/// national side, 4 coming back from it, 5 on international duty, 15 on holiday), 0 when he can.</param>
	internal record Flag(string Short, string Full, string Reason, int Id = -1, int Type = -1, int Ineligible = 0);

	/// <summary>Away with his national side: joining it, with it, or on his way back.</summary>
	internal static bool WithNation(Flag flag) => flag.Ineligible is 3 or 4 or 5;

	private const int FreeAgentFlag = 75;

	/// <summary>Every flag of each of our first-team players by name; empty when the game gives none.</summary>
	private static async Task<Dictionary<string, List<Flag>>> SquadFlags()
	{
		var flags = new Dictionary<string, List<Flag>>();
		JsonElement players;
		try { players = await Fm.Pull("Human.Team.Players", "Name", list: true); }
		catch (FmError) { return flags; }
		if (players.ValueKind != JsonValueKind.Array) return flags;
		var rows = players.EnumerateArray().Select(p => (Name: Field(p, "Name"), Row: Field(p, "Row"))).Where(r => r.Row.Contains('.') && r.Name.Length > 0).ToList();
		if (rows.Count == 0) return flags;

		// The rows hang under one node: what is put beside them is in reach of every one.
		var under = rows[0].Row[..rows[0].Row.LastIndexOf('.')];
		try
		{
			await Fm.Get("pathset", ("to", under + ".team"), ("from", "Human.Team"));
			await Fm.Get("pathset", ("to", under + ".PlayerStatusSortType"), ("int", "0"));
		}
		catch (FmError) { return flags; }

		const string props = "StatusTitleShort,StatusTitleFull,StatusReason,StatusFlag,StatusType,IneligibilityReason";
		var waiting = rows.ToList();
		// A list is bound by its first reading and filled on a later one: all are asked each round.
		for (var round = 0; round < 5 && waiting.Count > 0; round++)
		{
			foreach (var r in waiting.ToList())
			{
				string answer;
				try { answer = await Fm.Get("pull", ("path", r.Row + ".PlayerStatusFlagsForTeam"), ("props", props), ("list", "1"), ("gen", "f")); }
				catch (FmError) { continue; }
				if (answer.StartsWith("error", StringComparison.Ordinal)) continue;
				var read = JsonDocument.Parse(answer).RootElement;
				if (read!.ValueKind != JsonValueKind.Array) continue;
				var entries = read.EnumerateArray().ToList();
				if (entries.Any(e => Field(e, "StatusTitleShort").Length == 0)) continue;
				// An empty list on the first round may only not be bound yet.
				if (round == 0 && entries.Count == 0) continue;
				flags[r.Name] = entries.Select(e => new Flag(Field(e, "StatusTitleShort"), Field(e, "StatusTitleFull"), Field(e, "StatusReason"), int.TryParse(Field(e, "StatusFlag"), out var id) ? id : -1, int.TryParse(Field(e, "StatusType"), out var type) ? type : -1, int.TryParse(Field(e, "IneligibilityReason"), out var why) ? why : 0)).ToList();
				waiting.Remove(r);
			}
			if (waiting.Count > 0) await Task.Delay(250);
		}
		return flags;
	}

	/// <summary>A row's status in words: every flag of the player, else the table's three letters.</summary>
	private static string Status(Dictionary<string, List<Flag>> flags, JsonElement row) =>
		flags.TryGetValue(Field(row, "Name"), out var mine) && mine.Count > 0 ? FlagWords(mine) : Field(row, "StatusTitleShort");

	/// <summary>
	/// Flags in full, one after another. A free agent in our own squad is one here on trial: the
	/// game has no flag of its own for that.
	/// </summary>
	internal static string FlagWords(IEnumerable<Flag> flags) =>
		string.Join(", ", flags.Select(f => (f.Full.Length > 0 ? f.Full : f.Short) is var t && f.Id == FreeAgentFlag ? L($"{t} (on trial)") : t).Distinct());
}
