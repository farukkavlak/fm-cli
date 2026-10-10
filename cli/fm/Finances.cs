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

	private static async Task<int> Finances(bool json, string[] args)
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

		var words = Words(args).ToList();
		if (words.Count > 0)
		{
			var kind = Fold(words[0]) switch { "gelir" or "income" => "Income", "gider" or "expenditure" or "harcama" => "Expenditure", _ => null };
			if (kind == null)
			{
				Console.Error.WriteLine($"'{words[0]}' anlaşılmadı: finances gelir|gider [ay|geçen ay|sezon|geçen sezon]");
				return 1;
			}
			return await FinanceBreakdown(json, kind, string.Join(" ", words.Skip(1)));
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

		var sponsors = await Sponsors();
		// Ticket prices are not on the card; the club's own data has them, in pounds.
		var tickets = await Fm.Pull("Human.Team.Club", "MatchTicketPrice,SeasonTicketPrice,SeasonTicketHolders");

		var finances = new
		{
			Balance = Before("Overall Balance"),
			IncomeThisMonth = income, ExpenditureThisMonth = spent,
			Ffp = new { ffp.Competition, ffp.Status },
			Debt = new { Remaining = Before("Remaining Debt"), Original = Before("Original Debt") },
			Wages = new { Total = After("Wages"), Budget = After("Wages", 2).TrimStart('/', ' ').Replace(" p/w", ""), Share = After("Wages", 3) },
			WagesByStatus = statuses.Select(s => new { s.Status, s.Players, s.Wage }),
			Sponsorship = new
			{
				Total = After("Total Sponsorships income"), Sources = After("Sources"),
				Deals = sponsors.Select(d => new { Type = d[0], PerSeason = d[1], Until = d[2], Duration = d[3], TotalValue = d[4] }),
			},
			Tickets = new { Match = Pounds(Field(tickets, "MatchTicketPrice")), Season = Pounds(Field(tickets, "SeasonTicketPrice")), Holders = Field(tickets, "SeasonTicketHolders") },
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

		Heading("Bilet");
		Row("Maç bileti", finances.Tickets.Match);
		Row("Kombine", int.TryParse(finances.Tickets.Holders, out var holders) ? $"{finances.Tickets.Season} · {holders:N0} kombine sahibi" : finances.Tickets.Season);

		Heading("Sponsorluk");
		Row("Toplam gelir", $"{finances.Sponsorship.Total} ({finances.Sponsorship.Sources} kaynak)");
		foreach (var d in sponsors)
		{
			var type = Regex.IsMatch(d[0], @"additional club partner") ? "Diğer ortaklar" : d[0];
			var term = d[2] == "-" ? "" : $" · bitiş {d[2]} ({Regex.Replace(d[3], @"Years?", "yıl")})";
			Row(Ui.Fit(type, 22), $"{d[1]} sezonluk{term}");
		}

		Heading("Yönetimin projeksiyonu");
		foreach (var (season, balance, turnover) in projections) Row(season, $"bakiye {balance} · ciro {turnover}");
		if (Ui.Rich) Ui.Line("\n[dim]Kalem kalem: finances gelir|gider [ay|geçen ay|sezon|geçen sezon][/]");
		else Console.WriteLine("\nKalem kalem: finances gelir|gider [ay|geçen ay|sezon|geçen sezon]");
		return 0;
	}

	private static string Pounds(string raw) =>
		double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			? $"£{Math.Round(v):N0}" : "-";

	/// <summary>
	/// The sponsorship card, opened from the finances card's sponsorship tile: type, value per season,
	/// until, duration, total value. The finances card is left up behind it.
	/// </summary>
	private static async Task<List<string[]>> Sponsors()
	{
		var deals = new List<string[]>();
		if ((await Fm.Get("click", ("text", "Total Sponsorships income"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal)) return deals;
		List<string> t = [];
		await Until(async () =>
		{
			t = (await Fm.GetJson("texts", ("anchor", "Value (Per Season)"), ("up", "14"))).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
			return t.Contains("Total Value") && t.Count > t.IndexOf("Total Value") + 5;
		}, seconds: 8, every: 0.5);
		for (var i = t.IndexOf("Total Value") + 1; i > 0 && i + 4 < t.Count; i += 5) deals.Add(t.GetRange(i, 5).ToArray());
		return deals;
	}

	/// <summary>
	/// Income or expenditure item by item (FINDINGS §8.79): the finances card's turnover tile opens the
	/// income breakdown and its "This Month" expenditure tile the spending one; four period tabs
	/// (ThisMonthTab…LastSeasonTab) and an item list that lays out only the rows in view.
	/// </summary>
	private static async Task<int> FinanceBreakdown(bool json, string kind, string period)
	{
		var (tab, label) = Fold(period) switch
		{
			"" or "ay" or "bu ay" or "month" => ("This Month", "bu ay"),
			"gecen ay" or "last month" => ("Last Month", "geçen ay"),
			"sezon" or "bu sezon" or "season" => ("This Season", "bu sezon"),
			"gecen sezon" or "last season" => ("Last Season", "geçen sezon"),
			_ => (null, null),
		};
		if (tab == null)
		{
			Console.Error.WriteLine($"'{period}' bir dönem değil: ay, geçen ay, sezon, geçen sezon");
			return 1;
		}

		var tile = kind == "Income" ? "Turnover" : "This Month";
		var clicked = await Fm.Get("click", ("text", tile), ("name", "layout-tile-interaction-button"));
		if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);

		var heading = $"{kind} Breakdown";
		async Task<List<string>> Card() => (await Fm.GetJson("texts", ("anchor", heading), ("up", "6"))).EnumerateArray().Select(x => x.GetString() ?? "").ToList();
		static string TotalOf(List<string> c) => c.IndexOf("Total") is var i && i >= 0 && i + 1 < c.Count ? c[i + 1] : null;
		static List<(string Item, string Amount)> Items(List<string> c)
		{
			var items = new List<(string, string)>();
			for (var i = c.IndexOf("Item") + 2; i > 1 && i + 1 < c.Count; i += 2) items.Add((c[i], c[i + 1]));
			return items;
		}

		List<string> card = [];
		await Until(async () => Items(card = await Card()).Count > 0, seconds: 8, every: 0.5);
		if (Items(card).Count == 0)
		{
			Console.Error.WriteLine(kind == "Income" ? "Gelir dökümü açılmadı." : "Gider dökümü açılmadı.");
			return 1;
		}
		// The card comes back on whichever period it was last left on, so the tab is always pressed.
		// The figures follow it a moment later; the card may already be on it, or two periods may add
		// up the same, so a total that does not move is only waited on briefly.
		var before = TotalOf(card);
		await Fm.Get("click", ("text", tab), ("name", tab.Replace(" ", "") + "Tab"));
		await Until(async () => TotalOf(card = await Card()) != before, seconds: 1.5, every: 0.3);

		// The list is read top to bottom; any item in view anchors the scroll.
		var all = new List<(string Item, string Amount)>();
		foreach (var at in new[] { "0", "0.5", "1" })
		{
			var shown = Items(await Card());
			if (shown.Count == 0) break;
			await Fm.Get("scroll", ("anchor", shown[0].Item), ("to", at));
			await Task.Delay(400);
			foreach (var item in Items(card = await Card()))
				if (!all.Any(a => a.Item == item.Item)) all.Add(item);
		}
		var total = TotalOf(card);
		var turnover = card.IndexOf("Turnover") is var k && k >= 0 && k + 1 < card.Count ? card[k + 1] : null;

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(new { Kind = kind, Period = tab, Total = total, Turnover = turnover, Items = all.Select(a => new { a.Item, a.Amount }) }));
			return 0;
		}
		var title = $"{(kind == "Income" ? "Gelir" : "Gider")} — {label}: {total}" + (turnover != null && turnover != total ? $" (ciro {turnover})" : "");
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(title)}[/]");
		else Console.WriteLine(title);
		var width = all.Select(a => a.Item.Length).DefaultIfEmpty(10).Max();
		foreach (var (item, amount) in all.OrderByDescending(a => Money(a.Amount)))
		{
			if (Ui.Rich && Money(amount) == 0) Ui.Line($"  [dim]{Ui.Safe(item.PadRight(width))}  {Ui.Safe(amount)}[/]");
			else Console.WriteLine($"  {item.PadRight(width)}  {amount}");
		}
		return 0;

		static double Money(string text) => double.TryParse(Regex.Replace(text, @"[^\d.]", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
	}
}
