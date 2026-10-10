using System.Text.Json;

namespace FmCli;

/// <summary>
/// What the board and the supporters think of the manager (FINDINGS §8.81), read from the team's
/// own confidence record with no screen: a sentence for each thing they judge (results, the
/// squad's backing, tactics, transfers, the club's vision and culture, the competitions) and the
/// lists of what pleased and what worried them.
/// </summary>
internal static partial class Program
{
	private static readonly (string Label, string Field)[] BoardAreas =
	[
		("Genel", "BoardConfidenceOverallString"), ("Maçlar", "BoardMatchString"), ("Kadro", "BoardSquadConfidenceString"),
		("Taktik", "BoardTacticsString"), ("Transfer", "BoardTransferConfidenceOverallSummaryText"), ("Vizyon", "BoardVisionString"),
		("Kültür", "BoardCultureProgressString"), ("Turnuvalar", "InternationalCompConfidenceStringLong"),
	];

	private static readonly (string Label, string Field)[] SupporterAreas =
	[
		("Genel", "SupporterConfidenceOverallString"), ("Maçlar", "SupporterMatchString"), ("Taktik", "SupporterTacticsString"),
		("Transfer", "SupporterTransferConfidenceOverallSummaryText"), ("Vizyon", "SupporterVisionString"),
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
				Board = BoardAreas.ToDictionary(a => a.Label, a => Field(said, a.Field)), BoardPraise = praise, BoardWorries = worries,
				Supporters = SupporterAreas.ToDictionary(a => a.Label, a => Field(said, a.Field)), SupporterPraise = fansPraise, SupporterWorries = fansWorries,
			}));
			return 0;
		}

		if (BoardAreas.All(a => Field(said, a.Field).Length == 0))
		{
			Console.Error.WriteLine("Yönetimin görüşü okunamadı.");
			return 1;
		}

		Block("Yönetim", BoardAreas, praise, worries);
		Console.WriteLine();
		Block("Taraftarlar", SupporterAreas, fansPraise, fansWorries);
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

			Points("Memnun oldukları", good, "green", "+");
			Points("Endişeleri", bad, "red", "-");
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
