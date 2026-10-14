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
		("Wage", "Ücret"), ("WageAfterTax", "Vergi sonrası"), ("AgreedPlayingTime", "Kadro statüsü"), ("ContractType", "Tür"),
		("LoyaltyBonus", "Sadakat primi"), ("AppearanceBonus", "Maç başı ücret"), ("GoalBonus", "Gol primi"), ("AssistBonus", "Asist primi"),
		("CleanSheetBonus", "Gol yememe primi"), ("UnusedSubBonus", "Oynamayan yedek ücreti"), ("InternationalCapBonus", "Milli maç primi"),
		("SeasonalLandmarkGoalsBonus", "Sezon gol hedefi primi"), ("SeasonalLandmarkAssistsBonus", "Sezon asist hedefi primi"),
		("SeasonalLandmarkGoalsAndAssistsBonus", "Sezon gol + asist hedefi primi"),
		("MinimumFeeReleaseClauseValue", "Serbest kalma bedeli"), ("DomesticMinimumFeeReleaseClauseValue", "Serbest kalma bedeli (yurt içi)"),
		("ForeignMinimumFeeReleaseClauseValue", "Serbest kalma bedeli (yurt dışı)"),
		("MajorContinentalCompetitionMinimumFeeReleaseClauseValue", "Serbest kalma bedeli (Avrupa'da oynayan kulüp)"),
		("RelegationMinimumFeeReleaseClauseValue", "Serbest kalma bedeli (küme düşersek)"),
		("SellingOnFeePercentage", "Sonraki satıştan pay"), ("SellingOnFeeProfitPercentage", "Sonraki satış kârından pay"),
		("PromotionWageIncrease", "Yükselirsek zam"), ("RelegationWageDecrease", "Düşersek indirim"),
		("MatchHighestEarnerClause", "En yüksek ücretliye eşitleme"), ("InjuryReleaseClause", "Sakatlıkta fesih"),
		("WageNextSeason", "Gelecek sezon ücreti"),
	];

	/// <summary>One of our players' contract in full: `contracts <oyuncu>`.</summary>
	private static async Task<int> ContractOf(bool json, string wanted)
	{
		var players = (await Fm.Pull("Human.Team.Players", "Name,Age,PositionCombinedString", list: true)).EnumerateArray().ToList();
		var hits = Matching(players, p => Field(p, "Name"), wanted);
		if (hits.Count != 1)
		{
			Console.Error.WriteLine(hits.Count == 0 ? $"'{wanted}' kadroda yok." : $"'{wanted}' {hits.Count} oyuncuya uyuyor: {string.Join(", ", hits.Select(h => Field(h, "Name")))}");
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
			if (value.Length > 0 && value is not ("-" or "N/A" or "None" or "No" or "£0" or "0%")) terms.Add((say, value.Replace(" after tax", "")));
		}

		var name = Field(hits[0], "Name");
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Ends = ends, Terms = terms.ToDictionary(t => t.Say, t => t.Value) }));
			return 0;
		}
		Console.WriteLine($"{name} — sözleşme {(ends.Length > 0 ? ends + " tarihine kadar" : "")}");
		Table.Write(["ŞART", "DEĞER"], terms.Select(t => (string[])[t.Say, t.Value]).ToList());
		Console.WriteLine($"\nYenilemek için: contract \"{name}\"");
		return 0;
	}

	private static async Task<int> ContractsCalendar(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine("Kayıt yüklü değil. `fm continue` ile yükle.");
			return 69;
		}
		if (string.Join(" ", Words(args)) is { Length: > 0 } wanted) return await ContractOf(json, wanted);

		var players = (await Fm.Pull("Human.Team.Players", "Name,Age,PositionCombinedString", list: true)).EnumerateArray().ToList();
		const string props = "EndDateDescription";
		foreach (var p in players) await Fm.GetJson("pull", ("path", Field(p, "Row") + ".FullContract"), ("props", props));

		var today = DateTime.TryParseExact(Field(await Fm.GetJson("state"), "Date"), "yyyy-MM-dd", null, DateTimeStyles.None, out var d) ? d : DateTime.Today;
		var rows = new List<(string Name, string Age, string Position, DateTime? Ends, string Text)>();
		foreach (var p in players)
		{
			var ends = Field(await Fm.Pull(Field(p, "Row") + ".FullContract", props), "EndDateDescription");
			DateTime? date = DateTime.TryParseExact(ends, "d/M/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var e) ? e : null;
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
			return months < 12 ? $"{Math.Max(months, 0)} ay" : $"{months / 12} yıl {months % 12} ay";
		}

		if (!Ui.Rich)
		{
			Table.Write(["OYUNCU", "YAŞ", "MEVKİ", "BİTİŞ", "KALAN"], rows.Select(r => new[] { r.Name, r.Age, r.Position, r.Text, Left(r.Ends) }).ToList());
			return 0;
		}

		var table = new Spectre.Console.Table().Border(Spectre.Console.TableBorder.Rounded).BorderColor(Spectre.Console.Color.Grey)
			.AddColumn("Oyuncu").AddColumn("Yaş").AddColumn("Mevki").AddColumn("Bitiş").AddColumn("Kalan");
		foreach (var r in rows)
		{
			// Inside a year a contract is the manager's business now; inside two, soon.
			var colour = r.Ends is { } e && e < today.AddYears(1) ? "red" : r.Ends is { } f && f < today.AddYears(2) ? "yellow" : "white";
			table.AddRow(Ui.Safe(r.Name), r.Age, Ui.Safe(r.Position), $"[{colour}]{Ui.Safe(r.Text)}[/]", $"[{colour}]{Left(r.Ends)}[/]");
		}
		Ui.Out.Write(table);
		Console.WriteLine("Yenilemek için: contract <oyuncu>");
		return 0;
	}
}
