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
	private static async Task<int> Finances(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var words = Words(args).ToList();
		if (words.Count > 0)
		{
			var kind = Fold(words[0]) switch { "gelir" or "income" => "Income", "gider" or "expenditure" or "harcama" => "Expenditure", _ => null };
			if (kind == null)
			{
				Console.Error.WriteLine(L($"'{words[0]}' anlaşılmadı: finances gelir|gider [ay|geçen ay|sezon|geçen sezon]"));
				return 1;
			}
			return await FinanceBreakdown(json, kind, string.Join(" ", words.Skip(1)));
		}

		// Everything is read from the club's data, with no screen opened (FINDINGS §8.183): the finances
		// card's tiles are bound to these same properties.
		var club = await Fm.Pull("Human.Club", "OverallBalance,TotalIncomeThisMonth,TotalExpenditureThisMonth,TotalDebt,OriginalLoanDebt,"
			+ "TotalSponsorshipIncomeThisSeason,FfpData.IsPassingFfp,FfpData.Competition.Name");
		if (Field(club, "OverallBalance") is not { Length: > 0 })
		{
			Console.Error.WriteLine(L("Finans kartı okunamadı."));
			return 1;
		}

		// Count and total basic wage per player status, in the period the game is set to show; the
		// last entry is the whole squad's, its count the others' sum.
		var types = (await Fm.Pull("Human.Club.WagesData.PlayerTypeWageData", "Name,Quantity,TotalBasicWage", list: true)).EnumerateArray()
			.Select(r => (Status: Field(r, "Name"), Players: int.TryParse(Field(r, "Quantity"), out var n) ? n : 0, Wage: Field(r, "TotalBasicWage")))
			.ToList();
		var statuses = types.Count > 1 && types[^1].Players == types.SkipLast(1).Sum(t => t.Players) ? types.SkipLast(1).ToList() : types;

		// The two seasons the card projects (the first is the one under way).
		var projections = new List<(string Season, string Balance, string Turnover)>();
		foreach (var end in new[] { "Two", "Three" })
		{
			var balance = await Fm.Pull($"Human.Club.ProjectionBalance.ProjectionEndOfSeason{end}", "ThisSeason,PropertyValue");
			var turnover = await Fm.Pull($"Human.Club.ProjectionTurnover.ProjectionEndOfSeason{end}", "PropertyValue");
			if (Field(balance, "ThisSeason") is { Length: > 0 } season)
				projections.Add((season, PoundsShort(Field(balance, "PropertyValue")), PoundsShort(Field(turnover, "PropertyValue"))));
		}

		// The balance at the start of each month, the latest three.
		var months = (await Fm.Pull("Human.Club.FinanceBalanceHistory", "GraphPointX,GraphPointY", list: true)).EnumerateArray()
			.Select(r => (Day: DateTime.TryParse(Field(r, "GraphPointX"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) ? d : (DateTime?)null, Balance: Field(r, "GraphPointY")))
			.Where(r => r.Day != null).OrderBy(r => r.Day).TakeLast(3)
			.Select(r => (Month: $"{Months[r.Day!.Value.Month - 1]} {r.Day!.Value.Year}", Balance: Pounds(r.Balance))).ToList();

		var income = PoundsShort(Field(club, "TotalIncomeThisMonth"));
		var spent = PoundsShort(Field(club, "TotalExpenditureThisMonth"));
		var passing = Field(club, "FfpData.IsPassingFfp");
		(string Competition, string Status) ffp = (Field(club, "FfpData.Competition.Name") is { Length: > 0 } c ? c : "-",
			passing == "True" ? "Compliant" : passing == "False" ? "Failing" : "-");

		var wages = await WageBill();
		var sponsors = await Sponsors();
		// Ticket prices are not on the card; the club's own data has them, in pounds.
		var tickets = await Fm.Pull("Human.Team.Club", "MatchTicketPrice,SeasonTicketPrice,SeasonTicketHolders");

		var finances = new
		{
			Balance = Pounds(Field(club, "OverallBalance")),
			IncomeThisMonth = income, ExpenditureThisMonth = spent,
			// The words are the ones the human lines print: the game's own for the status and the
			// deal type, the CLI's for the fair play verdict (which is derived here, not read).
			Ffp = new { ffp.Competition, Status = ffp.Status switch { "Compliant" => L("uyumlu"), "Failing" => L("uyumsuz"), _ => ffp.Status } },
			Debt = new { Remaining = Pounds(Field(club, "TotalDebt")), Original = Pounds(Field(club, "OriginalLoanDebt")) },
			Wages = new { wages.Total, wages.Budget, wages.Share },
			WagesByStatus = statuses.Select(s => new { Status = GameWords.Of(s.Status), s.Players, s.Wage }),
			Sponsorship = new
			{
				Total = Pounds(Field(club, "TotalSponsorshipIncomeThisSeason")), Sources = sponsors.Count.ToString(),
				Deals = sponsors.Select(d => new { Type = GameWords.Of(d[0]), PerSeason = d[1], Until = d[2], Duration = d[3], TotalValue = d[4] }),
			},
			Tickets = new { Match = Pounds(Field(tickets, "MatchTicketPrice")), Season = Pounds(Field(tickets, "SeasonTicketPrice")), Holders = Field(tickets, "SeasonTicketHolders") },
			Projections = projections.Select(p => new { p.Season, p.Balance, p.Turnover }),
			Months = months.Select(m => new { m.Month, m.Balance }),
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(finances));
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
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(label),-26} {(value.StartsWith('(') ? $"[red]{Ui.Safe(value)}[/]" : Ui.Safe(value))}");
			else Console.WriteLine($"  {label,-26} {value}");
		}

		Heading("Kasa");
		Row(L("Bakiye"), finances.Balance);
		Row(L("Bu ay gelir"), income);
		Row(L("Bu ay gider"), spent);
		foreach (var (month, balance) in months) Row(L($"{month} bakiyesi"), balance);
		Row(L("Borç"), L($"{finances.Debt.Remaining} kaldı (başta {finances.Debt.Original})"));
		Row(L("Finansal fair play"), $"{(ffp.Status switch { "Compliant" => L("uyumlu"), "Failing" => L("uyumsuz"), _ => ffp.Status })} ({ffp.Competition})");

		Heading(L("Maaş bordrosu"));
		Row(L("Haftalık toplam"), $"{finances.Wages.Total} / {finances.Wages.Budget} ({finances.Wages.Share})");
		foreach (var (status, players, wage) in statuses.Where(s => s.Players > 0))
			// The card writes these in the period the game is set to show (a week, a month, a year).
			Row($"{GameWords.Of(status)} ({players})", wage);

		Heading(L("Bilet"));
		Row(L("Maç bileti"), finances.Tickets.Match);
		Row(L("Kombine"), int.TryParse(finances.Tickets.Holders, out var holders) ? L($"{finances.Tickets.Season} · {holders:N0} kombine sahibi") : finances.Tickets.Season);

		Heading(L("Sponsorluk"));
		Row(L("Toplam gelir"), L($"{finances.Sponsorship.Total} ({finances.Sponsorship.Sources} kaynak)"));
		foreach (var d in sponsors)
		{
			var term = d[2] is "" or "-" ? "" : L($" · bitiş {d[2]} ({d[3]})");
			Row(Ui.Fit(GameWords.Of(d[0]), 26), L($"{d[1]} sezonluk{term}"));
		}

		Heading(L("Yönetimin projeksiyonu"));
		foreach (var (season, balance, turnover) in projections) Row(season, L($"bakiye {balance} · ciro {turnover}"));
		if (Ui.Rich) Ui.Line($"[dim]{Ui.Safe(L("\nKalem kalem: finances gelir|gider [ay|geçen ay|sezon|geçen sezon]"))}[/]");
		else Console.WriteLine(L("\nKalem kalem: finances gelir|gider [ay|geçen ay|sezon|geçen sezon]"));
		return 0;
	}

	/// <summary>
	/// The weekly wage bill against its budget, from the club's data (in pounds a week whatever the
	/// game is set to show); the finances card writes them in the game's language and chosen period.
	/// </summary>
	private static async Task<(string Total, string Budget, string Share)> WageBill()
	{
		var club = await Fm.Pull("Human.Club", "CurrentWageSpending,WageBudget");
		var ok = double.TryParse(Field(club, "CurrentWageSpending"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var spent);
		var budgeted = double.TryParse(Field(club, "WageBudget"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var budget);
		return (ok ? Pounds(Field(club, "CurrentWageSpending")) : "-", budgeted ? Pounds(Field(club, "WageBudget")) : "-",
			ok && budgeted && budget > 0 ? GameNumbers.Percent((spent / budget * 100).ToString("0.0")) : "-");
	}

	private static string PoundsShort(string raw) =>
		double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			? MoneyFormat.Short((long)Math.Round(v)) : "-";

	private static string Pounds(string raw) =>
		double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
			? MoneyFormat.Exact((long)Math.Round(v)) : "-";

	/// <summary>
	/// Each sponsorship deal from the club's data (FinanceSponsorshipRowReference): type, value per
	/// season, until, duration, total value.
	/// </summary>
	private static async Task<List<string[]>> Sponsors() =>
		(await Fm.Pull("Human.Club.SponsorshipsData", "Name,SponsorshipValuePerSeason,DateString,Duration,PropertyValue", list: true)).EnumerateArray()
			.Select(r => new[] { Field(r, "Name"), Field(r, "SponsorshipValuePerSeason"), Field(r, "DateString"), Field(r, "Duration"), Pounds(Field(r, "PropertyValue")) })
			.Where(d => d[0].Length > 0).ToList();

	/// <summary>
	/// Income or expenditure item by item, from the club's data with no screen opened (FINDINGS §8.183):
	/// Human.Club.{Income|Expenditure}Breakdown{ThisMonth|LastMonth|ThisSeason|LastSeason} lists each item
	/// (Name, PropertyValue in pounds), Total{Income|Expenditure}{period} the total and
	/// TotalIncome{period}Turnover the turnover. The finances card shows the same lists.
	/// </summary>
	private static async Task<int> FinanceBreakdown(bool json, string kind, string period)
	{
		var (when, label) = Fold(period) switch
		{
			"" or "ay" or "bu ay" or "month" => ("ThisMonth", "bu ay"),
			"gecen ay" or "last month" => ("LastMonth", L("geçen ay")),
			"sezon" or "bu sezon" or "season" => ("ThisSeason", L("bu sezon")),
			"gecen sezon" or "last season" => ("LastSeason", L("geçen sezon")),
			_ => (null, null),
		};
		if (when == null)
		{
			Console.Error.WriteLine(L($"'{period}' bir dönem değil: ay, geçen ay, sezon, geçen sezon"));
			return 1;
		}

		var rows = (await Fm.Pull($"Human.Club.{kind}Breakdown{when}", "Name,PropertyValue", list: true)).EnumerateArray()
			.Select(r => (Item: Field(r, "Name"), Value: Field(r, "PropertyValue")))
			.Where(r => !string.IsNullOrEmpty(r.Item)).ToList();
		var totals = await Fm.Pull("Human.Club", kind == "Income" ? $"TotalIncome{when},TotalIncome{when}Turnover" : $"TotalExpenditure{when}");
		if (rows.Count == 0 || Field(totals, $"Total{kind}{when}") is not { Length: > 0 } rawTotal)
		{
			Console.Error.WriteLine(kind == "Income" ? L("Gelir dökümü okunamadı.") : L("Gider dökümü okunamadı."));
			return 1;
		}
		var total = Pounds(rawTotal);
		var turnover = kind == "Income" ? Pounds(Field(totals, $"TotalIncome{when}Turnover")) : null;
		var all = rows.Select(r => (r.Item, Amount: Pounds(r.Value), Sort: double.TryParse(r.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0)).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Kind = kind, Period = when, Total = total, Turnover = turnover, Items = all.Select(a => new { a.Item, a.Amount }) }));
			return 0;
		}
		var title = $"{(kind == "Income" ? L("Gelir") : L("Gider"))} — {label}: {total}" + (turnover != null && turnover != total ? L($" (ciro {turnover})") : "");
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(title)}[/]");
		else Console.WriteLine(title);
		// Aligned on the names as shown, in the game's words.
		var width = all.Select(a => GameWords.Of(a.Item).Length).DefaultIfEmpty(10).Max();
		foreach (var (english, amount, sort) in all.OrderByDescending(a => a.Sort))
		{
			var item = GameWords.Of(english);
			if (Ui.Rich && sort == 0) Ui.Line($"  [dim]{Ui.Safe(item.PadRight(width))}  {Ui.Safe(amount)}[/]");
			else Console.WriteLine($"  {item.PadRight(width)}  {amount}");
		}
		return 0;
	}
}
