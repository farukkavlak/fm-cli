using System.Text.Json;

namespace FmCli;

/// <summary>
/// The club at a glance, from the game's own records: the board's and the supporters' view
/// (Human.Team.TeamConfidence), the vision's objectives with the board's word on each, the
/// finances and wage bill, the facilities, reputation and the staff's vacancies. Only the
/// objectives need a page: Club › Overview's Vision tab lays out their list.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Club(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var read = await PullShown([
			("Human.Team.TeamConfidence", "BoardConfidenceOverallString,BoardVisionString,SupporterConfidenceOverallString"),
			("Human.Club", "OverallBalance,TotalIncomeLastMonth,TotalExpenditureLastMonth,SupporterBoardInfluenceString,ActiveBoardRequestsCount"),
			("Human.Club.ReputationScope", "DisplayString"),
			("Human.Team.Club.TrainingFacilityRating", "Description"),
			("Human.Team.Club.YouthFacilityRating", "Description"),
			("Human.Team.Club.CorporateFacilityRating", "Description"),
		]);
		string At(int i, string field) => Field(read[i], field) is { Length: > 0 } v ? v : "-";
		if (Field(read[1], "OverallBalance").Length == 0)
		{
			Console.Error.WriteLine(L("The club page could not be read."));
			return 1;
		}

		var objectives = await VisionObjectives();

		// Each staff team's head count, places and what is missing, from the team's data; the sentence
		// is the game's own ("1 vacant role (Set Piece Coach)"), in its language.
		var teams = new List<(string Team, string Filled, string Places, string Vacant)>();
		foreach (var (team, subgroup) in new[] { (L("Coaches"), "StaffCoachingSubGroup"), ("Scouting / transfer", "StaffRecruitmentSubGroup"), (L("Medical"), "StaffMedicalSubGroup") })
		{
			var counts = await Fm.Pull($"Human.Team.{subgroup}", "Count,MaxValue,LongString", shown: "LongString");
			teams.Add((team, Field(counts, "Count"), Field(counts, "MaxValue"), Field(counts, "LongString")));
		}

		var wages = await WageBill();
		var club = new
		{
			Objectives = objectives.Select(o => new { o.Text, o.Board }),
			Board = new { Confidence = At(0, "BoardConfidenceOverallString"), Vision = At(0, "BoardVisionString"), Requests = At(1, "ActiveBoardRequestsCount") },
			Supporters = new { Confidence = At(0, "SupporterConfidenceOverallString"), Influence = At(1, "SupporterBoardInfluenceString") },
			Finances = new
			{
				Balance = Pounds(Field(read[1], "OverallBalance")), IncomeLastMonth = Pounds(Field(read[1], "TotalIncomeLastMonth")),
				ExpenditureLastMonth = Pounds(Field(read[1], "TotalExpenditureLastMonth")),
				Wages = wages.Total, WageBudget = wages.Budget, WageShare = wages.Share,
			},
			Facilities = new { Training = At(3, "Description"), Youth = At(4, "Description"), Corporate = At(5, "Description") },
			Reputation = At(2, "DisplayString"),
			Staff = teams.Select(t => new { t.Team, t.Filled, t.Places, t.Vacant }),
		};
		if (json)
		{
			Console.WriteLine(JsonOut.Of(club));
			return 0;
		}

		void Heading(string title)
		{
			if (Ui.Rich) Ui.Line($"\n[bold]{Ui.Safe(title)}[/]");
			else Console.WriteLine($"\n{title}");
		}
		void Row(string label, string value)
		{
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(label),-22} {Ui.Safe(value)}");
			else Console.WriteLine($"  {label,-22} {value}");
		}

		Heading(L("Board"));
		Console.WriteLine($"  {club.Board.Confidence}");
		Console.WriteLine($"  {club.Board.Vision}");
		Row(L("Pending requests"), club.Board.Requests);
		Heading(L("Supporters"));
		Console.WriteLine($"  {club.Supporters.Confidence}");
		Row(L("Influence on the board"), club.Supporters.Influence);

		Heading(L("Objectives"));
		if (objectives.Count == 0) Console.WriteLine(L("  (the list could not be read)"));
		foreach (var o in objectives)
		{
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(o.Text)}[dim]{(o.Board.Length > 0 ? " — " + Ui.Safe(o.Board) : "")}[/]");
			else Console.WriteLine($"  {o.Text}{(o.Board.Length > 0 ? " — " + o.Board : "")}");
		}

		Heading(L("Finances"));
		Row(L("Balance"), club.Finances.Balance);
		Row(L("Income last month"), club.Finances.IncomeLastMonth);
		Row(L("Expenditure last month"), club.Finances.ExpenditureLastMonth);
		Row(L("Weekly wages"), $"{club.Finances.Wages} / {club.Finances.WageBudget} ({club.Finances.WageShare})");

		Heading(L("Club"));
		Row(L("Reputation"), club.Reputation);
		Row(L("Training facilities"), club.Facilities.Training);
		Row(L("Youth facilities"), club.Facilities.Youth);
		Row(L("Commercial"), club.Facilities.Corporate);

		Heading(L("Staff"));
		foreach (var t in teams)
		{
			var open = System.Text.RegularExpressions.Regex.Match(t.Vacant, @"\((.*)\)") is { Success: true } m ? L($" — vacant: {m.Groups[1].Value}") : "";
			if (Ui.Rich) Ui.Line($"  {Ui.Safe(t.Team),-22} {Ui.Safe(t.Filled)}/{Ui.Safe(t.Places)}[dim]{Ui.Safe(open)}[/]");
			else Console.WriteLine($"  {t.Team,-22} {t.Filled}/{t.Places}{open}");
		}
		return 0;
	}

	/// <summary>
	/// The vision's objectives, each with the board's sentence on how it goes (VisionObjectivePerformance).
	/// They are laid out only on Club › Overview's Vision tab (its element name); the rows are the
	/// list's own items, found by the text bound to an objective's name.
	/// </summary>
	private static async Task<List<(string Text, string Board)>> VisionObjectives()
	{
		var opened = await OpenPage(Screen.ClubHome);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return [];
		var gen = Environment.TickCount64.ToString();
		JsonElement rows = default;
		await Until(async () =>
		{
			await Fm.Get("click", ("within", "TabButtons"), ("name", "Vision"));
			rows = await Fm.GetJson("rowtexts", ("has", "clubvision.VisionObjectiveName"), ("gen", gen));
			return rows.ValueKind == JsonValueKind.Array && rows.GetArrayLength() > 0;
		}, seconds: 10, every: 0.5);
		if (rows.ValueKind != JsonValueKind.Array) return [];
		var items = await PullShown(rows.EnumerateArray().Select(r => (Field(r, "Item"), "VisionObjectiveName,VisionObjectivePerformance")).ToList());
		return items.Select(i => (Field(i, "VisionObjectiveName"), Field(i, "VisionObjectivePerformance"))).Where(o => o.Item1.Length > 0).ToList();
	}
}
