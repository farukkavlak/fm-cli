using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// Where the tactic is read and written. Outside a match it is the team's (`Human.Team.Tactic`
	/// beside a `Boolean` set True); during one it is the match's own (`MatchUI….Tactic`, a
	/// MatchTacticReference with the same properties), which the game applies at the next stoppage
	/// and which leaves the team's tactic as it was.
	/// </summary>
	private static string? matchTactic;

	private static string TacticAt(string scope) => matchTactic != null ? $"{scope}.tac" : $"{scope}.team.Tactic";

	private static string Stoppage() => matchTactic != null ? L("   (in a match: applied at the first dead ball; the team's saved tactic does not change)") : "";

	private static (string To, string From, string Number)[] TacticVariables() =>
		matchTactic != null ? [("tac", matchTactic, "")] : [("Boolean", "const.True", ""), ("team", "Human.Team", "")];

	/// <summary>During a match, finds the match's tactic; false when there is a match but no tactic of it to be found.</summary>
	private static async Task<bool> FindMatchTactic()
	{
		matchTactic = null;
		if (Field(await Fm.GetJson("state"), "DuringMatch") != "True") return true;
		var found = await Fm.Get("find", ("name", "Tactic"), ("limit", "40"));
		matchTactic = found.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("..MatchUI", StringComparison.Ordinal) && l.EndsWith(".Tactic", StringComparison.Ordinal))
			.Select(l => l[2..]).FirstOrDefault();
		return matchTactic != null;
	}

	private static int tacticScopes;

	/// <summary>A fresh place beside which the team's tactic answers for one instruction.</summary>
	private static async Task<string> TacticScope(int instruction)
	{
		var scope = $"fmcli.ti{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
		foreach (var (to, from, number) in (IEnumerable<(string, string, string)>)[.. TacticVariables(), ("TeamInstructionID", "", instruction.ToString())])
		{
			var set = from.Length > 0 ? await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("from", from)) : await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("int", number));
			if (set.StartsWith("error", StringComparison.Ordinal)) return null;
		}
		return scope;
	}

	/// <summary>The setting in use for each instruction, by number.</summary>
	private static async Task<Dictionary<int, long>> TeamInstructionsNow(List<TeamInstruction> instructions)
	{
		var scopes = new List<(int Id, string Scope)>();
		foreach (var i in instructions)
			if (await TacticScope(i.Id) is { } scope) scopes.Add((i.Id, scope));
		var read = await PullEach(scopes.Select(s => ($"{TacticAt(s.Scope)}", "SelectedTeamInstructionOption")).ToList());
		var now = new Dictionary<int, long>();
		foreach (var ((id, _), record) in scopes.Zip(read))
			if (long.TryParse(Field(record, "SelectedTeamInstructionOption"), out var option)) now[id] = option;
		return now;
	}

	/// <summary>
	/// Every instruction with its settings' names. They are the game's own and do not change,
	/// so they are asked once and kept (--refresh asks again).
	/// </summary>
	private static async Task<List<TeamInstruction>> TeamInstructionCatalogue(bool refresh)
	{
		var wanted = TeamInstructionGroups.SelectMany(g => g.Ids.Select(id => (id, g.Group, g.WithBall))).ToList();
		if (!refresh && File.Exists(TeamInstructionsFile))
		{
			try
			{
				var kept = JsonSerializer.Deserialize<List<KeptInstruction>>(File.ReadAllText(TeamInstructionsFile)) ?? [];
				if (kept.Count == wanted.Count)
					return wanted.Select(w => kept.First(k => k.Id == w.id))
						.Select(k => new TeamInstruction(k.Id, k.Name, wanted.First(w => w.id == k.Id).Group, wanted.First(w => w.id == k.Id).WithBall, k.Options.Select(o => (o.Id, o.Name)).ToList()))
						.ToList();
			}
			catch (Exception e) when (e is JsonException or InvalidOperationException or IOException) { }
		}

		if (!Ui.Json) Console.Error.WriteLine(L("Reading the team instructions from the game (once)…"));
		async Task<string> Static(string tag, long id)
		{
			var scope = $"fmcli.{tag}{id}";
			await Fm.Get("pathset", ("to", $"{scope}.ID"), ("int", id.ToString()));
			await Fm.Get("pathset", ("to", $"{scope}.data"), ("from", "data"));
			return scope;
		}

		var scopes = new List<string>();
		foreach (var (id, _, _) in wanted) scopes.Add(await Static("tic", id));
		var entries = await PullEach(scopes.Select(s => ($"{s}.data.TacticTeamInstruction", "Name,TeamInstructionOptionsList")).ToList());

		var optionIds = entries.SelectMany(e => Field(e, "TeamInstructionOptionsList").Split(',', StringSplitOptions.RemoveEmptyEntries)).Select(long.Parse).Distinct().ToList();
		var optionScopes = new List<string>();
		foreach (var id in optionIds) optionScopes.Add(await Static("tio", id));
		var optionNames = (await PullEach(optionScopes.Select(s => ($"{s}.data.TacticTeamInstructionOption", "Name")).ToList()))
			.Zip(optionIds).ToDictionary(p => p.Second, p => Field(p.First, "Name"));

		var catalogue = new List<TeamInstruction>();
		foreach (var ((id, group, withBall), entry) in wanted.Zip(entries))
		{
			var name = Field(entry, "Name");
			var options = Field(entry, "TeamInstructionOptionsList").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(long.Parse)
				.Select(o => (o, optionNames.GetValueOrDefault(o, ""))).ToList();
			if (name.Length == 0 || options.Count == 0 || options.Any(o => o.Item2.Length == 0)) return [];
			catalogue.Add(new TeamInstruction(id, name, group, withBall, options));
		}

		Directory.CreateDirectory(DiffFolder);
		File.WriteAllText(TeamInstructionsFile, JsonSerializer.Serialize(catalogue.Select(i => new KeptInstruction(i.Id, i.Name, i.Options.Select(o => new KeptOption(o.Id, o.Name)).ToList()))));
		return catalogue;
	}

	/// <summary>
	/// One position's player instructions with the ball and without it, read and changed with no
	/// card opened. A group (`data.TacticPlayerInstructionGroup` beside a
	/// `PlayerInstructionGroupID`, 0–10 with the ball, 11–14 without) names its settings by number;
	/// the tactic gives the slot's (`TeamSelectionIndex`, `InPossession`) setting and whether the
	/// role fixes it, and takes a new one written there.
}
