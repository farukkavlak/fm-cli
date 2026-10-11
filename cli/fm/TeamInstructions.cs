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

	private static string TeamInstructionsFile => Path.Combine(DiffFolder, "team-instructions.json");

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
		await PullEach([($"{scope}.team.Tactic", "SelectedTeamInstructionOption")]);
		var said = await Fm.Get("pathset", ("to", $"{scope}.team.Tactic.SelectedTeamInstructionOption"), ("int", option.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Read back from a place of its own: the one written to answers with what was written.
		long? after = null;
		var took = await Until(async () => (after = (await TeamInstructionsNow([target])).GetValueOrDefault(target.Id)) == option, seconds: 5, every: 0.3);
		if (Ui.Json) Console.WriteLine(JsonOut.Of(new { Instruction = target.Name, Was = current, Value = took ? choice : Setting(target), Changed = took }));
		else Console.WriteLine(took ? $"{target.Name}: {current} → {choice}" : $"{target.Name}: oyun değişikliği almadı.");
		return took ? 0 : 1;
	}

	private static int tacticScopes;

	/// <summary>A fresh place beside which the team's tactic answers for one instruction.</summary>
	private static async Task<string> TacticScope(int instruction)
	{
		var scope = $"fmcli.ti{DateTime.UtcNow.Ticks % 100000000}x{Interlocked.Increment(ref tacticScopes)}";
		foreach (var (to, from, number) in new[] { ("Boolean", "const.True", ""), ("team", "Human.Team", ""), ("TeamInstructionID", "", instruction.ToString()) })
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
		var read = await PullEach(scopes.Select(s => ($"{s.Scope}.team.Tactic", "SelectedTeamInstructionOption")).ToList());
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

	private sealed record KeptOption(long Id, string Name);

	private sealed record KeptInstruction(int Id, string Name, List<KeptOption> Options);
}
