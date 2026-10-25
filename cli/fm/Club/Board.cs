using System.Text.Json;

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

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				// The record answers in English where the game has no stored translation; the same
				// words the line path puts back at write time are put back here (Board, Turkish game).
				Board = BoardAreas.ToDictionary(a => a.Label, a => GameWords.Of(Field(said, a.Field))), BoardPraise = praise, BoardWorries = worries,
				Supporters = SupporterAreas.ToDictionary(a => a.Label, a => GameWords.Of(Field(said, a.Field))), SupporterPraise = fansPraise, SupporterWorries = fansWorries,
			}));
			return 0;
		}

		if (BoardAreas.All(a => Field(said, a.Field).Length == 0))
		{
			Console.Error.WriteLine(L("The board's view could not be read."));
			return 1;
		}

		Block(L("Board"), BoardAreas, praise, worries);
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
}
