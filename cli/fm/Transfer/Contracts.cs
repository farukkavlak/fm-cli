using System.Globalization;
using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// When each contract runs out: the squad's players read from the team's data,
/// each one's FullContract.EndDateDescription ("30/6/2028") under the row the list put him at.
/// All are bound in one pass and read in a second, so the wait for values is paid once.
/// </summary>
internal static partial class Program
{
	/// <summary>What a contract can carry besides the wage, by the name of its field.</summary>
	private static readonly (string Field, string Say)[] ContractTerms =
	[
		("Wage", L("Wage")), ("WageAfterTax", L("After tax")), ("AgreedPlayingTime", L("Squad status")), ("ContractType", L("Type")),
		("LoyaltyBonus", L("Loyalty bonus")), ("AppearanceBonus", L("Appearance fee")), ("GoalBonus", L("Goal bonus")), ("AssistBonus", L("Assist bonus")),
		("CleanSheetBonus", L("Clean sheet bonus")), ("UnusedSubBonus", L("Unused substitute fee")), ("InternationalCapBonus", L("International cap bonus")),
		("SeasonalLandmarkGoalsBonus", L("Season goals target bonus")), ("SeasonalLandmarkAssistsBonus", L("Season assists target bonus")),
		("SeasonalLandmarkGoalsAndAssistsBonus", L("Season goals + assists target bonus")),
		("MinimumFeeReleaseClauseValue", L("Release clause")), ("DomesticMinimumFeeReleaseClauseValue", L("Release clause (domestic)")),
		("ForeignMinimumFeeReleaseClauseValue", L("Release clause (foreign)")),
		("MajorContinentalCompetitionMinimumFeeReleaseClauseValue", L("Release clause (club in continental competition)")),
		("RelegationMinimumFeeReleaseClauseValue", L("Release clause (if we are relegated)")),
		("SellingOnFeePercentage", L("Sell-on fee")), ("SellingOnFeeProfitPercentage", L("Sell-on fee from profit")),
		("PromotionWageIncrease", L("Rise on promotion")), ("RelegationWageDecrease", L("Cut on relegation")),
		("MatchHighestEarnerClause", L("Match highest earner")), ("InjuryReleaseClause", L("Release on injury")),
		("WageNextSeason", L("Next season's wage")),
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
			Console.Error.WriteLine(hits.Count == 0 ? L($"'{wanted}' is not in the squad.") : L($"'{wanted}' matches {hits.Count} players: {string.Join(", ", hits.Select(h => Field(h, "Name")))}"));
			return 1;
		}
		var contract = Field(hits[0], "Row") + ".FullContract";
		var ends = Field(await Fm.Pull(contract, "EndDateDescription"), "EndDateDescription");

		// One field at a time: a field the contract does not know empties the whole reading.
		// Asked for once so they are bound, then read until each has come: an unbound field reads as {}, an empty
		// term as "-", so a field still {} is only late (all 26 arrive).
		foreach (var (field, _) in ContractTerms)
			try { await Fm.GetJson("pull", ("path", contract), ("props", $"{field}.DisplayValue")); } catch (FmError) { }
		var values = new Dictionary<string, string>();
		await Until(async () =>
		{
			foreach (var (field, _) in ContractTerms)
			{
				if (values.ContainsKey(field)) continue;
				try
				{
					var read = await Fm.GetJson("pull", ("path", contract), ("props", $"{field}.DisplayValue"));
					if (read!.ValueKind != JsonValueKind.Object || read.EnumerateObject().Any()) values[field] = Field(read, $"{field}.DisplayValue");
				}
				catch (FmError) { values[field] = ""; }
			}
			return values.Count == ContractTerms.Length;
		}, seconds: 5, every: 0.1);
		var terms = new List<(string Field, string Say, string Value)>();
		foreach (var (field, say) in ContractTerms)
		{
			var value = values.GetValueOrDefault(field, "");
			if (value.Length > 0 && value is not ("-" or "N/A" or "None" or "No" or "0%") && !(MoneyFormat.IsMoney(value) && Money(value) == 0)) terms.Add((field, say, value));
		}
		// The wage after tax is written in a sentence ("£10.5K p/w after tax"):
		// its amount, by the wage's own period.
		if (terms.FindIndex(t => t.Field == "WageAfterTax") is var net and >= 0 && MoneyFormat.Find(terms[net].Value) is { } netAmount)
		{
			var wage = terms.FirstOrDefault(t => t.Field == "Wage").Value ?? "";
			var period = MoneyFormat.Find(wage) is { } gross ? wage[(wage.IndexOf(gross, StringComparison.Ordinal) + gross.Length)..] : "";
			terms[net] = (terms[net].Field, terms[net].Say, netAmount + period);
		}

		var name = Field(hits[0], "Name");
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Ends = ends, Terms = terms.ToDictionary(t => t.Say, t => t!.Value) }));
			return 0;
		}
		Console.WriteLine(ends.Length > 0 ? L($"{name} — contract until {ends}") : L($"{name} — contract"));
		Table.Write([L("TERM"), L("VALUE")], terms.Select(t => (string[])[t.Say, t!.Value]).ToList());
		Console.WriteLine(L($"\nTo renew: contract \"{name}\""));
		return 0;
	}

	private static async Task<int> ContractsCalendar(bool json, string[] args)
	{
		if (!await Fm.InGame())
		{
			Console.Error.WriteLine(L("No save is loaded. Load one with `fm continue`."));
			return 69;
		}
		if (args.Contains("--expiring")) return await ContractsExpiring(json, args);
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
			return months < 12 ? L($"{Math.Max(months, 0)} months") : L($"{months / 12} yrs {months % 12} mths");
		}

		if (!Ui.Rich)
		{
			Table.Write([L("PLAYER"), L("AGE"), L("POSITION"), L("EXPIRES"), L("LEFT")], rows.Select(r => new[] { r.Name, r.Age, r.Position, r.Text, Left(r.Ends) }).ToList());
			return 0;
		}

		var table = new Spectre.Console.Table().Border(Spectre.Console.TableBorder.Rounded).BorderColor(Spectre.Console.Color.Grey)
			.AddColumn(L("Player")).AddColumn(L("Age")).AddColumn(L("Position")).AddColumn(L("Expires")).AddColumn(L("Left"));
		foreach (var r in rows)
		{
			// Inside a year a contract is the manager's business now; inside two, soon.
			var colour = r.Ends is { } e && e < today.AddYears(1) ? "red" : r.Ends is { } f && f < today.AddYears(2) ? "yellow" : "white";
			table.AddRow(Ui.Safe(r.Name), r.Age, Ui.Safe(r.Position), $"[{colour}]{Ui.Safe(r.Text)}[/]", $"[{colour}]{Left(r.Ends)}[/]");
		}
		Ui.Out.Write(table);
		Console.WriteLine(L("To renew: contract <player>"));
		return 0;
	}
}
