using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// When each contract runs out (FINDINGS §8.72): the squad's players read from the team's data,
/// each one's FullContract.EndDateDescription ("30/6/2028") under the row the list put him at.
/// All are bound in one pass and read in a second, so the wait for values is paid once.
/// </summary>
internal static partial class Program
{
	/// <summary>What a contract can carry besides the wage, by the name of its field (FINDINGS §8.96).</summary>
	private static readonly (string Field, string Say)[] ContractTerms =
	[
		("Wage", L("Ücret")), ("WageAfterTax", L("Vergi sonrası")), ("AgreedPlayingTime", L("Kadro statüsü")), ("ContractType", L("Tür")),
		("LoyaltyBonus", L("Sadakat primi")), ("AppearanceBonus", L("Maç başı ücret")), ("GoalBonus", L("Gol primi")), ("AssistBonus", L("Asist primi")),
		("CleanSheetBonus", L("Gol yememe primi")), ("UnusedSubBonus", L("Oynamayan yedek ücreti")), ("InternationalCapBonus", L("Milli maç primi")),
		("SeasonalLandmarkGoalsBonus", L("Sezon gol hedefi primi")), ("SeasonalLandmarkAssistsBonus", L("Sezon asist hedefi primi")),
		("SeasonalLandmarkGoalsAndAssistsBonus", L("Sezon gol + asist hedefi primi")),
		("MinimumFeeReleaseClauseValue", L("Serbest kalma bedeli")), ("DomesticMinimumFeeReleaseClauseValue", L("Serbest kalma bedeli (yurt içi)")),
		("ForeignMinimumFeeReleaseClauseValue", L("Serbest kalma bedeli (yurt dışı)")),
		("MajorContinentalCompetitionMinimumFeeReleaseClauseValue", L("Serbest kalma bedeli (Avrupa'da oynayan kulüp)")),
		("RelegationMinimumFeeReleaseClauseValue", L("Serbest kalma bedeli (küme düşersek)")),
		("SellingOnFeePercentage", L("Sonraki satıştan pay")), ("SellingOnFeeProfitPercentage", L("Sonraki satış kârından pay")),
		("PromotionWageIncrease", L("Yükselirsek zam")), ("RelegationWageDecrease", L("Düşersek indirim")),
		("MatchHighestEarnerClause", L("En yüksek ücretliye eşitleme")), ("InjuryReleaseClause", L("Sakatlıkta fesih")),
		("WageNextSeason", L("Gelecek sezon ücreti")),
	];

	/// <summary>
	/// A day from the game's data ("2027-06-30"); null for none, which the game writes as a day in 1900
	/// (a player with no end to his contract shows "N/A").
	/// </summary>
	internal static DateTime? GameDay(string iso) =>
		DateTime.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year > 1900 ? day : null;

	/// <summary>One of our players' contract in full: `contracts <oyuncu>`.</summary>
	private static async Task<int> ContractOf(bool json, string wanted)
	{
		var players = (await Fm.Pull("Human.Team.Players", "Name,Age,PositionCombinedString", list: true)).EnumerateArray().ToList();
		var hits = Matching(players, p => Field(p, "Name"), wanted);
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? L($"'{wanted}' kadroda yok.") : L($"'{wanted}' {hits.Count} oyuncuya uyuyor: {string.Join(", ", hits.Select(h => Field(h, "Name")))}"));
			return 1;
		}
		var contract = Field(hits[0], "Row") + ".FullContract";
		var ends = Field(await Fm.Pull(contract, "EndDateDescription"), "EndDateDescription");

		// One field at a time: a field the contract does not know empties the whole reading.
		var terms = new List<(string Say, string Value)>();
		// Asked for once so they are bound, then read: a value is there a frame after it is asked for.
		foreach (var (field, _) in ContractTerms)
			try { await Fm.GetJson("pull", ("path", contract), ("props", $"{field}.DisplayValue")); } catch (FmError) { }
		await Task.Delay(300);
		foreach (var (field, say) in ContractTerms)
		{
			JsonElement read;
			try { read = await Fm.GetJson("pull", ("path", contract), ("props", $"{field}.DisplayValue")); }
			catch (FmError) { continue; }
			var value = Field(read, $"{field}.DisplayValue");
			if (value.Length > 0 && value is not ("-" or "N/A" or "None" or "No" or "0%") && !(MoneyFormat.IsMoney(value) && Money(value) == 0)) terms.Add((say, value));
		}
		// The wage after tax is written in a sentence ("£10.5K p/w after tax", "Vergi sonrası 10,5B £ aylık"):
		// its amount, by the wage's own period.
		if (terms.FindIndex(t => t.Say == L("Vergi sonrası")) is var net and >= 0 && MoneyFormat.Find(terms[net].Value) is { } netAmount)
		{
			var wage = terms.FirstOrDefault(t => t.Say == L("Ücret")).Value ?? "";
			var period = MoneyFormat.Find(wage) is { } gross ? wage[(wage.IndexOf(gross, StringComparison.Ordinal) + gross.Length)..] : "";
			terms[net] = (terms[net].Say, netAmount + period);
		}

		var name = Field(hits[0], "Name");
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Ends = ends, Terms = terms.ToDictionary(t => t.Say, t => t.Value) }));
			return 0;
		}
		Console.WriteLine(ends.Length > 0 ? L($"{name} — sözleşme {ends} tarihine kadar") : L($"{name} — sözleşme"));
		Table.Write([L("ŞART"), L("DEĞER")], terms.Select(t => (string[])[t.Say, t.Value]).ToList());
		Console.WriteLine(L($"\nYenilemek için: contract \"{name}\""));
		return 0;
	}

	private static async Task<int> ContractsCalendar(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("Kayıt yüklü değil. `fm continue` ile yükle."));
			return 69;
		}
		if (string.Join(" ", Words(args)) is { Length: > 0 } wanted) return await ContractOf(json, wanted);

		var players = (await Fm.Pull("Human.Team.Players", "Name,Age,PositionCombinedString", list: true)).EnumerateArray().ToList();
		// The day as data (the shown text follows the game's date format: day or month first).
		const string props = "EndDate,EndDateDescription";
		foreach (var p in players) await Fm.GetJson("pull", ("path", Field(p, "Row") + ".FullContract"), ("props", props));

		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var rows = new List<(string Name, string Age, string Position, DateTime? Ends, string Text)>();
		foreach (var p in players)
		{
			var read = await Fm.Pull(Field(p, "Row") + ".FullContract", props);
			var ends = Field(read, "EndDateDescription");
			var date = GameDay(Field(read, "EndDate"));
			rows.Add((Field(p, "Name"), Field(p, "Age"), Field(p, "PositionCombinedString"), date, ends));
		}
		rows = rows.OrderBy(r => r.Ends ?? DateTime.MaxValue).ThenBy(r => r.Name).ToList();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(rows.Select(r => new { r.Name, r.Age, r.Position, Ends = r.Text })));
			return 0;
		}

		string Left(DateTime? ends)
		{
			if (ends is not { } e) return "";
			var months = (e.Year - today.Year) * 12 + e.Month - today.Month;
			return months < 12 ? L($"{Math.Max(months, 0)} ay") : L($"{months / 12} yıl {months % 12} ay");
		}

		if (!Ui.Rich)
		{
			Table.Write([L("OYUNCU"), L("YAŞ"), L("MEVKİ"), L("BİTİŞ"), L("KALAN")], rows.Select(r => new[] { r.Name, r.Age, r.Position, r.Text, Left(r.Ends) }).ToList());
			return 0;
		}

		var table = new Spectre.Console.Table().Border(Spectre.Console.TableBorder.Rounded).BorderColor(Spectre.Console.Color.Grey)
			.AddColumn(L("Oyuncu")).AddColumn(L("Yaş")).AddColumn(L("Mevki")).AddColumn(L("Bitiş")).AddColumn(L("Kalan"));
		foreach (var r in rows)
		{
			// Inside a year a contract is the manager's business now; inside two, soon.
			var colour = r.Ends is { } e && e < today.AddYears(1) ? "red" : r.Ends is { } f && f < today.AddYears(2) ? "yellow" : "white";
			table.AddRow(Ui.Safe(r.Name), r.Age, Ui.Safe(r.Position), $"[{colour}]{Ui.Safe(r.Text)}[/]", $"[{colour}]{Left(r.Ends)}[/]");
		}
		Ui.Out.Write(table);
		Console.WriteLine(L("Yenilemek için: contract <oyuncu>"));
		return 0;
	}
}
