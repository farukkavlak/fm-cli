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
		(L("Genel"), true, [1, 21, 4, 3, 5, 7, 6]),
		(L("Son üçte bir"), true, [19, 18, 27, 26]),
		(L("İlerleme"), true, [28, 30, 20]),
		(L("Oyun kurma"), true, [24, 29, 17, 16]),
		(L("Topsuz"), false, [8, 9, 15, 10, 25, 11, 12, 22, 23]),
	];

	private static async Task<int> TacticInstructions(List<string> words, bool oop, bool dry, bool refresh)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}
		if (!await FindMatchTactic())
		{
			Console.Error.WriteLine(L("Maçın taktiği bulunamadı."));
			return 1;
		}
		var catalogue = await TeamInstructionCatalogue(refresh);
		if (catalogue.Count == 0)
		{
			Console.Error.WriteLine(L("Takım talimatları oyundan okunamadı."));
			return 1;
		}
		var now = await TeamInstructionsNow(catalogue);
		if (now.Count == 0)
		{
			Console.Error.WriteLine(L("Takımın taktiği okunamadı. Taktik yoksa: tactic new"));
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
				if (Ui.Rich) Ui.Line($"[bold underline]{(group.Key ? L("Topla") : L("Topsuz"))}[/]"); else Console.WriteLine(group.Key ? L("TOPLA") : L("TOPSUZ"));
				foreach (var phase in group.GroupBy(i => i.Group))
				{
					if (group.Key) { if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(phase.Key)}[/]"); else Console.WriteLine(phase.Key); }
					foreach (var i in phase)
						if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(i.Name),-30}[/] [yellow]{Ui.Safe(Setting(i))}[/]");
						else Console.WriteLine($"  {i.Name}: {Setting(i)}");
				}
			}
			Console.WriteLine(L("\nDeğiştirmek için: tactic instructions \"<talimat>\" [<değer>]"));
			return 0;
		}

		// "Final Third/Dribbling" was how the screen's two Dribbling cells were told apart; it is one setting.
		var asked = words[0];
		if (asked.IndexOf('/') is var slash and > 0) asked = asked[(slash + 1)..];
		var names = catalogue.Select(i => i.Name).ToList();
		if (Choose(names, asked.Trim(), L("Talimat")) is not { } named) return 1;
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
			if (Ui.Ask(target.Name, choices, current: choices.IndexOf(current), cancel: L("Vazgeç")) is not { } picked) return 0;
			choice = choices[picked];
		}
		else if (Ui.Json)
		{
			Console.WriteLine(JsonOut.Of(new { Instruction = target.Name, Value = current, Choices = choices }));
			return 0;
		}
		else
		{
			Console.WriteLine(L($"{target.Name}: {current}\nSeçenekler: {string.Join(" · ", choices)}"));
			return 0;
		}

		if (dry || choice == current)
		{
			Console.WriteLine(dry ? L($"(kuru) {target.Name}: {current} → {choice}") : L($"{target.Name} zaten {choice}."));
			return 0;
		}

		var option = target.Options.First(o => o.Name == choice).Id;
		var scope = await TacticScope(target.Id);
		if (scope == null) return Report(L("error: taktik bağlanamadı"));
		// Written before it is bound, the setting stays a value of that place and never reaches the game.
		await PullEach([($"{TacticAt(scope)}", "SelectedTeamInstructionOption")]);
		var said = await Fm.Get("pathset", ("to", $"{TacticAt(scope)}.SelectedTeamInstructionOption"), ("int", option.ToString()));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Read back from a place of its own: the one written to answers with what was written.
		long? after = null;
		var took = await Until(async () => (after = (await TeamInstructionsNow([target])).GetValueOrDefault(target.Id)) == option, seconds: 5, every: 0.3);
		if (Ui.Json) Console.WriteLine(JsonOut.Of(new { Instruction = target.Name, Was = current, Value = took ? choice : Setting(target), Changed = took }));
		else Console.WriteLine(took ? $"{target.Name}: {current} → {choice}{Stoppage()}" : L($"{target.Name}: oyun değişikliği almadı."));
		return took ? 0 : 1;
	}
}
