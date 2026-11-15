using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club's budgets: Club › Finances › Transfer Budget shows the transfer, wage and scouting
/// budgets, and its "Adjust Transfer Budget" tile opens a card whose slider moves money between
/// the first two. Each step's figures are read off the card by their data paths as the slider
/// stands on it; the confirm button keeps the step, the other one leaves it as it was.
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
		// The page draws its tiles again after a card closes, so a tile just found may be gone by the press.
		JsonElement? tile = null;
		var clicked = "";
		await Until(async () => (tile = await GenericAction(BudgetAdjustAction)) is { } t
		                        && !(clicked = await Fm.Get("click", ("ptr", Field(t, "Pointer")))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.25);
		if (tile == null)
		{
			Console.Error.WriteLine(L("Could not find the tile that opens the budget card."));
			return 1;
		}
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
		var card = await BudgetCard();
		var board = card.Where(i => Field(i, "Path") == "budget.BudgetAdjustmentBoardString").Select(i => Field(i, "Text")).FirstOrDefault();

		// A board that will not talk money yet (season expectations not agreed) shuts the slider;
		// its sentence says why.
		if (Field(slider, "Enabled") != "True" || high <= 0)
		{
			Console.Error.WriteLine(board is { Length: > 0 } ? board : L("The board does not allow the budgets to be adjusted now."));
			var shut = await PressCardButton(keep: false);
			if (shut.StartsWith("error", StringComparison.Ordinal)) Report(shut);
			return 1;
		}

		var steps = new List<(string Wage, string Transfer)>();
		var atStart = BudgetFigures(card);
		var shown = atStart;
		for (var step = 0; step <= high; step++)
		{
			await Fm.Get("setcontrol", ("ptr", pointer), ("value", step.ToString()));
			// The figures follow the slider a frame or two later; the last step's are still up
			// meanwhile. The step in force shows what the card opened with, any other something new.
			var before = shown;
			(string Wage, string Transfer) figures = default;
			await Until(async () => (figures = BudgetFigures(await BudgetCard())).Wage != null
			                        && (step == start ? figures == atStart : figures != before), seconds: 3, every: 0.2);
			steps.Add(figures);
			shown = figures;
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

		long.TryParse(Field(await Fm.Pull("Human.Club", "TransferBudget"), "TransferBudget"), out var budgetBefore);
		await Fm.Get("setcontrol", ("ptr", pointer), ("value", chosen.ToString()));
		var kept = await PressCardButton(keep: true);
		if (kept.StartsWith("error", StringComparison.Ordinal)) return Report(kept);

		// The club's own data says whether the board took it: another step moves the transfer budget.
		var taken = await Until(async () => long.TryParse(Field(await Fm.Pull("Human.Club", "TransferBudget"), "TransferBudget"), out var now)
		                                    && now != budgetBefore, seconds: 6, every: 0.5);
		var after = await BudgetOverview();
		foreach (var (name, value) in after ?? overview) Console.WriteLine($"{name}: {value}");
		if (!taken)
		{
			Console.Error.WriteLine(L("The game did not change the budgets."));
			return 1;
		}
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
		var scouting = await Fm.Pull("Human.ScoutingInfo", "ScoutingBudget");
		return
		[
			new(L("Transfer budget"), L($"{MoneyFormat.Short(MoneyFormat.Shown(left))} (spent this season {Amount(club, "TransferExpenditureThisSeason", MoneyFormat.Short)}, at the start {Amount(club, "OriginalTransferBudget", MoneyFormat.Short)})")),
			new(L("Wage budget"), L($"{WageLeft(club)} (weekly) · paid {Amount(club, "CurrentWageSpending", MoneyFormat.Exact)} / {Amount(club, "WageBudget", MoneyFormat.Exact)}")),
			new(L("Scouting budget"), Amount(scouting, "ScoutingBudget", MoneyFormat.Short)),
		];
	}

	/// <summary>The transfer budget left and the weekly wage budget free (or over), from the club's data.</summary>
	private static async Task<(string Transfer, string Wage)?> BudgetsLeft()
	{
		var club = await Fm.Pull("Human.Club", "TransferBudget,RemainingWageBudget");
		return long.TryParse(Field(club, "TransferBudget"), out var left) ? (MoneyFormat.Short(MoneyFormat.Shown(left)), WageLeft(club)) : null;
	}

	/// <summary>The weekly wage budget free, or how far over it the club is.</summary>
	private static string WageLeft(JsonElement club) =>
		long.TryParse(Field(club, "RemainingWageBudget"), out var free)
			? free < 0 ? L($"{MoneyFormat.Exact(MoneyFormat.Shown(-free))} over") : L($"{MoneyFormat.Exact(MoneyFormat.Shown(free))} free")
			: "-";

	/// <summary>The card, found by the figure it is bound to; climbing from it takes in the board's
	/// sentence, the slider and the figures (7 to 10 levels all hold the same card).</summary>
	private static async Task<List<JsonElement>> BudgetCard() =>
		(await Fm.GetJson("form", ("anchorpath", "budget.BudgetAdjustmentEditedTransferValue"), ("up", "8"), ("paths", "1"))).EnumerateArray().ToList();

	/// <summary>The wage and transfer budgets the card shows for where the slider stands, by their data paths.</summary>
	private static (string Wage, string Transfer) BudgetFigures(List<JsonElement> card)
	{
		string Of(string path) => card.Where(i => Field(i, "Path") == path).Select(i => Field(i, "Text")).FirstOrDefault(t => t.Length > 0);
		return (Of("budget.BudgetAdjustmentEditedOtherValue"), Of("budget.BudgetAdjustmentEditedTransferValue")) is ({ } w, { } t) ? (w, t) : (null, null);
	}

	/// <summary>The tile that opens the card, by the action it stands for (ActionID
	/// ClubBudgetAdjustmentTransfers). FM26's ClubBudgetAdjustmentTransfersAndWages (1885623898) is
	/// now an entry of the board requests menu beside it; the action tells them apart where their
	/// words, the game's own strings, are only English when the table says so.</summary>
	private const string BudgetAdjustAction = "894198379";

	/// <summary>What the card's answer that keeps the step sends (InteropEventIDs.EventFmxBudgetAdjustmentConfirm).</summary>
	private const string BudgetConfirmEvent = "EventFmxBudgetAdjustmentConfirm";

	/// <summary>
	/// The card's own two buttons: the answer that keeps the step sends the game's
	/// EventFmxBudgetAdjustmentConfirm, and the one that drops it is the other button of that same
	/// panel (the one that only closes it). Both labels ("Got It", "Cancel") are the game's own
	/// strings run through this language's table, so they are pressed by their pointer; when the
	/// card is not recognised the command says so rather than guessing at its words. They are pressed
	/// by their events (a click on them does nothing in FM27), and the card is read back as gone.
	/// </summary>
	private static async Task<string> PressCardButton(bool keep)
	{
		var buttons = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().ToList();
		var kept = buttons.FirstOrDefault(b => Field(b, "Events").Contains(BudgetConfirmEvent, StringComparison.Ordinal));
		if (kept!.ValueKind != JsonValueKind.Object)
			return "error: " + L(keep ? "Could not find the button that confirms the budget card." : "Could not find the button that closes the budget card.");
		// The confirm button is shut while the slider stands where it started; pressing a shut button locks the game.
		if (keep && Field(kept, "Enabled") != "True") return "error: " + L("The budget card cannot be confirmed now.");
		if (keep) return await PressAndClose(kept);

		var leave = buttons.FirstOrDefault(b => Field(b, "Page") == Field(kept, "Page") && Field(b, "Enabled") == "True"
		                                        && Field(b, "Events").Contains("ClosePanel", StringComparison.Ordinal)
		                                        && !Field(b, "Events").Contains(BudgetConfirmEvent, StringComparison.Ordinal));
		return leave.ValueKind == JsonValueKind.Object
			? await PressAndClose(leave)
			: "error: " + L("Could not find the button that closes the budget card.");
	}

	/// <summary>Presses one of the card's buttons and waits for the card to be gone: its confirm button no longer there.</summary>
	private static async Task<string> PressAndClose(JsonElement button)
	{
		var pressed = await Fm.Get("press", ("ptr", Field(button, "Pointer")));
		if (pressed.StartsWith("error", StringComparison.Ordinal)) return pressed;
		var gone = await Until(async () => !(await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
			.Any(b => Field(b, "Events").Contains(BudgetConfirmEvent, StringComparison.Ordinal)), seconds: 5, every: 0.2);
		return gone ? pressed : "error: " + L("The budget card did not close.");
	}
}
