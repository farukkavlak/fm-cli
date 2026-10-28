using System.Globalization;
using System.Text.Json;

namespace FmCli;

/// <summary>
/// What is wrong with the squad, in one look and with no screen opened: who is injured and
/// when he is back, who is tired or short of match fitness, whose contract runs out within a
/// year, which positions have one fit player or none, and what the board is worried about. Each
/// finding names the command that goes into it.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Doctor(bool json, string[] args)
	{
		if (args.Contains("--install"))
		{
			if (!json) await EnsureNlModel();
			return InstallDoctor(json);
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var (team, _) = await Fm.OwnTeam();
		var everyone = (await Fm.Pull("Human.Team.Players",
			"Name,Age,PositionCombined,Condition,Sharpness,IsInjured,CurrentInjury.InjuryDescription,CurrentInjury.InjuryReturnDays,"
			+ "FullContract.EndDate,FullContract.EndDateDescription,LoanClub.Name", list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0).ToList();
		// Players out on loan are someone else's to look after; one loaned to us has us as his loan club.
		var squad = everyone.Where(p => Field(p, "LoanClub.Name") is not { Length: > 0 } club || club == team).ToList();
		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var worries = await Fm.Strings("Human.Team.TeamConfidence.BoardFeedbackCriticism");

		static int Number(JsonElement p, string f) => int.TryParse(Field(p, f), out var n) ? n : -1;
		bool Injured(JsonElement p) => Field(p, "IsInjured") == "True";

		var injured = squad.Where(Injured).OrderByDescending(p => Number(p, "CurrentInjury.InjuryReturnDays")).ToList();
		var tired = squad.Where(p => !Injured(p) && Number(p, "Condition") is >= 0 and < 85).OrderBy(p => Number(p, "Condition")).ToList();
		var rusty = squad.Where(p => !Injured(p) && Number(p, "Sharpness") is >= 0 and < 60).OrderBy(p => Number(p, "Sharpness")).ToList();
		var expiring = squad
			.Select(p => (Player: p, Ends: GameDay(Field(p, "FullContract.EndDate"))))
			.Where(x => x.Ends is { } end && end < today.AddYears(1))
			.OrderBy(x => x.Ends)
			.ToList();

		// Depth counts the fit: an injured player does not cover his position this week.
		var fit = squad.Where(p => !Injured(p)).Select(p => (Name: Field(p, "Name"), Slots: Positions(Field(p, "PositionCombined")))).ToList();
		var thin = DepthRows.SelectMany(r => r)
			.Select(slot => (Slot: slot, Players: fit.Where(p => p.Slots.Contains(slot)).Select(p => p.Name).ToList()))
			.Where(x => x.Players.Count <= 1)
			.ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Injured = injured.Select(p => new { Name = Field(p, "Name"), Injury = Field(p, "CurrentInjury.InjuryDescription"), DaysOut = Number(p, "CurrentInjury.InjuryReturnDays") }),
				Tired = tired.Select(p => new { Name = Field(p, "Name"), Condition = Number(p, "Condition") }),
				LowSharpness = rusty.Select(p => new { Name = Field(p, "Name"), Sharpness = Number(p, "Sharpness") }),
				ContractsEndingWithinAYear = expiring.Select(x => new { Name = Field(x.Player, "Name"), Ends = Field(x.Player, "FullContract.EndDateDescription") }),
				ThinPositions = thin.Select(x => new { x.Slot, x.Players }),
				BoardWorries = worries,
			}));
			return 0;
		}

		var problems = 0;
		void Finding(string title, List<string> lines, string next)
		{
			if (lines.Count == 0) return;
			problems++;
			if (Ui.Rich) Ui.Line($"[bold yellow]![/] [bold]{Ui.Safe(title)}[/]  [dim]→ {Ui.Safe(next)}[/]");
			else Console.WriteLine($"! {title}  → {next}");
			foreach (var line in lines) Console.WriteLine($"    {line}");
		}

		Finding(L($"Injured: {injured.Count}"), injured.Select(p =>
			$"{Field(p, "Name")} — {Field(p, "CurrentInjury.InjuryDescription")}{(Number(p, "CurrentInjury.InjuryReturnDays") is > 0 and var days ? L($", {days} more days") : "")}").ToList(),
			"health");
		Finding(L($"Tired (condition under 85%): {tired.Count}"), Wrapped(tired.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Condition"))}")), L("intensity <player> \"rest 1\""));
		Finding(L($"Low match sharpness (under 60%): {rusty.Count}"), Wrapped(rusty.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Sharpness"))}")), "squad --fitness");
		Finding(L($"Contract ends within a year: {expiring.Count}"), expiring.Select(x => $"{Field(x.Player, "Name")} — {Field(x.Player, "FullContract.EndDateDescription")}").ToList(), L("contract <player>"));
		Finding(L("Thin positions (one fit player or none)"), thin.Select(x => $"{x.Slot,-4} {(x.Players.Count == 0 ? L("nobody") : L($"only {x.Players[0]}"))}").ToList(), "depth");
		Finding(L($"The board's concerns: {worries.Count}"), worries.Take(3).Concat(worries.Count > 3 ? [L($"… and {worries.Count - 3} more")] : []).ToList(), "board");

		// A list of names, comma after comma, broken into lines that fit the terminal under the indent.
		static List<string> Wrapped(IEnumerable<string> items)
		{
			var room = Math.Max(40, Ui.Out.Profile.Width - 6);
			var lines = new List<string>();
			var line = "";
			foreach (var item in items)
			{
				if (line.Length > 0 && line.Length + item.Length + 2 > room)
				{
					lines.Add(line + ",");
					line = "";
				}
				line = line.Length == 0 ? item : $"{line}, {item}";
			}
			if (line.Length > 0) lines.Add(line);
			return lines;
		}

		if (problems == 0) Console.WriteLine(L("Nothing in the squad stands out as a problem."));
		if (Ui.Rich) Ui.Line(L("[dim]Happiness and tactical familiarity are separate: happiness, tactic[/]"));
		else Console.WriteLine(L("Happiness and tactical familiarity are separate: happiness, tactic"));
		return 0;
	}
}
