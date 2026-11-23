namespace FmCli;

/// <summary>
/// Transfer targets in two steps. Bare, it says what there is to spend and where the squad is
/// thin; which position to look at is the manager's to say. With a position, it searches the
/// player database for that position within the transfer budget — the same search as
/// `players --position … --max-value …`, whose other options it passes on.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> PositionTurkish = new()
	{
		["kaleci"] = "GK", ["sol bek"] = "DL", ["sag bek"] = "DR", ["stoper"] = "DC", ["sol kanat bek"] = "WBL", ["sag kanat bek"] = "WBR",
		["on libero"] = "DM", ["defansif orta saha"] = "DM", ["orta saha"] = "MC", ["sol orta saha"] = "ML", ["sag orta saha"] = "MR",
		["on numara"] = "AMC", ["ofansif orta saha"] = "AMC", ["sol kanat"] = "AML", ["sag kanat"] = "AMR", ["forvet"] = "ST", ["santrfor"] = "ST",
		["goalkeeper"] = "GK", ["left back"] = "DL", ["right back"] = "DR", ["centre back"] = "DC", ["center back"] = "DC",
		["left wing back"] = "WBL", ["right wing back"] = "WBR", ["defensive midfielder"] = "DM", ["central midfielder"] = "MC",
		["left midfielder"] = "ML", ["right midfielder"] = "MR", ["attacking midfielder"] = "AMC", ["left winger"] = "AML",
		["right winger"] = "AMR", ["striker"] = "ST",
	};

	private static async Task<int> Targets(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage(Screen.RecruitmentBudgets);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		if (await BudgetsLeft() is not { } budgets)
		{
			Console.Error.WriteLine(L("The budget page could not be read."));
			return 1;
		}
		var (transfer, wage) = budgets;

		var words = Words(args).ToList();
		await EngineTexts.Positions(Fm);
		if (words.Count == 0) return await ThinPositions(json, transfer, wage);

		var typed = string.Join(" ", words);
		// The game's own abbreviation first ("KL" in a Turkish game), then the code, then a Turkish name.
		var slot = PositionTyped(typed) ?? PositionTurkish.GetValueOrDefault(Fold(typed), typed);
		// As the game writes it beside a player ("OS (M)"): its words are learned from the squad.
		if (!PositionNames.ContainsKey(new string(slot.Where(char.IsLetter).ToArray())) && typed.Contains('(')
		    && await PositionsFromShown(typed) is [var single])
			slot = single;
		if (!PositionNames.ContainsKey(new string(slot.Where(char.IsLetter).ToArray())))
		{
			Console.Error.WriteLine(L($"There is no position called '{typed}'. Positions: {string.Join(" ", DepthRows.SelectMany(r => r).Select(PositionShown))}  (or: {string.Join(", ", PositionTurkish.Keys)})"));
			return 64;
		}

		// Everything else typed is the search's own: --age, --grade, --listed, --attr…
		var passed = args.Skip(1).Where(a => !words.Contains(a)).ToList();
		var limited = passed.Contains("--max-value");
		if (!limited && Money(transfer) is not > 0)
		{
			Console.Error.WriteLine(L($"The transfer budget could not be read ({transfer}); give the limit yourself: targets {typed} --max-value <money>"));
			return 1;
		}
		if (!json)
		{
			Console.WriteLine(L($"{PositionShown(slot.ToUpperInvariant())} — value {(limited ? Option(args, "--max-value") : transfer)} and under{(limited ? "" : L(" (your transfer budget; to change it --max-value <money>)"))}"));
			Console.WriteLine(L($"Wage budget (weekly): {wage}"));
		}
		var found = await Players(json, ["players", "--position", slot, .. (limited ? [] : new[] { "--max-value", transfer }), .. passed]);
		if (found == 0 && !json) Ui.Hint(L("Narrow it: --age 18-23 · --grade B · --listed · --loan-listed · --attr \"pace>=14\"   (the search stays open in the game: players --clear)"));
		return found;
	}

	/// <summary>What there is to spend, and the positions with one fit player or none.</summary>
	private const int LeavingFlag = 14;

	private static async Task<int> ThinPositions(bool json, string transfer, string wage)
	{
		var (team, _) = await Fm.OwnTeam();
		var squad = (await Fm.Pull("Human.Team.Players", "Name,PositionCombined,IsInjured,LoanClub.Name", list: true)).EnumerateArray()
			.Where(p => Field(p, "Name").Length > 0)
			// Players out on loan are not here to cover; one loaned to us has us as his loan club.
			.Where(p => Field(p, "LoanClub.Name") is not { Length: > 0 } club || club == team)
			.Select(p => (Name: Field(p, "Name"), Injured: Field(p, "IsInjured") == "True", Slots: Positions(Field(p, "PositionCombined"))))
			.ToList();
		// A player whose move is agreed (the game's status 14: leaving for another club on a date) does not cover a
		// position next season: Marin, the only goalkeeper, read as cover a week before he left (22 Jun 2034).
		var leaving = (await SquadFlags()).Where(f => f.Value.Any(x => x.Id == LeavingFlag)).Select(f => f.Key).ToHashSet();
		squad = squad.Where(p => !leaving.Contains(p.Name)).ToList();
		var depth = DepthRows.SelectMany(r => r)
			.Select(slot => (Slot: slot, Fit: squad.Where(p => p.Slots.Contains(slot) && !p.Injured).Select(p => p.Name).ToList(), Injured: squad.Where(p => p.Slots.Contains(slot) && p.Injured).Select(p => p.Name).ToList()))
			.ToList();
		var thin = depth.Where(d => d.Fit.Count <= 1).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				TransferBudget = transfer,
				WageBudget = wage,
				Thin = thin.Select(d => new { d.Slot, d.Fit, d.Injured }),
				Leaving = leaving,
				Depth = depth.ToDictionary(d => d.Slot, d => d.Fit.Count),
			}));
			return 0;
		}

		Console.WriteLine(L($"Budget: transfers {transfer} · wages (weekly) {wage}"));
		if (leaving.Count > 0) Console.WriteLine(L($"Leaving (their move is agreed; not counted): {string.Join(", ", leaving.Order())}"));
		Console.WriteLine(thin.Count == 0 ? L("\nNo thin positions in the squad: every position has at least two fit players.") : L("\nThin positions in the squad (one fit player or none):"));
		foreach (var (slot, fit, injured) in thin)
		{
			var who = fit.Count == 0 ? L("nobody") : L($"only {fit[0]}");
			var out_ = injured.Count > 0 ? L($"  (injured: {string.Join(", ", injured)})") : "";
			if (Ui.Rich) Ui.Line($"  [bold]{Ui.Safe(PositionShown(slot)),-4}[/] [red]{Ui.Safe(who)}[/][dim]{Ui.Safe(out_)}[/]");
			else Console.WriteLine($"  {PositionShown(slot),-4} {who}{out_}");
		}
		Ui.Hint(L($"\nOther positions: {string.Join(" · ", depth.Where(d => d.Fit.Count > 1).Select(d => $"{PositionShown(d.Slot)} {d.Fit.Count}"))}"));
		Console.WriteLine(L($"\nTo search: targets <position>   (e.g. targets {PositionShown("DL")}, targets \"left back\")"));
		return 0;
	}
}
