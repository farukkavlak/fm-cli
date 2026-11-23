namespace FmCli;

/// <summary>
/// `prep`: the next match on one screen, each part read as its own command reads it: the
/// opponent (`opponent`), our side and how well it knows the tactic (`tactic`), who is injured
/// and whether he is picked (`doctor`, `status`), and the preparation sessions up to the match
/// (`prep --sessions`, what `prep` was before).
/// </summary>
internal static partial class Program
{
	private static async Task<int> PrepScreen()
	{
		if (await DaysToNextMatch() is null)
		{
			Console.WriteLine(L("No match ahead. The preparation sessions: prep --sessions"));
			return 0;
		}

		PrepPart(L("Opponent"), first: true);
		await Opponent(false);

		PrepPart(L("Our side"));
		await Tactic(false, ["tactic", "--list"]);

		PrepPart(L("Injured"));
		var check = await SquadCheck();
		if (check.Injured.Count == 0) Console.WriteLine(L("Nobody is injured."));
		foreach (var p in check.Injured)
		{
			var days = int.TryParse(Field(p, "CurrentInjury.InjuryReturnDays"), out var d) && d > 0 ? L($", {d} more days") : "";
			Console.WriteLine($"  {Field(p, "Name")} — {Field(p, "CurrentInjury.InjuryDescription")}{days}");
		}
		if (check.Injured.Count > 0 && await InjuryNote() is { } note) Ui.Hint(note);

		PrepPart(L("Match preparation"));
		return await Prep(false, ["prep"]);
	}

	/// <summary>A part's heading: a line with its name on it at a terminal, as the inbox's days have.</summary>
	private static void PrepPart(string name, bool first = false)
	{
		if (!first) Console.WriteLine();
		if (Ui.Rich)
			Ui.Out.Write(new Spectre.Console.Rule($"[bold]{Ui.Safe(name)}[/]")
			{
				Justification = Spectre.Console.Justify.Left, Style = new Spectre.Console.Style(Spectre.Console.Color.Grey),
			});
		else Console.WriteLine($"== {name} ==");
	}
}
