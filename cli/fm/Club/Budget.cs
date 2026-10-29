using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club's budgets: Recruitment › Recruitment Budgets (FM27 Club › Finances › Transfer Budget) shows the transfer,
/// wage and scouting budgets, and "Adjust Transfer and Wage Budget" opens a card whose slider
/// moves money between the first two. Each step's figures are read off the card as the slider
/// stands on it; "Got It" keeps the step, "Cancel" leaves it as it was.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Budget(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage(Screen.RecruitmentBudgets);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var overview = await BudgetOverview();
		if (overview == null)
		{
			Console.Error.WriteLine(L("The budget page could not be read."));
			return 1;
		}

		var words = Words(args).ToList();
		if (words.Count == 0 || Fold(words[0]) is not ("ayarla" or "adjust"))
		{
			if (json)
			{
				Console.WriteLine(JsonOut.Of(overview));
				return 0;
			}
			foreach (var (name, value) in overview) Console.WriteLine($"{name}: {value}");
			Console.WriteLine(L("\nTo move money between the transfer and wage budgets: budget adjust"));
			return 0;
		}

		// The card offers the slider's steps; every one is read by standing on it. The tile that
		// opens it is found by the action it stands for, not by its words: the label is the game's
		// own string, put into English by this language's table (§8.209).
		JsonElement? tile = null;
		await Until(async () => (tile = await SpeakToAction(BudgetAdjustAction)) != null, seconds: 8, every: 0.25);
		if (tile is not { } opening)
		{
			Console.Error.WriteLine(L("Could not find the tile that opens the budget card."));
			return 1;
		}
		var clicked = await Fm.Get("click", ("ptr", Field(opening, "Pointer")));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);
		JsonElement slider = default;
		await Until(async () =>
		{
			var form = await BudgetCard();
			slider = form.FirstOrDefault(i => Field(i, "Kind") == "slider");
			return slider.ValueKind == JsonValueKind.Object;
		}, seconds: 8, every: 0.5);
		if (slider.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine(L("The budget adjustment card did not open."));
			return 1;
		}

		var pointer = Field(slider, "Pointer");
		var start = (int)double.Parse(Field(slider, "Value"), System.Globalization.CultureInfo.InvariantCulture);
		var high = (int)double.Parse(Field(slider, "High"), System.Globalization.CultureInfo.InvariantCulture);
		// The board's word on it, a sentence in the game's language, by its data path.
		var board = (await BudgetCard()).Where(i => Field(i, "Path") == "budget.BudgetAdjustmentBoardString").Select(i => Field(i, "Text")).FirstOrDefault();

		var steps = new List<(string Wage, string Transfer)>();
		for (var step = 0; step <= high; step++)
		{
			await Fm.Get("setcontrol", ("ptr", pointer), ("value", step.ToString()));
			// The figures follow the slider a frame or two later; the last step's are still up meanwhile.
			var before = steps.Count > 0 ? steps[^1] : default;
			(string Wage, string Transfer) figures = default;
			// A step away from the one in force is marked with a star.
			await Until(async () => (figures = BudgetFigures(await BudgetCard())).Wage != null && figures != before
			                        && figures.Wage.EndsWith('*') == (step != start), seconds: 3, every: 0.2);
			steps.Add(figures);
		}
		await Fm.Get("setcontrol", ("ptr", pointer), ("value", start.ToString()));

		var labels = steps.Select(s => L($"Transfers {s.Transfer.TrimEnd('*'),-14} · Wages {s.Wage.TrimEnd('*')}")).ToList();
		if (board != null) Console.WriteLine(board);

		int? target = null;
		if (words.Count > 1)
		{
			if (int.TryParse(words[1], out var n) && n >= 0 && n <= high) target = n;
			else
			{
				Console.Error.WriteLine(L($"'{words[1]}' is not a step (0–{high})."));
				var left = await PressCardButton(keep: false);
				if (left.StartsWith("error", StringComparison.Ordinal)) Report(left);
				return 1;
			}
		}
		else if (Ui.Interactive) target = Ui.Ask(L("Which split?"), labels, start);
		else
		{
			for (var i = 0; i <= high; i++) Console.WriteLine($"  {i,2}  {labels[i]}{(i == start ? L("  ← now") : "")}");
			Console.WriteLine(L("\nTo choose: budget adjust <step>"));
		}

		if (target is not { } chosen || chosen == start)
		{
			var closed = await PressCardButton(keep: false);
			if (closed.StartsWith("error", StringComparison.Ordinal)) return Report(closed);
			if (target == start) Console.WriteLine(L("The budget is already like that."));
			return 0;
		}

		await Fm.Get("setcontrol", ("ptr", pointer), ("value", chosen.ToString()));
		var kept = await PressCardButton(keep: true);
		if (kept.StartsWith("error", StringComparison.Ordinal)) return Report(kept);

		// The page's own figures say whether the board took it.
		var wanted = steps[chosen].Transfer.TrimEnd('*');
		var after = overview;
		// The club's transfer budget is the step's figure once the board has taken it.
		await Until(async () => Money(wanted) is { } amount && long.TryParse(Field(await Fm.Pull("Human.Club", "TransferBudget"), "TransferBudget"), out var now)
		                        && Math.Abs(MoneyFormat.Shown(now) - amount) <= Math.Max(1000, amount / 100), seconds: 6, every: 0.5);
		after = await BudgetOverview();
		foreach (var (name, value) in after ?? overview) Console.WriteLine($"{name}: {value}");
		return 0;
	}

	/// <summary>
	/// The overview's figures, from the club's data: the transfer budget left, what was spent of it
	/// this season and the one it started from, the wage budget by the week, the scouting budget.
	/// </summary>
	private static async Task<List<KeyValuePair<string, string>>> BudgetOverview()
	{
		JsonElement club = default;
		await Until(async () => long.TryParse(Field(club = await Fm.Pull("Human.Club",
			"TransferBudget,TransferExpenditureThisSeason,OriginalTransferBudget,WageBudget,RemainingWageBudget,CurrentWageSpending"), "TransferBudget"), out _), seconds: 8, every: 0.5);
		if (!long.TryParse(Field(club, "TransferBudget"), out var left)) return null;

		string Amount(JsonElement from, string field, Func<long, string> format) => long.TryParse(Field(from, field), out var v) ? format(MoneyFormat.Shown(v)) : "-";
		var free = long.TryParse(Field(club, "RemainingWageBudget"), out var r) ? r : (long?)null;
		var scouting = await Fm.Pull("Human.ScoutingInfo", "ScoutingBudget");
		return
		[
			new(L("Transfer budget"), L($"{MoneyFormat.Short(MoneyFormat.Shown(left))} (spent this season {Amount(club, "TransferExpenditureThisSeason", MoneyFormat.Short)}, at the start {Amount(club, "OriginalTransferBudget", MoneyFormat.Short)})")),
			new(L("Wage budget"), L($"{(free is < 0 ? L($"{MoneyFormat.Exact(MoneyFormat.Shown(-free.Value))} over") : free is { } f ? L($"{MoneyFormat.Exact(MoneyFormat.Shown(f))} free") : "-")} (weekly) · paid {Amount(club, "CurrentWageSpending", MoneyFormat.Exact)} / {Amount(club, "WageBudget", MoneyFormat.Exact)}")),
			new(L("Scouting budget"), Amount(scouting, "ScoutingBudget", MoneyFormat.Short)),
		];
	}

	private static async Task<List<JsonElement>> BudgetCard() =>
		(await Fm.GetJson("form", ("anchor", "Budget Adjustment"), ("up", "6"), ("paths", "1"))).EnumerateArray().ToList();

	/// <summary>The wage and transfer budgets the card shows for where the slider stands.</summary>
	private static (string Wage, string Transfer) BudgetFigures(List<JsonElement> card)
	{
		var texts = card.Where(i => Field(i, "Kind") == "text").Select(i => Field(i, "Text")).ToList();
		var w = texts.IndexOf("Wage Budget");
		var t = texts.IndexOf("Transfer Budget");
		return w >= 0 && t >= 0 && w + 1 < texts.Count && t + 1 < texts.Count ? (texts[w + 1], texts[t + 1]) : (null, null);
	}

	/// <summary>The tile that opens the card, by the action it stands for
	/// (InteropEventIDs.ClubBudgetAdjustmentTransfersAndWages). The tiles beside it carry
	/// actions of their own (ClubBudgetAdjustmentScouting, 1332374646), so the action tells them
	/// apart where their words — the game's own strings — are only English when the table says so.</summary>
	private const string BudgetAdjustAction = "1885623898";

	/// <summary>What the card's answer that keeps the step sends (InteropEventIDs.EventFmxBudgetAdjustmentConfirm).</summary>
	private const string BudgetConfirmEvent = "EventFmxBudgetAdjustmentConfirm";

	/// <summary>
	/// The card's own two buttons: the answer that keeps the step sends the game's
	/// EventFmxBudgetAdjustmentConfirm, and the one that drops it is the other button of that same
	/// panel (the one that only closes it). Both labels ("Got It", "Cancel") are the game's own
	/// strings run through this language's table, so they are pressed by their pointer; when the
	/// card is not recognised the command says so rather than guessing at its words.
	/// </summary>
	private static async Task<string> PressCardButton(bool keep)
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var kept = buttons.FirstOrDefault(b => Field(b, "Events").Contains(BudgetConfirmEvent, StringComparison.Ordinal));
		if (kept!.ValueKind != JsonValueKind.Object)
			return "error: " + L(keep ? "Could not find the button that confirms the budget card." : "Could not find the button that closes the budget card.");
		if (keep) return await Fm.Get("click", ("ptr", Field(kept, "Pointer")));

		var leave = buttons.FirstOrDefault(b => Field(b, "Page") == Field(kept, "Page")
		                                        && Field(b, "Events").Contains("ClosePanel", StringComparison.Ordinal)
		                                        && !Field(b, "Events").Contains(BudgetConfirmEvent, StringComparison.Ordinal));
		return leave.ValueKind == JsonValueKind.Object
			? await Fm.Get("click", ("ptr", Field(leave, "Pointer")))
			: "error: " + L("Could not find the button that closes the budget card.");
	}
}
