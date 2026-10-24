using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> PlayerInstructions(List<string> words, bool dry)
	{
		if (!await FindMatchTactic())
		{
			Console.Error.WriteLine(L("Maçın taktiği bulunamadı."));
			return 1;
		}
		var slots = await PiSlots();
		if (slots.Count == 0)
		{
			Console.Error.WriteLine(L("Takımın taktiği yok. Oluşturmak için: tactic new"));
			return 1;
		}
		var spots = slots.Select(s => s.Spot).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L($"kullanım: tactic pi <mevki|oyuncu> [<talimat> [<değer>]]   mevkiler: {string.Join(" ", slots.Select(s => $"{s.Spot} ({s.Player})"))}"));
			return 64;
		}
		// A slot by its position as the pitch shows it, or by the player in it.
		var slot = slots.FirstOrDefault(s => Fold(s.Spot) == Fold(words[0]));
		if (slot == null)
		{
			var byName = Matching(slots, s => s.Player, words[0]);
			if (byName.Count != 1)
			{
				Console.Error.WriteLine(L($"'{words[0]}' bir mevkiye ya da ilk on birdeki tek bir oyuncuya uymuyor. Mevkiler: {string.Join(" ", slots.Select(s => $"{s.Spot} ({s.Player})"))}"));
				return 1;
			}
			slot = byName[0];
		}
		var spot = slot.Spot;
		var index = slot.Index;

		var groups = await PlayerInstructionCatalogue();
		if (groups.Count == 0)
		{
			Console.Error.WriteLine(L("Oyuncu talimatları oyundan okunamadı."));
			return 1;
		}
		var now = await PlayerInstructionsNow(index, groups);
		if (now.Count == 0)
		{
			Console.Error.WriteLine(L($"{spot}: talimatlar okunamadı."));
			return 1;
		}
		string Setting(PlayerInstructionGroup g) => now.TryGetValue(g.Id, out var n) ? g.Options.FirstOrDefault(o => o.Id == n.Option).Name ?? n.Option.ToString() : "?";
		bool Fixed(PlayerInstructionGroup g) => now.TryGetValue(g.Id, out var n) && n.Locked;
		var roles = slot.Role.Length > 0 ? L($"{slot.Player} · {slot.Role} / topsuz {slot.RoleWithoutBall}") : slot.Player;

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
				if (Ui.Rich) Ui.Line($"[bold]{(side.Key ? L("Topla") : L("Topsuz"))}[/]"); else Console.WriteLine(side.Key ? L("Topla") : L("Topsuz"));
				foreach (var g in side)
					if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(g.Name),-22}[/] [yellow]{Ui.Safe(Setting(g))}[/]{(Fixed(g) ? L(" [dim](rolden)[/]") : "")}");
					else Console.WriteLine($"  {g.Name}: {Setting(g)}{(Fixed(g) ? L(" (rolden)") : "")}");
			}
			Console.WriteLine(L($"\nDeğiştirmek: tactic pi {spot} \"<talimat>\" <değer>   (rolden gelenler rol değişince değişir)"));
			return 0;
		}

		if (Choose(groups.Select(g => g.Name).ToList(), words[1], spot) is not { } named) return 1;
		var group = groups.First(g => g.Name == named);
		var name = $"{spot} · {group.Name}";
		var current = Setting(group);
		if (Fixed(group))
		{
			Console.Error.WriteLine(L($"{name}: {current} — rolün{(slot.Role.Length > 0 ? $" ({(group.WithBall ? slot.Role : slot.RoleWithoutBall)})" : "")} parçası, değiştirilemiyor. Rolü değiştirmek için: tactic role {spot}{(group.WithBall ? "" : " --oop")}"));
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
			if (Ui.Ask(name, choices, current: choices.IndexOf(current), cancel: L("Vazgeç")) is not { } picked) return 0;
			choice = choices[picked];
		}
		else if (Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Position = spot, Instruction = group.Name, Value = current, Choices = choices }));
			return 0;
		}
		else
		{
			Console.WriteLine(L($"{name}: {current}\nSeçenekler: {string.Join(" · ", choices)}"));
			return 0;
		}

		if (dry || choice == current)
		{
			Console.WriteLine(dry ? L($"(kuru) {name}: {current} → {choice}") : L($"{name} zaten {choice}."));
			return 0;
		}

		var option = open.First(o => o.Name == choice).Id;
		var scope = await PlayerScope(index, group);
		if (scope == null) return Report(L("error: taktik bağlanamadı"));
		await PullEach([($"{TacticAt(scope)}", "SelectedPlayerInstruction")]);
		var said = await Fm.Get("pathset", ("to", $"{TacticAt(scope)}.SelectedPlayerInstruction"), ("int", option.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		long? after = null;
		var took = await Until(async () => (after = (await PlayerInstructionsNow(index, [group])).GetValueOrDefault(group.Id)?.Option) == option, seconds: 5, every: 0.3);
		if (Ui.Json) Console.WriteLine(JsonOut.Of(new { Position = spot, Instruction = group.Name, Was = current, Value = took ? choice : current, Changed = took }));
		else Console.WriteLine(took ? $"{name}: {current} → {choice}{Stoppage()}" : L($"{name}: oyun değişikliği almadı."));
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
				if (at >= 0) slots[at] = slots[at] with { Role = RoleOf(p, withoutBall: false), RoleWithoutBall = RoleOf(p, withoutBall: true) };
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

		if (!Ui.Json) Console.Error.WriteLine(L("Oyuncu talimatları oyundan okunuyor (bir kez)…"));
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
