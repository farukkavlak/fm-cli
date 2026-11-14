namespace FmCli;

/// <summary>
/// A Dugout card for one player ("Tell Salifou: Shoot Less Often") changes his player instruction
/// in the match tactic (FM.UI.PlayerInstruction PI_SHOOT_LESS, group Shooting), not a tweak. The
/// instructions of everyone on the pitch are read before the card is taken and after, and the one
/// that moved is said (T157). Held by the game's numbers: the slot's index in the team selection,
/// the group's id and the setting's id; the card's sentence is never read for it.
/// </summary>
internal static partial class Program
{
	/// <summary>One player's one instruction group, bound beside the match tactic, read again as often as wanted.</summary>
	private sealed record PiWatch(string Index, string Player, PlayerInstructionGroup Group, string Scope);

	/// <summary>
	/// Binds every instruction group of every player on the pitch, in a few batches (one at a time
	/// each would wait a frame of its own: seconds before the card could be taken). Empty outside a
	/// match, or when the game answers no batch.
	/// </summary>
	private static async Task<List<PiWatch>> WatchPlayerInstructions()
	{
		if (matchTactic == null) return [];
		var groups = await PlayerInstructionCatalogue();
		if (groups.Count == 0) return [];

		// A substitute keeps his own index (11 on) and takes the position: the bench is asked too, only those with a position count.
		var stamp = $"{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
		var slotScopes = Enumerable.Range(0, 30).Select(i => (Index: i.ToString(), Scope: $"fmcli.dw{stamp}s{i}")).ToList();
		if (await Fm.Batch(slotScopes.SelectMany(s => new[]
			{
				("pathset", new[] { ("to", $"{s.Scope}.tac"), ("from", matchTactic) }),
				("pathset", new[] { ("to", $"{s.Scope}.TeamSelectionIndex"), ("int", s.Index) }),
				("pathset", new[] { ("to", $"{s.Scope}.InPossession"), ("from", "const.True") }),
			}).ToList()) == null) return [];
		var read = await PullEach(slotScopes.Select(s => ($"{TacticAt(s.Scope)}", "IPPosition,PlayerFromTeamSelectionIndex.Name")).ToList());
		var onPitch = slotScopes.Zip(read)
			.Where(p => Field(p.Second, "IPPosition") is { Length: > 0 } mask && mask != "0")
			.Select(p => (p.First.Index, Player: Field(p.Second, "PlayerFromTeamSelectionIndex.Name"))).ToList();
		if (onPitch.Count == 0) return [];

		var watches = onPitch.SelectMany(s => groups.Select(g => new PiWatch(s.Index, s.Player, g, $"fmcli.dw{stamp}p{s.Index}g{g.Id}"))).ToList();
		if (await Fm.Batch(watches.SelectMany(w => new[]
			{
				("pathset", new[] { ("to", $"{w.Scope}.tac"), ("from", matchTactic) }),
				("pathset", new[] { ("to", $"{w.Scope}.TeamSelectionIndex"), ("int", w.Index) }),
				("pathset", new[] { ("to", $"{w.Scope}.PlayerInstructionGroupID"), ("int", w.Group.Id.ToString()) }),
				("pathset", new[] { ("to", $"{w.Scope}.InPossession"), ("from", w.Group.WithBall ? "const.True" : "const.False") }),
			}).ToList()) == null) return [];
		return watches;
	}

	/// <summary>Each watched group's setting now, by its scope; a group that does not read is left out.</summary>
	private static async Task<Dictionary<string, long>> PlayerInstructionsOf(List<PiWatch> watches)
	{
		if (watches.Count == 0) return [];
		var read = await PullEach(watches.Select(w => ($"{TacticAt(w.Scope)}", "SelectedPlayerInstruction")).ToList());
		var now = new Dictionary<string, long>();
		foreach (var (watch, record) in watches.Zip(read))
			if (long.TryParse(Field(record, "SelectedPlayerInstruction"), out var option)) now[watch.Scope] = option;
		return now;
	}

	/// <summary>The settings that moved, as "Player · Instruction: was → now" in the game's words.</summary>
	private static async Task<List<string>> PlayerInstructionChanges(List<PiWatch> watches, Dictionary<string, long> before, List<object> changes)
	{
		var after = await PlayerInstructionsOf(watches);
		var moved = watches.Where(w => before.TryGetValue(w.Scope, out var was) && after.TryGetValue(w.Scope, out var now) && was != now).ToList();
		if (moved.Count == 0) return [];
		await GameWords.Ask(Fm, [.. moved.Select(w => w.Group.Name), .. moved.SelectMany(w => w.Group.Options.Select(o => o.Name))]);
		string Named(PlayerInstructionGroup g, long value) => GameWords.Of(g.Options.FirstOrDefault(o => o.Id == value).Name ?? value.ToString());
		foreach (var w in moved)
			changes.Add(new { Kind = "player", Id = w.Group.Id.ToString(), Name = w.Group.Name, w.Player, From = before[w.Scope].ToString(), To = after[w.Scope].ToString() });
		return moved.Select(w => $"{w.Player} · {GameWords.Of(w.Group.Name)}: {Named(w.Group, before[w.Scope])} → {Named(w.Group, after[w.Scope])}").ToList();
	}
}
