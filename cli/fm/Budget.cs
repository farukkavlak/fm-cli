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
		await Until(async () => (after = await BudgetOverview()) != null && after.Any(p => p.Value.StartsWith(Short(wanted), StringComparison.Ordinal)), seconds: 6, every: 0.5);
		foreach (var (name, value) in after ?? overview) Console.WriteLine($"{name}: {value}");
		return 0;

		// "£7,383,138" is "£7.38M" on the overview; the first digits are enough to know it.
		static string Short(string full) => full.Length > 3 ? full[..3] : full;
	}

	/// <summary>The overview's figures: transfer budget left and spent, wage budget left and spent, scouting.</summary>
	private static async Task<List<KeyValuePair<string, string>>> BudgetOverview()
	{
		List<string> transfer = [], wage = [];
		await Until(async () =>
		{
			transfer = (await Fm.GetJson("texts", ("anchor", "Transfer Budget"), ("up", "8"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			wage = (await Fm.GetJson("texts", ("anchor", "Wage Budget"), ("up", "8"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			return transfer.Contains("Current") && wage.Contains("Available");
		}, seconds: 8, every: 0.5);
		if (!transfer.Contains("Current")) return null;

		string After(List<string> list, string label) => list.IndexOf(label) is var i && i >= 0 && i + 1 < list.Count ? list[i + 1] : "-";
		var scouting = (await Fm.GetJson("texts", ("anchor", "Scouting"), ("up", "6"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
		return
		[
			new("Transfer bütçesi", $"{After(transfer, "Current")} (bu sezon harcanan {After(transfer, "Spent this season")}, başta {After(transfer, "Original Budget")})"),
			// The game writes what is over the budget in brackets: "(£1K) p/w".
			new("Maaş bütçesi", $"{(After(wage, "Available") is var free && free.StartsWith('(') ? $"{free.Replace("(", "").Replace(")", "")} aşılmış" : $"{free} boşta")} · {wage.FirstOrDefault(t => t.StartsWith("Currently spending", StringComparison.Ordinal))?.Replace("Currently spending", "ödenen") ?? "-"}"),
			new("Scouting bütçesi", After(scouting, "Scouting")),
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
