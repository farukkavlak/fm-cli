namespace FmCli;

/// <summary>
/// What the board and the supporters think of the manager, read from the team's
/// own confidence record with no screen: a sentence for each thing they judge (results, the
/// squad's backing, tactics, transfers, the club's vision and culture, the competitions) and the
/// lists of what pleased and what worried them.
/// </summary>
internal static partial class Program
{
	private static readonly (string Label, string Field)[] BoardAreas =
	[
		(L("Overall"), "BoardConfidenceOverallString"), (L("Matches"), "BoardMatchString"), (L("Squad"), "BoardSquadConfidenceString"),
		(L("Tactics"), "BoardTacticsString"), ("Transfer", "BoardTransferConfidenceOverallSummaryText"), (L("Vision"), "BoardVisionString"),
		(L("Culture"), "BoardCultureProgressString"), (L("Competitions"), "InternationalCompConfidenceStringLong"),
	];

	private static readonly (string Label, string Field)[] SupporterAreas =
	[
		(L("Overall"), "SupporterConfidenceOverallString"), (L("Matches"), "SupporterMatchString"), (L("Tactics"), "SupporterTacticsString"),
		("Transfer", "SupporterTransferConfidenceOverallSummaryText"), (L("Vision"), "SupporterVisionString"),
	];

	private static async Task<int> Board(bool json)
	{
		const string at = "Human.Team.TeamConfidence";
		var said = await Fm.Pull(at, string.Join(",", BoardAreas.Concat(SupporterAreas).Select(a => a.Field)));
		var praise = await Fm.Strings(at + ".BoardFeedbackHighlights");
		var worries = await Fm.Strings(at + ".BoardFeedbackCriticism");
		var fansPraise = await Fm.Strings(at + ".SupporterFeedbackHighlights");
		var fansWorries = await Fm.Strings(at + ".SupporterFeedbackCriticism");
		var expected = await SeasonExpectations();

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				// The record answers in English where the game has no stored translation; the same
				// words the line path puts back at write time are put back here (Board, Turkish game).
				Board = BoardAreas.ToDictionary(a => a.Label, a => GameWords.Of(Field(said, a.Field))), BoardPraise = praise, BoardWorries = worries,
				Supporters = SupporterAreas.ToDictionary(a => a.Label, a => GameWords.Of(Field(said, a.Field))), SupporterPraise = fansPraise, SupporterWorries = fansWorries,
				Expectations = expected.Select(e => new { e.Competition, e.Expectation }),
			}));
			return 0;
		}

		if (BoardAreas.All(a => Field(said, a.Field).Length == 0))
		{
			Console.Error.WriteLine(L("The board's view could not be read."));
			return 1;
		}

		Block(L("Board"), BoardAreas, praise, worries);
		if (expected.Count > 0)
		{
			if (Ui.Rich) Ui.Line($"  [dim]{L("This season")}[/]");
			else Console.WriteLine($"  {L("This season")}");
			foreach (var (competition, expectation) in expected)
			{
				if (Ui.Rich) Ui.Line($"    {Ui.Safe(competition)}: {Ui.Safe(expectation)}");
				else Console.WriteLine($"    {competition}: {expectation}");
			}
		}
		Console.WriteLine();
		Block(L("Supporters"), SupporterAreas, fansPraise, fansWorries);
		return 0;

		void Block(string title, (string Label, string Field)[] areas, List<string> good, List<string> bad)
		{
			if (Ui.Rich) Ui.Line($"[bold]{title}[/]");
			else Console.WriteLine(title);

			foreach (var (label, field) in areas)
			{
				// The tactics verdict comes in two paragraphs; on one line it reads as one.
				var text = string.Join(" ", Field(said, field).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
				if (text.Length == 0) continue;
				if (Ui.Rich) Ui.Line($"  [dim]{label,-11}[/] {Ui.Safe(text)}");
				else Console.WriteLine($"  {label,-11} {text}");
			}

			Points(L("Pleased with"), good, "green", "+");
			Points(L("Concerns"), bad, "red", "-");
		}

		static void Points(string heading, List<string> points, string colour, string mark)
		{
			if (points.Count == 0) return;
			if (Ui.Rich) Ui.Line($"  [dim]{heading}[/]");
			else Console.WriteLine($"  {heading}");
			foreach (var point in points)
			{
				if (Ui.Rich) Ui.Line($"    [{colour}]{mark}[/] {Ui.Safe(point)}");
				else Console.WriteLine($"    {mark} {point}");
			}
		}
	}

	/// <summary>
	/// What the board expects of us in each competition this season, in the game's words. The words are bound only on
	/// Portal › Competitions (each competition's record there carries them for our team), so that page is opened and the
	/// expectations read by their data path; whole once there are as many as the board judges competitions.
	/// </summary>
	private static async Task<List<(string Competition, string Expectation)>> SeasonExpectations()
	{
		var count = Field(await Fm.Pull("Human.Club.ClubVisionMeetingSeasonExpectations", "ClubSeasonExpectationsNumberCompetitions"), "ClubSeasonExpectationsNumberCompetitions");
		if (!int.TryParse(count, out var wanted) || wanted == 0) return [];
		if ((await OpenPage(Screen.Competitions)).StartsWith("error", StringComparison.Ordinal)) return [];
		var found = new List<(string, string)>();
		await Until(async () =>
		{
			var shown = await Fm.GetJson("shown", ("remap", "1"));
			if (shown.ValueKind != System.Text.Json.JsonValueKind.Array) return false;
			var rows = shown.EnumerateArray()
				.Where(t => Field(t, "Path") == "comp.TeamsBoardExpectationsInCompString" && Field(t, "Text").Length > 0 && Field(t, "Under").Length > 0)
				.ToList();
			if (rows.Count < wanted) return false;
			found = [];
			foreach (var row in rows)
				if (Field(await Fm.Pull($"{Field(row, "Under")}.comp", "Name"), "Name") is { Length: > 0 } name) found.Add((name, Field(row, "Text")));
			return found.Count == rows.Count;
		}, seconds: 10, every: 0.3);
		return found;
	}
}
