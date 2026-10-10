using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// The club's finances (FINDINGS §8.78): the balance tile on Club › Overview opens the finances
/// card — this month's income and expenditure, financial fair play, debts, the wage bill by player
/// status, sponsorship, the board's projections and the balance month by month.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> PlayerStatusTurkish = new()
	{
		["Star Player"] = "Yıldız", ["Important Player"] = "Önemli", ["Regular Starter"] = "Düzenli ilk 11",
		["Squad Player"] = "Rotasyon", ["Impact Sub"] = "Etkili yedek", ["Fringe Player"] = "Kenarda",
		["Emergency Backup"] = "Acil durum yedeği", ["Youngster"] = "Genç", ["Not Needed"] = "İhtiyaç yok",
	};

	private static async Task<int> Finances(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Overview", "Club");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("text", "Overall Balance"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		List<string> t = [];
		await Until(async () =>
		{
			t = (await Fm.GetJson("texts", ("anchor", "Remaining Debt"), ("up", "16"))).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
			return t.Contains("Projections") && t.Contains("Total Sponsorships income");
		}, seconds: 10, every: 0.5);
		if (!t.Contains("Projections"))
		{
			Console.Error.WriteLine("Finans kartı okunamadı.");
			return 1;
		}

		string After(string label, int skip = 1, int from = 0) =>
			t.IndexOf(label, from) is var i && i >= 0 && i + skip < t.Count ? t[i + skip] : "-";
		string Before(string label) => t.IndexOf(label) is var i && i > 0 ? t[i - 1] : "-";

		// "count | status | wage" per player status, after the wage tile's caption.
		var statuses = new List<(string Status, int Players, string Wage)>();
		for (var i = Math.Max(0, t.IndexOf("Player types and their total basic wage:")); i + 2 < t.Count && t[i] != "Sponsorship"; i++)
			if (int.TryParse(t[i], out var n) && PlayerStatusTurkish.ContainsKey(t[i + 1]))
			{
				statuses.Add((t[i + 1], n, t[i + 2]));
				i += 2;
			}

		var projections = new List<(string Season, string Balance, string Turnover)>();
		for (var i = t.IndexOf("Projections"); i >= 0 && (i = t.IndexOf("Season", i + 1)) >= 0 && i + 5 < t.Count && t[i + 2] == "Balance";)
			projections.Add((t[i + 1], t[i + 3], t[i + 5]));

		var months = new List<(string Month, string Balance)>();
		var summary = t.IndexOf("Month");
		for (var i = summary + 2; summary >= 0 && i + 1 < t.Count && Regex.IsMatch(t[i], @"^\d+/\d+/\d{4}$"); i += 2)
			months.Add((t[i], t[i + 1]));

		// Spending is shown under "Expenditure" further down than income, so it is looked for after it.
		var income = After("Income");
		var spent = After("Expenditure");
		(string Competition, string Status) ffp = t.IndexOf("FFP") is var f && f >= 0 && f + 2 < t.Count ? (t[f + 1], t[f + 2]) : ("-", "-");

		var finances = new
		{
			Balance = Before("Overall Balance"),
			IncomeThisMonth = income, ExpenditureThisMonth = spent,
			Ffp = new { ffp.Competition, ffp.Status },
			Debt = new { Remaining = Before("Remaining Debt"), Original = Before("Original Debt") },
			Wages = new { Total = After("Wages"), Budget = After("Wages", 2).TrimStart('/', ' ').Replace(" p/w", ""), Share = After("Wages", 3) },
			WagesByStatus = statuses.Select(s => new { s.Status, s.Players, s.Wage }),
			Sponsorship = new { Total = After("Total Sponsorships income"), Sources = After("Sources") },
			Projections = projections.Select(p => new { p.Season, p.Balance, p.Turnover }),
			Months = months.Select(m => new { m.Month, m.Balance }),
		};
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(finances));
			return 0;
		}

		void Heading(string title)
		{
			if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
			else Console.WriteLine($"\n{title}");
		}
		// The game writes a loss in brackets: "(£34.96M)".
		void Row(string label, string value)
		{
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(label),-22} {(value.StartsWith('(') ? $"[red]{Ui.Safe(value)}[/]" : Ui.Safe(value))}");
			else Console.WriteLine($"  {label,-22} {value}");
		}

		Heading("Kasa");
		Row("Bakiye", finances.Balance);
		Row("Bu ay gelir", income);
		Row("Bu ay gider", spent);
		foreach (var (month, balance) in months) Row($"{month} bakiyesi", balance);
		Row("Borç", $"{finances.Debt.Remaining} kaldı (başta {finances.Debt.Original})");
		Row("Finansal fair play", $"{(ffp.Status == "Compliant" ? "uyumlu" : ffp.Status)} ({ffp.Competition})");

		Heading("Maaş bordrosu");
		Row("Haftalık toplam", $"{finances.Wages.Total} / {finances.Wages.Budget} ({finances.Wages.Share})");
		foreach (var (status, players, wage) in statuses.Where(s => s.Players > 0))
			Row($"{PlayerStatusTurkish[status]} ({players})", $"{wage} haftalık");

		Heading("Sponsorluk");
		Row("Toplam gelir", $"{finances.Sponsorship.Total} ({finances.Sponsorship.Sources} kaynak)");

		Heading("Yönetimin projeksiyonu");
		foreach (var (season, balance, turnover) in projections) Row(season, $"bakiye {balance} · ciro {turnover}");
		return 0;
	}
}
