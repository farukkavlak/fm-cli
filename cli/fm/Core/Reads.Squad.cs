using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The squad table in any of its views, printed with whatever columns that view binds.
	///
	/// The table has a dozen views and each binds different fields, so instead of a command per
	/// view the columns are taken from the data: every field a row carries, less the handful
	/// that are the table's own bookkeeping.
	/// </summary>
	private static async Task<int> SquadAnyView(bool json, string? wanted)
	{
		var opened = await OpenPage(Screen.Squad);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement? viewList = null;
		await Until(async () => (viewList = await ViewList(SquadViews)) != null, seconds: 10);

		var views = viewList?.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList() ?? [];
		if (views.Count == 0)
		{
			Console.Error.WriteLine(L("The squad table's view list was not found."));
			return 1;
		}

		if (wanted == null)
		{
			if (json) Console.WriteLine(JsonOut.Of(views));
			else foreach (var name in views) Console.WriteLine(name);
			return 0;
		}

		// A view marked with › is a menu of further views, which this cannot reach into yet.
		if (Choose(views, wanted, L("View")) is not { } view) return 1;
		if (view.EndsWith("›", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"'{view}' is a submenu; the views inside it cannot be chosen yet."));
			return 1;
		}

		var before = Keys(await Fm.GetJson("squad"));
		var selected = await Fm.Get("select", ("ptr", Field(viewList!.Value, "Pointer")), ("choice", view));
		if (selected.StartsWith("error", StringComparison.Ordinal)) return Report(selected);

		// The rows refill a moment after the view changes; two readings alike, and unlike the
		// view before unless it was this one already, mean it has landed.
		JsonElement squad = default;
		var last = "";
		var waited = 0;
		await Until(async () =>
		{
			squad = await Fm.GetJson("squad");
			var keys = Keys(squad);
			var settled = keys == last && (keys != before || ++waited >= 4);
			last = keys;
			return settled;
		}, seconds: 15);

		if (json)
		{
			Console.WriteLine(squad.ToString());
			return 0;
		}

		var youth = await YouthTeams();
		Columns(squad.EnumerateArray().Where(p => !youth.Contains(Field(p, "Team"))).ToList());
		return 0;

		static string Keys(JsonElement squad) =>
			string.Join(",", squad.EnumerateArray().SelectMany(p => p.EnumerateObject().Select(f => f.Name)).Distinct().OrderBy(k => k));
	}

	/// <summary>Prints people with every field their rows carry as a column, less the table's own bookkeeping.</summary>
	private static void Columns(List<JsonElement> rows, string? who = null)
	{
		who ??= L("PLAYER");
		// Bound by a table for its own use (sorting, icons, the pitch view), in every view.
		string[] noise = ["UniqueId", "Regenerated", "Team", "Count", "PlayerStatusSortType", "NewgenAppearanceDescription",
			"AverageRating", "Index", "SquadNumberBlankIfInvalid", "PositionValue", "Position", "SubstituteString", "StatusType",
			"TransferValueRangeStatus", "Level", "AddOnType", "MaxValue", "MinValue", "Grade", "ScoutedBestRole",
			"ScoutedBestPosition", "Text", "Person", "IsRetired", "ShowDescriptorText", "EnableDebugText"];

		var columns = rows.SelectMany(p => p.EnumerateObject().Select(f => f.Name))
			.Distinct()
			.Where(c => c != "Name" && !noise.Contains(c))
			.Where(c => rows.Any(p => Field(p, c) is { Length: > 0 and < 60 }))
			.ToList();

		var ordered = rows.OrderBy(p => Field(p, "Name"), StringComparer.OrdinalIgnoreCase).ToList();
		if (!Ui.Rich)
		{
			Table.Write([L(who), .. columns.Select(Heading)], ordered
				.Select(p => new[] { Field(p, "Name") }.Concat(columns.Select(c => Field(p, c))).ToArray())
				.ToList());
			return;
		}

		// At a terminal the values are made readable and the columns that do not fit the width
		// are left out, least wanted first; the name never wraps.
		var shown = columns
			.Select(c => (Field: c, Values: ordered.Select(p => Readable(c, Field(p, c))).ToList()))
			.Where(c => c!.Values.Any(v => v.Length > 0))
			.OrderBy(c => Wanted(c.Field))
			.ToList();
		var names = ordered.Select(p => Field(p, "Name")).ToList();
		int Width(IEnumerable<(string Field, List<string> Values)> cols) =>
			names.Max(n => n.Length) + 4 + cols.Sum(c => Math.Max(Heading(c.Field).Length, c!.Values.Max(v => v.Length)) + 3);
		var room = Ui.Out.Profile.Width;
		while (shown.Count > 1 && Width(shown) > room) shown.RemoveAt(shown.Count - 1);
		var dropped = columns.Count - shown.Count;

		var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
		table.AddColumn(new Spectre.Console.TableColumn($"[dim]{L(who)}[/]") { NoWrap = true });
		foreach (var c in shown) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{Ui.Safe(Heading(c.Field))}[/]") { NoWrap = true });
		for (var i = 0; i < ordered.Count; i++)
		{
			var injured = Field(ordered[i], "StatusTitleShort").Contains("Inj", StringComparison.Ordinal) || Field(ordered[i], "IsInjured") == "True";
			var name = injured ? $"[red]{Ui.Safe(names[i])}[/]" : Ui.Safe(names[i]);
			Spectre.Console.TableExtensions.AddRow(table, [name, .. shown.Select(c => Ui.Safe(c!.Values[i]))]);
		}
		Ui.Out.Write(table);
		if (dropped > 0) Ui.Line(L($"[dim]{dropped} columns did not fit; for all of them: fm players --json or a wider terminal[/]"));
	}

	/// <summary>The game's raw values as a reader wants them: no "-1", no "Scouting Required", yes for True.</summary>
	private static string Readable(string field, string value) => (field, value) switch
	{
		(_, "-1" or "-" or "None" or "False") => "",
		(_, "True") => L("yes"),
		("ShortGradeString", "Scouting Required") => "",
		_ => value,
	};

	/// <summary>Which columns stay when the terminal is narrow: lower first.</summary>
	private static int Wanted(string field) => field switch
	{
		"Age" => 0,
		"PositionCombinedString" => 1,
		"TransferValueRangeString" => 2,
		"ShortName" or "Club" => 3,
		"Wage" => 4,
		"StatusTitleShort" => 5,
		"AgreedPlayingTime" => 6,
		"AskingPrice" => 7,
		"ContractStatus" => 8,
		"ShortGradeString" => 9,
		_ => 10,
	};

	/// <summary>The columns' names as a reader knows them; anything else spelled out from the game's own field name.</summary>
	private static readonly Dictionary<string, string> Headings = new()
	{
		["PositionCombinedString"] = L("POSITION"), ["Age"] = L("AGE"), ["Wage"] = L("WAGE"), ["SquadNumber"] = "NO",
		["TransferValueRangeString"] = L("VALUE"), ["AskingPrice"] = L("ASKING PRICE"), ["TransferOptions"] = L("TRANSFER STATUS"),
		["AgreedPlayingTime"] = L("PLAYING TIME"), ["FutureAgreedPlayingTime"] = L("FUTURE PLAYING TIME"), ["ActualPlayingTime"] = L("ACTUAL TIME"),
		["PlayingTimeHappinessLevel"] = L("PLAYING TIME HAPPINESS"), ["ContractStatus"] = L("CONTRACT"), ["Condition"] = L("CONDITION"),
		["Sharpness"] = L("MATCH SHARPNESS"), ["InjuriesTimeMissed"] = L("TIME OUT"), ["InjurySeverity"] = L("SEVERITY"),
		["InjuryDescription"] = L("INJURY"), ["TrainingRating"] = L("TRAINING RATING"), ["MoraleString"] = "MORAL",
		["StatusTitleShort"] = L("STATUS"), ["Nation"] = L("NATION"), ["ThreeLetterName"] = L("NATION"), ["Club"] = L("CLUB"), ["Personality"] = L("PERSONALITY"),
		["HierarchyInfluenceCategoryString"] = L("HIERARCHY"), ["SocialGroupString"] = L("GROUP"), ["Height"] = L("HEIGHT"),
		["ShortName"] = L("CLUB"), ["ShortGradeString"] = "SCOUT", ["ScoutingCosts"] = L("SCOUTING COST"),
		["DaysSinceLastScouted"] = L("LAST SCOUTED"), ["ScoutedMinWage"] = L("WAGE (MIN)"), ["ScoutedMaxWage"] = L("WAGE (MAX)"),
	};

	private static string Heading(string field)
	{
		if (Headings.TryGetValue(field, out var known)) return known;

		// "InternationalCapsAndGoalsString" → "INTERNATIONAL CAPS AND GOALS".
		var name = field.EndsWith("String", StringComparison.Ordinal) && field.Length > 6 ? field[..^6] : field;
		var spaced = new System.Text.StringBuilder();
		for (var i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]) && (char.IsLower(name[i - 1]) || i + 1 < name.Length && char.IsLower(name[i + 1]))) spaced.Append(' ');
			spaced.Append(name[i]);
		}
		return spaced.ToString().ToUpperInvariant();
	}

	/// <summary>
	/// The player database: every player the game knows, as its Player Database page lists them.
	/// A quick filter narrows it (transfer listed, contract expiring…) and a view decides which
	/// columns the rows carry. Only the rows the page has on screen are bound, so this is the
	/// top of the list, not all of it.
	/// </summary>
	private static async Task<int> Players(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;

		var opened = await OpenPage(Screen.PlayerDatabase);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		const string filterList = "inputs-combobox-default-default";
		JsonElement? views = null, filters = null;
		await Until(async () =>
		{
			var lists = (await Fm.GetJson("dropdowns", ("json", "1"))).EnumerateArray().ToList();
			views = await ViewList(PlayerSearchViews);
			filters = lists.Where(d => Field(d, "Name") == filterList).Cast<JsonElement?>().FirstOrDefault();
			return views != null && filters != null;
		}, seconds: 12);

		if (views == null || filters == null)
		{
			Console.Error.WriteLine(L("The player search page did not show the lists expected."));
			return 1;
		}

		var viewNames = views.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();
		var filterNames = filters.Value.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "").ToList();

		if (args.Contains("--filters"))
		{
			foreach (var name in filterNames) Console.WriteLine(name);
			return 0;
		}
		if (args.Contains("--views"))
		{
			foreach (var name in viewNames) Console.WriteLine(name);
			return 0;
		}

		var before = (await Fm.GetJson("people")).ToString();

		// A quick filter stays on until the page's own Clear is pressed; the button is found by
		// the event it sends, which no language changes.
		if (args.Contains("--clear"))
		{
			var cleared = await Arriving("press", [("event", "EventToPlugin:EventFmxResetFilter")]);
			if (cleared.StartsWith("error", StringComparison.Ordinal)) return Report(cleared);
		}

		// The page's search panel: the role he can play, his age, the scouts' grade, his
		// contract and whether he is listed. Each rule is named by the data path the game binds
		// its control to, which no language changes; the heading word is not used. A search stays
		// on in the game until the next one or --clear.
		var search = new List<(string Rule, string Choice, string What)>();
		if (Option(args, "--role") is { } searchRole) search.Add(("filter.FilterRuleIpRole.filtervalue", searchRole, L("Role")));
		if (Option(args, "--age") is { } ages)
		{
			var ends = ages.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
			if (ends.Length != 2 || !int.TryParse(ends[0], out _) || !int.TryParse(ends[1], out _))
			{
				Console.Error.WriteLine(L("An age range takes two numbers:  --age 18-23"));
				return 64;
			}
			search.Add(("filter.filterruleage.minvalue", ends[0], L("Youngest age")));
			search.Add(("filter.filterruleage.maxvalue", ends[1], L("Oldest age")));
		}
		if (Option(args, "--grade") is { } grade) search.Add(("filter.FilterRuleScoutingGrade.MinValue", grade, L("Scout's note")));
		if (Option(args, "--contract") is { } contract) search.Add(("filter.filterrulecontract.filtervalue", contract, L("Contract status")));
		if (args.Contains("--listed")) search.Add(("filter.filterruletransferstatus.filtervalue", "Listed", L("Transfer status")));
		if (args.Contains("--loan-listed")) search.Add(("filter.filterruleloanstatus.filtervalue", "Listed", L("Loan status")));
		// --attr "pace>=15" (also <=, >, <), as many as wanted.
		var conditions = new List<(string Attribute, bool AtLeast, int Value)>();
		for (var i = 0; i + 1 < args.Length; i++)
		{
			if (args[i] != "--attr") continue;
			var match = System.Text.RegularExpressions.Regex.Match(args[i + 1], @"^\s*(.+?)\s*(>=|<=|>|<)\s*(\d+)\s*$");
			if (!match.Success || !int.TryParse(match.Groups[3].Value, out var bound))
			{
				Console.Error.WriteLine(L($"'{args[i + 1]}': an attribute condition takes the form \"<attribute>>=<number>\" (example: --attr \"pace>=15\")."));
				return 64;
			}
			var op = match.Groups[2].Value;
			conditions.Add((match.Groups[1].Value, op is ">=" or ">", op == ">" ? bound + 1 : op == "<" ? bound - 1 : bound));
		}
		var searched = false;
		var position = Option(args, "--position");

		// The most he may be worth: the page takes round figures, so the largest of them that
		// is not above what was typed is used, and said when it differs.
		long? maxValue = null;
		if (Option(args, "--max-value") is { } typedValue)
		{
			// The list's figures, in any currency's symbol and any language's letters.
			long[] figures = [0, 1_000, 10_000, 20_000, 40_000, 60_000, 100_000, 250_000, 500_000, 1_000_000, 2_500_000, 5_000_000, 7_500_000,
				10_000_000, 15_000_000, 20_000_000, 30_000_000, 40_000_000, 60_000_000, 80_000_000, 100_000_000];
			if (Money(typedValue) is not { } limit || limit < 0)
			{
				Console.Error.WriteLine(L($"'{typedValue}': must be money (500K, 10M)."));
				return 64;
			}
			maxValue = figures.LastOrDefault(f => f <= limit);
			if (maxValue != limit) Console.WriteLine(L($"(the value limit was taken as {MoneyFormat.Short(maxValue.Value)} from the game's list)"));
		}
		if (search.Count > 0 || conditions.Count > 0 || position != null || maxValue != null)
		{
			// A search is added to whatever an earlier one left on, and two positions both
			// required find almost no one: each search starts from a clean one.
			if (!args.Contains("--clear"))
			{
				var cleared = await Arriving("press", [("event", "EventToPlugin:EventFmxResetFilter")]);
				if (cleared.StartsWith("error", StringComparison.Ordinal)) return Report(cleared);
				// The filter is gone when the button that opens its panel is back; it says how many
				// rules are on it in the game's own words, so it is found by what it sends.
				await Until(async () => (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray()
					.Any(b => Field(b, "Events").Contains("EventToPlugin:EventFmxFilterStoreFilter", StringComparison.Ordinal)), seconds: 4, every: 0.3);
			}
			if (await ApplySearch(search, conditions, position, maxValue) is { } failed) return failed.Length == 0 ? 64 : Report(failed);
			searched = true;
		}

		// Free agents are the players whose contract has run out.
		if ((Option(args, "--filter") ?? (args.Contains("--free") ? "Contract Expired" : null)) is { } wantedFilter)
		{
			if (Choose(filterNames, wantedFilter, L("Filter")) is not { } filter) return 1;

			// The filter is sent to the search by the list's own event, not just written.
			var chosen = await Fm.Get("select", ("ptr", Field(filters.Value, "Pointer")), ("choice", filter), ("fire", "1"));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		}

		if (Option(args, "--view") is { } wantedView)
		{
			if (Choose(viewNames, wantedView, L("View")) is not { } view) return 1;
			if (view.EndsWith("›", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"'{view}' is a submenu; the views inside it cannot be chosen yet."));
				return 1;
			}

			var chosen = await Fm.Get("select", ("ptr", Field(views.Value, "Pointer")), ("choice", view));
			if (chosen.StartsWith("error", StringComparison.Ordinal)) return Report(chosen);
		}

		// The rows refill a moment after a filter or view changes; two readings alike, and
		// unlike what was there before unless nothing was asked for, mean they have landed.
		JsonElement people = default;
		var last = "";
		var waited = 0;
		var asked = searched || Option(args, "--filter") != null || Option(args, "--view") != null || args.Contains("--clear") || args.Contains("--free");
		await Until(async () =>
		{
			var now = await Fm.GetJson("people");
			var text = now.ToString();
			var settled = text == last && now.GetArrayLength() > 0 && (!asked || text != before || ++waited >= 5);
			last = text;
			if (settled) people = now;
			return settled;
		}, seconds: 25);

		if (people.ValueKind != JsonValueKind.Array) people = await Fm.GetJson("people");
		if (json)
		{
			Console.WriteLine(people.ToString());
			return 0;
		}

		Columns(people.EnumerateArray().ToList());

		var count = await Found("PersonListCountString");
		Console.WriteLine(L($"\n{people.GetArrayLength()} rows on screen{(count != null ? L($"; the game: \"{count}\"") : "")}"));
		return 0;
	}
}
