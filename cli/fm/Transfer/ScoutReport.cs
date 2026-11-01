using System.Text.Json;

namespace FmCli;

/// <summary>
/// The scouts' report on a player: who watched him last, what they make of his ability and
/// potential, how keen he is on a move, what he would cost, the points for and against, and
/// where he would rank in our squad at his position.
/// </summary>
internal static partial class Program
{
	/// <summary>
	/// The report is the player's own record for our team (`PlayerReportForContextTeam`), asked with
	/// the variables its page lays beside him: `team` and `human` for whom it is written, and
	/// `InPossession` for the squad comparison. Nothing is opened or recognised by its words or by
	/// where it stands on the page; every value comes in the game's words.
	/// </summary>
	private static async Task<int> PlayerReport(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		var path = Field(card.GetProperty("Player"), "Path");
		const string at = "fmcli.report.player";
		// A variable is bound by this very call when nothing has bound it yet: then a frame later.
		(string To, string Kind, string Value)[] context =
			[(at, "from", path), ($"{at}.team", "from", "Human.Team"), ($"{at}.human", "from", "Human"), ($"{at}.InPossession", "int", "1")];
		foreach (var (to, kind, value) in context)
			if (path.Length == 0 || !await Until(async () => !(await Fm.Get("pathset", ("to", to), (kind, value))).StartsWith("error", StringComparison.Ordinal), seconds: 10, every: 0.2))
			{
				Console.Error.WriteLine(L($"{name}: the scout report did not open."));
				return 1;
			}

		const string report = $"{at}.PlayerReportForContextTeam";
		const string words = "ScoutedReportBasicInfo.ReportProvider.Name,ScoutedReportBasicInfo.ReportProvider.JobString,"
			+ "ScoutedGradeInfo.ShortGradeString,ScoutedGradeInfo.GradeString,ScoutedCurrentAbilityInfo.ShortString,ScoutedPotentialAbilityInfo.ShortString,"
			+ "ScoutedTransferInfo.ExpectedTransferInterest.ShortString,ScoutedTransferInfo.ExpectedLoanInterest.ShortString,"
			+ "ScoutedTransferInfo.ExpectedTransferValue.ShortString,ScoutedTransferInfo.ExpectedWageDemands.ShortString,"
			+ "WatchedMatchesInfo.WatchedMatchesSummary,BestRoleForPosition,ReportSelectedPosition.DisplayValue";
		var read = await Fm.Pull(report, $"HasScoutedInfo,{words}", shown: words);
		// One of our own players has no scouted report, nor has one nobody has watched.
		if (Field(read, "HasScoutedInfo") != "True" || Field(read, "ScoutedGradeInfo.GradeString").Length == 0)
		{
			Console.Error.WriteLine(L($"There is no scout report on {name} (one of our own players, or never scouted). To have them scouted: scout player \"{name}\" --for <length>"));
			return 1;
		}

		async Task<List<string>> Points(string list) =>
			(await Fm.Pull($"{report}.ScoutedProConsList.{list}", "ProConsLongString", list: true, shown: "ProConsLongString")).EnumerateArray()
				.Select(p => Field(p, "ProConsLongString").Replace("\n", " — ")).Where(p => p.Length > 0).ToList();
		var pros = await Points("ProsList");
		var cons = await Points("ConsList");
		// The squad comparison: our players at his position, ranked, with him among them.
		var ranks = (await Fm.Pull($"{report}.ScoutedSquadComparisonPlayers", "Ranking,Player.Name", list: true)).EnumerateArray()
			.Select(r => (Rank: Field(r, "Ranking"), Player: Field(r, "Player.Name"))).Where(r => r.Player.Length > 0).ToList();
		var position = Field(read, "ReportSelectedPosition.DisplayValue");

		var provider = Field(read, "ScoutedReportBasicInfo.ReportProvider.Name");
		var by = provider.Length == 0 ? "" : $"{provider} ({Field(read, "ScoutedReportBasicInfo.ReportProvider.JobString")})";
		var shown = new
		{
			Grade = Field(read, "ScoutedGradeInfo.ShortGradeString"), Recommendation = Field(read, "ScoutedGradeInfo.GradeString"),
			CurrentAbility = Field(read, "ScoutedCurrentAbilityInfo.ShortString"), PotentialAbility = Field(read, "ScoutedPotentialAbilityInfo.ShortString"),
			TransferInterest = Field(read, "ScoutedTransferInfo.ExpectedTransferInterest.ShortString"), LoanInterest = Field(read, "ScoutedTransferInfo.ExpectedLoanInterest.ShortString"),
			Value = Field(read, "ScoutedTransferInfo.ExpectedTransferValue.ShortString"), Wage = Field(read, "ScoutedTransferInfo.ExpectedWageDemands.ShortString"),
			MatchesWatched = Field(read, "WatchedMatchesInfo.WatchedMatchesSummary"), BestRole = Field(read, "BestRoleForPosition"),
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Name = name, LastScoutedBy = by, shown.Grade, shown.Recommendation,
				shown.CurrentAbility, shown.PotentialAbility,
				shown.TransferInterest, shown.LoanInterest, shown.Value, shown.Wage,
				Pros = pros, Cons = cons, shown.MatchesWatched, shown.BestRole,
				SquadComparison = new { Position = position, Ranks = ranks.Select(r => new { r.Rank, r.Player }) },
			}));
			return 0;
		}

		Console.WriteLine(L($"{name} — scout report{(by.Length > 0 ? L($" · last scouted by {by}") : "")}"));
		// An own player's grade and recommendation are one sentence: said once.
		Console.WriteLine(shown.Grade == shown.Recommendation ? L($"  Grade: {shown.Grade}") : L($"  Grade: {shown.Grade} — {shown.Recommendation}"));
		Console.WriteLine(L($"  Ability: {shown.CurrentAbility} · Potential: {shown.PotentialAbility}"));
		Console.WriteLine(L($"  Interest in a transfer: {shown.TransferInterest} · in a loan: {shown.LoanInterest}"));
		Console.WriteLine(L($"  Value: {shown.Value} · expected wage: {shown.Wage}"));
		if (shown.BestRole.Length > 0) Console.WriteLine(L($"  Best role: {shown.BestRole} · matches watched: {shown.MatchesWatched}"));
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
