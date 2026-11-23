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
	// Names shown of a long finding at a terminal.
	private const int DoctorShown = 3;

	private static async Task<int> Doctor(bool json, string[] args)
	{
		if (args.Contains("--install"))
		{
			if (!json && !await PlaceBundledBepInEx()) return 1;
			if (!json) await EnsureNlModel();
			return InstallDoctor(json);
		}

		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}

		var (injured, tired, rusty, expiring, thin, worries) = await SquadCheck();
		static int Number(JsonElement p, string f) => int.TryParse(Field(p, f), out var n) ? n : -1;

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
		// At a terminal a long list is its first few and how many more; scripts, the MCP and --all get all of it.
		var shortened = Ui.Rich && !args.Contains("--all");
		List<string> Few(IEnumerable<string> items, int count)
		{
			var all = items.ToList();
			if (!shortened || all.Count <= DoctorShown + 1) return all;
			return [.. all.Take(DoctorShown), L($"… and {count - DoctorShown} more")];
		}

		Finding(L($"Tired (condition under 85%): {tired.Count}"), Wrapped(Few(tired.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Condition"))}"), tired.Count)), L("intensity <player> \"rest 1\""));
		Finding(L($"Low match sharpness (under 60%): {rusty.Count}"), Wrapped(Few(rusty.Select(p => $"{Field(p, "Name")} {GameNumbers.Percent(Number(p, "Sharpness"))}"), rusty.Count)), "squad --fitness");
		Finding(L($"Contract ends within a year: {expiring.Count}"), Few(expiring.Select(x => $"{Field(x.Player, "Name")} — {Field(x.Player, "FullContract.EndDateDescription")}"), expiring.Count), L("contract <player>"));
		Finding(L("Thin positions (one fit player or none)"), thin.Select(x => $"{x.Slot,-4} {(x.Players.Count == 0 ? L("nobody") : L($"only {x.Players[0]}"))}").ToList(), "depth");
		Finding(L($"The board's concerns: {worries.Count}"), Few(worries, worries.Count), "board");

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

		if (shortened && new[] { tired.Count, rusty.Count, expiring.Count, worries.Count }.Any(n => n > DoctorShown + 1))
			Ui.Line(L("[dim]Every name: doctor --all[/]"));
		if (problems == 0) Console.WriteLine(L("Nothing in the squad stands out as a problem."));
		if (Ui.Rich) Ui.Line(L("[dim]Happiness and tactical familiarity are separate: happiness, tactic[/]"));
		else Console.WriteLine(L("Happiness and tactical familiarity are separate: happiness, tactic"));
		return 0;
	}

	private sealed record SquadChecked(
		List<JsonElement> Injured, List<JsonElement> Tired, List<JsonElement> Rusty,
		List<(JsonElement Player, DateTime? Ends)> Expiring, List<(string Slot, List<string> Players)> Thin, List<string> Worries);

	/// <summary>What `doctor` finds, read from the game's data with no screen opened; `todo` reads the same.</summary>
	private static async Task<SquadChecked> SquadCheck()
	{
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

		return new SquadChecked(injured, tired, rusty, expiring, thin, worries);
	}
}
