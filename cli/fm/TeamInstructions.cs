using System.Text.Json;

namespace FmCli;

/// <summary>
/// The team instructions with the ball and without it, read and changed with no page opened
/// (FINDINGS §8.132). Each instruction is a number: `data.TacticTeamInstruction` answers for it
/// once an `ID` variable sits beside the static data, with its name and the numbers of its
/// settings, and `data.TacticTeamInstructionOption` names a setting the same way. The team's
/// tactic (`Human.Team.Tactic`, with a `Boolean` variable set True — False is another copy the
/// planner does not show) gives the setting in use as `SelectedTeamInstructionOption` beside a
/// `TeamInstructionID`, and writing a setting's number there changes it in the game.
/// </summary>
internal static partial class Program
{
	private sealed record TeamInstruction(int Id, string Name, string Group, bool WithBall, List<(long Id, string Name)> Options);

	private static string TeamInstructionsFile => CacheFile("team-instructions.json");

	/// <summary>
	/// Where each instruction is shown, in the game's order: the planner's four phases with the
	/// ball (Dribbling is one setting shown in two of them) and the instructions without it.
	/// 31, a second "Supporting Runs", is on no page.
	/// </summary>
	private static readonly (string Group, bool WithBall, int[] Ids)[] TeamInstructionGroups =
	[
		("Genel", true, [1, 21, 4, 3, 5, 7, 6]),
		("Son üçte bir", true, [19, 18, 27, 26]),
		("İlerleme", true, [28, 30, 20]),
		("Oyun kurma", true, [24, 29, 17, 16]),
		("Topsuz", false, [8, 9, 15, 10, 25, 11, 12, 22, 23]),
	];

	private static async Task<int> TacticInstructions(List<string> words, bool oop, bool dry, bool refresh)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}
		if (!await FindMatchTactic())
		{
			Console.Error.WriteLine("Maçın taktiği bulunamadı.");
			return 1;
		}
		var catalogue = await TeamInstructionCatalogue(refresh);
		if (catalogue.Count == 0)
		{
			Console.Error.WriteLine("Takım talimatları oyundan okunamadı.");
			return 1;
		}
		var now = await TeamInstructionsNow(catalogue);
		if (now.Count == 0)
		{
			Console.Error.WriteLine("Takımın taktiği okunamadı. Taktik yoksa: tactic new");
			return 1;
		}
		string Setting(TeamInstruction i) => now.TryGetValue(i.Id, out var o) ? i.Options.FirstOrDefault(x => x.Id == o).Name ?? o.ToString() : "?";

		if (words.Count == 0)
		{
			var shown = catalogue.Where(i => !oop || !i.WithBall).ToList();
			if (Ui.Json)
			{
				Console.WriteLine(JsonOut.Of(shown.Select(i => new { Phase = i.Group, WithBall = i.WithBall, Label = i.Name, Value = Setting(i), Choices = i.Options.Select(o => o.Name) })));
				return 0;
			}
			var first = true;
			foreach (var group in shown.GroupBy(i => i.WithBall))
			{
				if (!first) Console.WriteLine();
				first = false;
				if (Ui.Rich) Ui.Line($"[bold underline]{(group.Key ? "Topla" : "Topsuz")}[/]"); else Console.WriteLine(group.Key ? "TOPLA" : "TOPSUZ");
				foreach (var phase in group.GroupBy(i => i.Group))
				{
					if (group.Key) { if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(phase.Key)}[/]"); else Console.WriteLine(phase.Key); }
					foreach (var i in phase)
						if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(i.Name),-30}[/] [yellow]{Ui.Safe(Setting(i))}[/]");
						else Console.WriteLine($"  {i.Name}: {Setting(i)}");
				}
			}
			Console.WriteLine("\nDeğiştirmek için: tactic instructions \"<talimat>\" [<değer>]");
			return 0;
		}

		// "Final Third/Dribbling" was how the screen's two Dribbling cells were told apart; it is one setting.
		var asked = words[0];
		if (asked.IndexOf('/') is var slash and > 0) asked = asked[(slash + 1)..];
		var names = catalogue.Select(i => i.Name).ToList();
		if (Choose(names, asked.Trim(), "Talimat") is not { } named) return 1;
		var target = catalogue.First(i => i.Name == named);
		var current = Setting(target);
		var choices = target.Options.Select(o => o.Name).ToList();

		string choice;
		if (words.Count > 1)
		{
			if (Choose(choices, string.Join(" ", words.Skip(1)), target.Name) is not { } picked) return 1;
			choice = picked;
		}
		else if (Ui.Interactive && !dry)
		{
			if (Ui.Ask(target.Name, choices, current: choices.IndexOf(current), cancel: "Vazgeç") is not { } picked) return 0;
			choice = choices[picked];
		}
		else if (Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Instruction = target.Name, Value = current, Choices = choices }));
			return 0;
		}
		else
		{
			Console.WriteLine($"{target.Name}: {current}\nSeçenekler: {string.Join(" · ", choices)}");
			return 0;
		}

		if (dry || choice == current)
		{
			Console.WriteLine(dry ? $"(kuru) {target.Name}: {current} → {choice}" : $"{target.Name} zaten {choice}.");
			return 0;
		}

		var option = target.Options.First(o => o.Name == choice).Id;
		var scope = await TacticScope(target.Id);
		if (scope == null) return Report("error: taktik bağlanamadı");
		// Written before it is bound, the setting stays a value of that place and never reaches the game.
		await PullEach([($"{TacticAt(scope)}", "SelectedTeamInstructionOption")]);
		var said = await Fm.Get("pathset", ("to", $"{TacticAt(scope)}.SelectedTeamInstructionOption"), ("int", option.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Read back from a place of its own: the one written to answers with what was written.
		long? after = null;
		var took = await Until(async () => (after = (await TeamInstructionsNow([target])).GetValueOrDefault(target.Id)) == option, seconds: 5, every: 0.3);
		if (Ui.Json) Console.WriteLine(JsonOut.Of(new { Instruction = target.Name, Was = current, Value = took ? choice : Setting(target), Changed = took }));
		else Console.WriteLine(took ? $"{target.Name}: {current} → {choice}{Stoppage()}" : $"{target.Name}: oyun değişikliği almadı.");
		return took ? 0 : 1;
	}

	/// <summary>
	/// Where the tactic is read and written. Outside a match it is the team's (`Human.Team.Tactic`
	/// beside a `Boolean` set True); during one it is the match's own (`MatchUI….Tactic`, a
	/// MatchTacticReference with the same properties), which the game applies at the next stoppage
	/// and which leaves the team's tactic as it was (FINDINGS §8.137).
	/// </summary>
	private static string matchTactic;

	private static string TacticAt(string scope) => matchTactic != null ? $"{scope}.tac" : $"{scope}.team.Tactic";

	private static string Stoppage() => matchTactic != null ? "   (maçta: ilk ölü topta uygulanır; takımın kayıtlı taktiği değişmez)" : "";

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

		if (!Ui.Json) Console.Error.WriteLine("Takım talimatları oyundan okunuyor (bir kez)…");
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
	/// card opened (FINDINGS §8.133). A group (`data.TacticPlayerInstructionGroup` beside a
	/// `PlayerInstructionGroupID`, 0–10 with the ball, 11–14 without) names its settings by number;
	/// the tactic gives the slot's (`TeamSelectionIndex`, `InPossession`) setting and whether the
	/// role fixes it, and takes a new one written there.
	/// </summary>
	private static async Task<int> PlayerInstructions(List<string> words, bool dry)
	{
		if (!await FindMatchTactic())
		{
			Console.Error.WriteLine("Maçın taktiği bulunamadı.");
			return 1;
		}
		var slots = await PiSlots();
		if (slots.Count == 0)
		{
			Console.Error.WriteLine("Takımın taktiği yok. Oluşturmak için: tactic new");
			return 1;
		}
		var spots = slots.Select(s => s.Spot).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine($"kullanım: tactic pi <mevki|oyuncu> [<talimat> [<değer>]]   mevkiler: {string.Join(" ", slots.Select(s => $"{s.Spot} ({s.Player})"))}");
			return 64;
		}
		// A slot by its position as the pitch shows it, or by the player in it.
		var slot = slots.FirstOrDefault(s => Fold(s.Spot) == Fold(words[0]));
		if (slot == null)
		{
			var byName = Matching(slots, s => s.Player, words[0]);
			if (byName.Count != 1)
			{
				Console.Error.WriteLine($"'{words[0]}' bir mevkiye ya da ilk on birdeki tek bir oyuncuya uymuyor. Mevkiler: {string.Join(" ", slots.Select(s => $"{s.Spot} ({s.Player})"))}");
				return 1;
			}
			slot = byName[0];
		}
		var spot = slot.Spot;
		var index = slot.Index;

		var groups = await PlayerInstructionCatalogue();
		if (groups.Count == 0)
		{
			Console.Error.WriteLine("Oyuncu talimatları oyundan okunamadı.");
			return 1;
		}
		var now = await PlayerInstructionsNow(index, groups);
		if (now.Count == 0)
		{
			Console.Error.WriteLine($"{spot}: talimatlar okunamadı.");
			return 1;
		}
		string Setting(PlayerInstructionGroup g) => now.TryGetValue(g.Id, out var n) ? g.Options.FirstOrDefault(o => o.Id == n.Option).Name ?? n.Option.ToString() : "?";
		bool Fixed(PlayerInstructionGroup g) => now.TryGetValue(g.Id, out var n) && n.Locked;
		var roles = slot.Role.Length > 0 ? $"{slot.Player} · {slot.Role} / topsuz {slot.RoleWithoutBall}" : slot.Player;

		if (words.Count == 1)
		{
			if (Ui.Json)
			{
				Console.WriteLine(JsonOut.Of(new
				{
					Position = spot, slot.Player, slot.Role, slot.RoleWithoutBall,
					Instructions = groups.Select(g => new { Label = g.Name, WithBall = g.WithBall, Value = Setting(g), ByRole = Fixed(g), Choices = g.Options.Select(o => o.Name) }),
				}));
				return 0;
			}
			Console.WriteLine($"{spot} · {roles}");
			foreach (var side in groups.GroupBy(g => g.WithBall))
			{
				if (Ui.Rich) Ui.Line($"[bold]{(side.Key ? "Topla" : "Topsuz")}[/]"); else Console.WriteLine(side.Key ? "Topla" : "Topsuz");
				foreach (var g in side)
					if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(g.Name),-22}[/] [yellow]{Ui.Safe(Setting(g))}[/]{(Fixed(g) ? " [dim](rolden)[/]" : "")}");
					else Console.WriteLine($"  {g.Name}: {Setting(g)}{(Fixed(g) ? " (rolden)" : "")}");
			}
			Console.WriteLine($"\nDeğiştirmek: tactic pi {spot} \"<talimat>\" <değer>   (rolden gelenler rol değişince değişir)");
			return 0;
		}

		if (Choose(groups.Select(g => g.Name).ToList(), words[1], spot) is not { } named) return 1;
		var group = groups.First(g => g.Name == named);
		var name = $"{spot} · {group.Name}";
		var current = Setting(group);
		if (Fixed(group))
		{
			Console.Error.WriteLine($"{name}: {current} — rolün{(slot.Role.Length > 0 ? $" ({(group.WithBall ? slot.Role : slot.RoleWithoutBall)})" : "")} parçası, değiştirilemiyor. Rolü değiştirmek için: tactic role {spot}{(group.WithBall ? "" : " --oop")}");
			return 1;
		}
		// Some settings are not on offer to every role ("Cut Inside" for a centre-back), as on the card.
		var open = group.Options.Where(o => !now[group.Id].Disabled.Contains(o.Id)).ToList();
		var choices = open.Select(o => o.Name).ToList();

		string choice;
		if (words.Count > 2)
		{
			if (Choose(choices, string.Join(" ", words.Skip(2)), name) is not { } picked) return 1;
			choice = picked;
		}
		else if (Ui.Interactive && !dry)
		{
			if (Ui.Ask(name, choices, current: choices.IndexOf(current), cancel: "Vazgeç") is not { } picked) return 0;
			choice = choices[picked];
		}
		else if (Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Position = spot, Instruction = group.Name, Value = current, Choices = choices }));
			return 0;
		}
		else
		{
			Console.WriteLine($"{name}: {current}\nSeçenekler: {string.Join(" · ", choices)}");
			return 0;
		}

		if (dry || choice == current)
		{
			Console.WriteLine(dry ? $"(kuru) {name}: {current} → {choice}" : $"{name} zaten {choice}.");
			return 0;
		}

		var option = open.First(o => o.Name == choice).Id;
		var scope = await PlayerScope(index, group);
		if (scope == null) return Report("error: taktik bağlanamadı");
		await PullEach([($"{TacticAt(scope)}", "SelectedPlayerInstruction")]);
		var said = await Fm.Get("pathset", ("to", $"{TacticAt(scope)}.SelectedPlayerInstruction"), ("int", option.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		long? after = null;
		var took = await Until(async () => (after = (await PlayerInstructionsNow(index, [group])).GetValueOrDefault(group.Id)?.Option) == option, seconds: 5, every: 0.3);
		if (Ui.Json) Console.WriteLine(JsonOut.Of(new { Position = spot, Instruction = group.Name, Was = current, Value = took ? choice : current, Changed = took }));
		else Console.WriteLine(took ? $"{name}: {current} → {choice}{Stoppage()}" : $"{name}: oyun değişikliği almadı.");
		return took ? 0 : 1;
	}

	private sealed record PiSlot(string Spot, string Index, string Player, string Role, string RoleWithoutBall);

	/// <summary>
	/// The eleven slots with the player in each, read from the tactic with no page (FINDINGS §8.133),
	/// so they are found during a match too. The role names are only on the planner's pitch, so they
	/// are taken from it when it can be opened — not during a match.
	/// </summary>
	private static async Task<List<PiSlot>> PiSlots()
	{
		var scopes = new List<string>();
		for (var i = 0; i < 11; i++)
		{
			var scope = $"fmcli.sl{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
			foreach (var (to, from, number) in (IEnumerable<(string, string, string)>)[.. TacticVariables(), ("TeamSelectionIndex", "", i.ToString()), ("InPossession", "const.True", "")])
				if (from.Length > 0) await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("from", from)); else await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("int", number));
			scopes.Add(scope);
		}
		var read = await PullEach(scopes.Select(s => ($"{TacticAt(s)}", "IPPosition,PlayerFromTeamSelectionIndex.Name")).ToList());
		var slots = read.Select((r, i) => new PiSlot(Spot(Field(r, "IPPosition")), i.ToString(), Field(r, "PlayerFromTeamSelectionIndex.Name"), "", ""))
			.Where(s => Field(read[int.Parse(s.Index)], "IPPosition").Length > 0).ToList();
		if (slots.Count == 0 || Field(await Fm.GetJson("state"), "DuringMatch") == "True") return slots;

		if (await Planner() is { } tactic && tactic.TryGetProperty("Slots", out var planned))
			foreach (var p in planned.EnumerateArray())
			{
				var at = slots.FindIndex(s => s.Index == Field(p, "TeamSelectionIndex"));
				if (at >= 0) slots[at] = slots[at] with { Role = Role(Field(p, "IPRoleName")), RoleWithoutBall = Role(Field(p, "OOPRoleName")) };
			}
		return slots;
	}

	private sealed record PlayerInstructionGroup(int Id, string Name, bool WithBall, List<(long Id, string Name)> Options);

	private sealed record PlayerInstructionNow(long Option, bool Locked, HashSet<long> Disabled);

	/// <summary>The groups 0–10 are the ones with the ball, 11–14 (pressing, marking, tackling) without it.</summary>
	private const int PlayerInstructionGroups = 15, FirstWithoutBall = 11;

	private static string PlayerInstructionsFile => CacheFile("player-instructions.json");

	/// <summary>A fresh place beside which the tactic answers for one slot's one group.</summary>
	private static async Task<string> PlayerScope(string index, PlayerInstructionGroup group)
	{
		var scope = $"fmcli.pi{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
		foreach (var (to, from, number) in (IEnumerable<(string, string, string)>)
		[
			.. TacticVariables(), ("TeamSelectionIndex", "", index),
			("PlayerInstructionGroupID", "", group.Id.ToString()), ("InPossession", group.WithBall ? "const.True" : "const.False", ""),
		])
		{
			var set = from.Length > 0 ? await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("from", from)) : await Fm.Get("pathset", ("to", $"{scope}.{to}"), ("int", number));
			if (set.StartsWith("error", StringComparison.Ordinal)) return null;
		}
		return scope;
	}

	/// <summary>Each group's setting for one slot, whether the role fixes it, and the settings the role does not allow.</summary>
	private static async Task<Dictionary<int, PlayerInstructionNow>> PlayerInstructionsNow(string index, List<PlayerInstructionGroup> groups)
	{
		var scopes = new List<(PlayerInstructionGroup Group, string Scope)>();
		foreach (var g in groups)
			if (await PlayerScope(index, g) is { } scope) scopes.Add((g, scope));
		var read = await PullEach(scopes.Select(s => ($"{TacticAt(s.Scope)}", "SelectedPlayerInstruction,PlayerInstructionGroupIsLocked")).ToList());
		var now = new Dictionary<int, PlayerInstructionNow>();
		foreach (var ((group, scope), record) in scopes.Zip(read))
		{
			if (!long.TryParse(Field(record, "SelectedPlayerInstruction"), out var option)) continue;
			var disabled = new HashSet<long>();
			// Only a group that is to be changed needs its settings looked at.
			if (groups.Count == 1)
			{
				var rows = await Fm.Pull($"{TacticAt(scope)}.PlayerInstructionGroupOptions", "PropertyValue,Disabled", list: true);
				if (rows.ValueKind == JsonValueKind.Array)
					foreach (var row in rows.EnumerateArray())
						if (Field(row, "Disabled") == "True" && long.TryParse(Field(row, "PropertyValue"), out var off)) disabled.Add(off);
			}
			now[group.Id] = new PlayerInstructionNow(option, Field(record, "PlayerInstructionGroupIsLocked") == "True", disabled);
		}
		return now;
	}

	/// <summary>The fifteen groups with their settings' names, asked once and kept like the team's.</summary>
	private static async Task<List<PlayerInstructionGroup>> PlayerInstructionCatalogue()
	{
		if (File.Exists(PlayerInstructionsFile))
		{
			try
			{
				var kept = JsonSerializer.Deserialize<List<KeptInstruction>>(File.ReadAllText(PlayerInstructionsFile)) ?? [];
				if (kept.Count == PlayerInstructionGroups)
					return kept.Select(k => new PlayerInstructionGroup(k.Id, k.Name, k.Id < FirstWithoutBall, k.Options.Select(o => (o.Id, o.Name)).ToList())).ToList();
			}
			catch (Exception e) when (e is JsonException or InvalidOperationException or IOException) { }
		}

		if (!Ui.Json) Console.Error.WriteLine("Oyuncu talimatları oyundan okunuyor (bir kez)…");
		async Task<string> Static(string tag, int group, long? option)
		{
			var scope = $"fmcli.{tag}{group}{(option is { } o ? $"_{o}" : "")}";
			await Fm.Get("pathset", ("to", $"{scope}.PlayerInstructionGroupID"), ("int", group.ToString()));
			if (option is { } id) await Fm.Get("pathset", ("to", $"{scope}.playerinstructionoptionid"), ("int", id.ToString()));
			await Fm.Get("pathset", ("to", $"{scope}.data"), ("from", "data"));
			return scope;
		}

		var ids = Enumerable.Range(0, PlayerInstructionGroups).ToList();
		var scopes = new List<string>();
		foreach (var id in ids) scopes.Add(await Static("pic", id, null));
		var entries = await PullEach(scopes.Select(s => ($"{s}.data.TacticPlayerInstructionGroup", "Name,PlayerInstructionGroupOptionsInt")).ToList());

		// A setting is named beside its group: the number alone ("playerinstructionoptionid") does not say whose it is.
		var wanted = ids.Zip(entries).SelectMany(p => Field(p.Second, "PlayerInstructionGroupOptionsInt").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(o => (Group: p.First, Option: long.Parse(o)))).ToList();
		var optionScopes = new List<string>();
		foreach (var (group, option) in wanted) optionScopes.Add(await Static("pio", group, option));
		var names = (await PullEach(optionScopes.Select(s => ($"{s}.data.TacticPlayerInstructionOption", "Name")).ToList()))
			.Zip(wanted).ToDictionary(p => p.Second, p => Field(p.First, "Name"));

		var catalogue = new List<PlayerInstructionGroup>();
		foreach (var (id, entry) in ids.Zip(entries))
		{
			var options = wanted.Where(w => w.Group == id).Select(w => (w.Option, names[w])).ToList();
			if (Field(entry, "Name").Length == 0 || options.Count == 0 || options.Any(o => o.Item2.Length == 0)) return [];
			// The game lists "Standard" last; it reads better first.
			catalogue.Add(new PlayerInstructionGroup(id, Field(entry, "Name"), id < FirstWithoutBall, [.. options.Where(o => o.Option == 0), .. options.Where(o => o.Option != 0)]));
		}

		Directory.CreateDirectory(DiffFolder);
		File.WriteAllText(PlayerInstructionsFile, JsonSerializer.Serialize(catalogue.Select(g => new KeptInstruction(g.Id, g.Name, g.Options.Select(o => new KeptOption(o.Id, o.Name)).ToList()))));
		return catalogue;
	}

	private sealed record KeptOption(long Id, string Name);

	private sealed record KeptInstruction(int Id, string Name, List<KeptOption> Options);
}
