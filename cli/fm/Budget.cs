using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club's budgets (FINDINGS §8.65): Recruitment › Recruitment Budgets shows the transfer,
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
			Console.Error.WriteLine("Bütçe sayfası okunamadı.");
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
			Console.WriteLine("\nTransfer ile maaş bütçesi arasında para kaydırmak: budget ayarla");
			return 0;
		}

		// The card offers the slider's steps; every one is read by standing on it.
		var clicked = await Fm.Get("click", ("text", "Adjust Transfer and Wage Budget"));
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
			Console.Error.WriteLine("Bütçe ayarı kartı açılmadı.");
			return 1;
		}

		var pointer = Field(slider, "Pointer");
		var start = (int)double.Parse(Field(slider, "Value"), System.Globalization.CultureInfo.InvariantCulture);
		var high = (int)double.Parse(Field(slider, "High"), System.Globalization.CultureInfo.InvariantCulture);
		var board = (await BudgetCard()).Select(i => Field(i, "Text")).FirstOrDefault(t => t.StartsWith("The board", StringComparison.Ordinal));

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

		var labels = steps.Select(s => $"Transfer {s.Transfer.TrimEnd('*'),-14} · Maaş {s.Wage.TrimEnd('*')}").ToList();
		if (board != null) Console.WriteLine(board);

		int? target = null;
		if (words.Count > 1)
		{
			if (int.TryParse(words[1], out var n) && n >= 0 && n <= high) target = n;
			else
			{
				Console.Error.WriteLine($"'{words[1]}' bir adım değil (0–{high}).");
				await Fm.Get("click", ("text", "Cancel"));
				return 1;
			}
		}
		else if (Ui.Interactive) target = Ui.Ask("Hangi dağılım?", labels, start);
		else
		{
			for (var i = 0; i <= high; i++) Console.WriteLine($"  {i,2}  {labels[i]}{(i == start ? "  ← şu an" : "")}");
			Console.WriteLine("\nSeçmek: budget ayarla <adım>");
		}

		if (target is not { } chosen || chosen == start)
		{
			await Fm.Get("click", ("text", "Cancel"));
			if (target == start) Console.WriteLine("Bütçe zaten böyle.");
			return 0;
		}

		await Fm.Get("setcontrol", ("ptr", pointer), ("value", chosen.ToString()));
		var kept = await Fm.Get("click", ("text", "Got It"));
		if (kept.StartsWith("error", StringComparison.Ordinal)) return Report(kept);

		// The page's own figures say whether the board took it.
		var wanted = steps[chosen].Transfer.TrimEnd('*');
		var after = overview;
		// The club's transfer budget is the step's figure once the board has taken it.
		await Until(async () => Money(wanted) is { } amount && long.TryParse(Field(await Fm.Pull("Human.Club", "TransferBudget"), "TransferBudget"), out var now)
		                        && Math.Abs(now - amount) <= Math.Max(1000, amount / 100), seconds: 6, every: 0.5);
		after = await BudgetOverview();
		foreach (var (name, value) in after ?? overview) Console.WriteLine($"{name}: {value}");
		return 0;
	}

	/// <summary>
	/// The overview's figures: the transfer budget left (the club's data) with what was spent and the
	/// budget it started from (the page, which has them only as words: the figure after its label);
	/// the wage budget by the week from the club's data; the scouting budget's figure.
	/// </summary>
	private static async Task<List<KeyValuePair<string, string>>> BudgetOverview()
	{
		List<string> transfer = [];
		await Until(async () =>
		{
			transfer = (await Fm.GetJson("texts", ("anchor", "Transfer Budget"), ("up", "8"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			return transfer.Any(MoneyFormat.IsMoney);
		}, seconds: 8, every: 0.5);
		if (!transfer.Any(MoneyFormat.IsMoney)) return null;

		// The figure after a label, its case as the translation has it ("Original budget").
		string After(List<string> list, string label) =>
			list.FindIndex(t => t.Equals(label, StringComparison.OrdinalIgnoreCase)) is var i && i >= 0 && i + 1 < list.Count && MoneyFormat.IsMoney(list[i + 1]) ? list[i + 1] : "-";
		var club = await Fm.Pull("Human.Club", "TransferBudget,WageBudget,RemainingWageBudget,CurrentWageSpending");
		string Amount(string field) => long.TryParse(Field(club, field), out var v) ? MoneyFormat.Exact(v) : "-";
		var left = long.TryParse(Field(club, "TransferBudget"), out var t) ? MoneyFormat.Short(t) : After(transfer, "Current");
		var free = long.TryParse(Field(club, "RemainingWageBudget"), out var r) ? r : (long?)null;
		var scouting = (await Fm.GetJson("texts", ("anchor", "Scouting"), ("up", "6"))).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
		var scoutAt = scouting.IndexOf("Scouting");
		return
		[
			new("Transfer bütçesi", $"{left} (bu sezon harcanan {After(transfer, "Spent this season")}, başta {After(transfer, "Original Budget")})"),
			new("Maaş bütçesi", $"{(free is < 0 ? $"{MoneyFormat.Exact(-free.Value)} aşılmış" : free is { } f ? $"{MoneyFormat.Exact(f)} boşta" : "-")} (haftalık) · ödenen {Amount("CurrentWageSpending")} / {Amount("WageBudget")}"),
			new("Scouting bütçesi", scouting.Skip(scoutAt + 1).FirstOrDefault(MoneyFormat.IsMoney) ?? "-"),
		];
	}

	private static async Task<List<JsonElement>> BudgetCard() =>
		(await Fm.GetJson("form", ("anchor", "Budget Adjustment"), ("up", "6"))).EnumerateArray().ToList();

	/// <summary>The wage and transfer budgets the card shows for where the slider stands.</summary>
	private static (string Wage, string Transfer) BudgetFigures(List<JsonElement> card)
	{
		var texts = card.Where(i => Field(i, "Kind") == "text").Select(i => Field(i, "Text")).ToList();
		var w = texts.IndexOf("Wage Budget");
		var t = texts.IndexOf("Transfer Budget");
		return w >= 0 && t >= 0 && w + 1 < texts.Count && t + 1 < texts.Count ? (texts[w + 1], texts[t + 1]) : (null, null);
	}
}
