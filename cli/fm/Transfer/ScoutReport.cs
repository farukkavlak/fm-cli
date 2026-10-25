using System.Text.Json;

namespace FmCli;

/// <summary>
/// The scouts' report on a player: the tile on his page that carries their
/// grade opens it — who watched him last, what they make of his ability and potential, how
/// keen he is on a move, what he would cost, the points for and against, and where he would
/// rank in our squad at his position.
/// </summary>
internal static partial class Program
{
	private static async Task<int> PlayerReport(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		// The report read by each text's data path, from its scout's name.
		async Task<List<(string Text, string Path)>> Read() =>
			(await Fm.GetJson("texts", ("anchorpath", "report.ScoutedReportBasicInfo.ReportProvider.Name"), ("up", "16"), ("paths", "1"), ("most", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
		static bool Whole(List<(string Text, string Path)> t) => t.Any(x => x.Path == "report.ScoutedGradeInfo.GradeString");

		// The page remembers the tab it was left on, so the report may already be showing; else the
		// tile that opens it (laid out in player-recommendation-cell-remapper) arrives a moment after
		// the page. One of our own players has no such tile, nor has one nobody has watched.
		List<(string Text, string Path)> texts = [];
		if (!Whole(await Read()))
		{
			var clicked = "";
			await Until(async () => !(clicked = await Fm.Get("click", ("within", "player-recommendation-cell-remapper"), ("name", "SIButton"))).StartsWith("error", StringComparison.Ordinal), seconds: 5, every: 0.5);
			if (clicked.StartsWith("error", StringComparison.Ordinal))
			{
				Console.Error.WriteLine(L($"There is no scout report on {name} (one of our own players, or never scouted). To have them scouted: scout player \"{name}\" --for <length>"));
				return 1;
			}
		}
		var previous = "";
		await Until(async () =>
		{
			texts = await Read();
			var now = string.Join("|", texts.Select(t => t.Text));
			var settled = now == previous && Whole(texts);
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (!Whole(texts))
		{
			Console.Error.WriteLine(L($"{name}: the scout report did not open."));
			return 1;
		}

		string Bound(string path) => texts.FirstOrDefault(t => t.Path == path).Text ?? "";
		// The few values drawn without a path follow their labels, which are the game's own texts.
		string After(string label) => texts.FindIndex(t => t.Text == label) is var at && at >= 0 && at + 1 < texts.Count ? texts[at + 1].Text : "";
		var grade = texts.FindIndex(t => t.Path == "report.ScoutedGradeInfo.GradeString") is > 0 and var g && texts[g - 1].Path.Length == 0 ? texts[g - 1].Text : "";
		var provider = Bound("report.ScoutedReportBasicInfo.ReportProvider.Name");
		var by = provider.Length == 0 ? "" : $"{provider} ({Bound("report.ScoutedReportBasicInfo.ReportProvider.JobString")})";
		var negatives = texts.FindIndex(t => t.Text == "Negatives");
		var points = texts.Select((t, at) => (t.Text, t.Path, At: at)).Where(t => t.Path == "binding.ProConsLongString").ToList();
		var pros = points.Where(p => negatives < 0 || p.At < negatives).Select(p => p.Text.Replace("\n", " — ")).ToList();
		var cons = points.Where(p => negatives >= 0 && p.At > negatives).Select(p => p.Text.Replace("\n", " — ")).ToList();
		// The squad comparison: rank and player in pairs, under a heading naming the position.
		var values = texts.Where(t => t.Path == "Value").Select(t => t.Text).ToList();
		var ranks = new List<(string Rank, string Player)>();
		for (var i = 0; i + 1 < values.Count; i += 2) ranks.Add((values[i], values[i + 1]));
		var firstValue = texts.FindIndex(t => t.Path == "Value");
		var comparison = firstValue > 0 ? texts.Take(firstValue).LastOrDefault(t => t.Path.Length == 0 && t.Text.Contains(" - ")).Text : null;
		var position = comparison?[(comparison.LastIndexOf(" - ", StringComparison.Ordinal) + 3)..];
		var report = new
		{
			Grade = grade, Recommendation = Bound("report.ScoutedGradeInfo.GradeString"),
			CurrentAbility = Bound("report.ScoutedCurrentAbilityInfo.ShortString"), PotentialAbility = Bound("report.ScoutedPotentialAbilityInfo.ShortString"),
			TransferInterest = After("Transfer Interest"), LoanInterest = After("Loan Interest"),
			Value = Bound("report.ScoutedTransferInfo.ExpectedTransferValue.ShortString"), Wage = Bound("report.ScoutedTransferInfo.ExpectedWageDemands.ShortString"),
			MatchesWatched = Bound("watchInfo.WatchedMatchesSummary"), BestRole = Bound("report.BestRoleForPosition"),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Name = name, LastScoutedBy = by, report.Grade, report.Recommendation,
				report.CurrentAbility, report.PotentialAbility,
				report.TransferInterest, report.LoanInterest, report!.Value, report.Wage,
				Pros = pros, Cons = cons, report.MatchesWatched, report.BestRole,
				SquadComparison = new { Position = position, Ranks = ranks.Select(r => new { r.Rank, r.Player }) },
			}));
			return 0;
		}

		Console.WriteLine(L($"{name} — scout report{(by.Length > 0 ? L($" · last scouted by {by}") : "")}"));
		Console.WriteLine(L($"  Grade: {report.Grade} — {report.Recommendation}"));
		Console.WriteLine(L($"  Ability: {report.CurrentAbility} · Potential: {report.PotentialAbility}"));
		Console.WriteLine(L($"  Interest in a transfer: {report.TransferInterest} · in a loan: {report.LoanInterest}"));
		Console.WriteLine(L($"  Value: {report!.Value} · expected wage: {report.Wage}"));
		if (report.BestRole.Length > 0) Console.WriteLine(L($"  Best role: {report.BestRole} · matches watched: {report.MatchesWatched}"));
		Console.WriteLine(L("Pros:"));
		foreach (var line in pros) Console.WriteLine($"  + {line}");
		Console.WriteLine(L("Cons:"));
		foreach (var line in cons) Console.WriteLine($"  - {line}");
		if (ranks.Count > 0)
		{
			Console.WriteLine(L($"Place in the squad ({position}):"));
			foreach (var (rank, player) in ranks) Console.WriteLine($"  {rank,2}  {player}{(Fold(player) == Fold(name) ? "  ←" : "")}");
		}
		return 0;
	}
}
