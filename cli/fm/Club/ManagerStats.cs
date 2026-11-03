using System.Text.Json;

namespace FmCli;

/// <summary>
/// The manager's own record (Career › Managerial Stats): clubs and nations managed, time at clubs,
/// transfers, and the results at this club and over the whole career. Each figure is read by the data
/// path the page binds it to (Person.TotalGamesWon …), as the game writes it; the CLI names it.
/// </summary>
internal static partial class Program
{
	private static readonly (string Path, string Say)[] ManagerFacts =
	[
		("Person.NumberOfClubsManaged", L("Clubs managed")), ("Person.NumberOfNationsManaged", L("National teams managed")),
		("Person.LongestTimeAtClub", L("Longest at a club")), ("Person.ShortestTimeAtClub", L("Shortest at a club")),
		("Person.NumberOfAwards", L("Awards")),
	];

	private static readonly (string Path, string Say)[] ManagerTransfers =
	[
		("Person.NumberOfPlayersBought", L("Players bought")), ("Person.TotalTransferFeesOut", L("Spent on fees")),
		("Person.NumberOfPlayersSold", L("Players sold")), ("Person.TotalTransferFeesIn", L("Received in fees")),
		("Person.HighestFeeSpent", L("Highest fee paid")), ("Person.HighestFeeReceived", L("Highest fee received")),
		("Person.TotalAgentFees", L("Paid to agents")), ("Person.NumberOfPlayersReleased", L("Players released")),
	];

	// The results, this club's and the whole career's side by side: (what, this club's path, the career's path).
	private static readonly (string Say, string Club, string Career)[] ManagerResults =
	[
		(L("Played"), "Person.CurrentClubTotalGamesPlayed", "Person.TotalGamesPlayed"),
		(L("Won"), "Person.CurrentClubTotalGamesWon", "Person.TotalGamesWon"),
		(L("Drawn"), "Person.CurrentClubTotalGamesDrawn", "Person.TotalGamesDrawn"),
		(L("Lost"), "Person.CurrentClubTotalGamesLost", "Person.TotalGamesLost"),
		(L("Goals for"), "Person.CurrentClubTotalGoalsFor", "Person.TotalGoalsFor"),
		(L("Goals against"), "Person.CurrentClubTotalGoalsAgainst", "Person.TotalGoalsAgainst"),
		(L("Goal difference"), "Person.CurrentClubTotalGoalDifference", "Person.TotalGoalDifference"),
		(L("Win %"), "Person.CurrentClubTotalGamesWonRatio", "Person.TotalGamesWonRatio"),
		(L("Cups"), "Person.CurrentClubTotalCupWins", "Person.TotalCupsWins"),
		(L("League titles"), "Person.CurrentClubTotalLeagueWins", "Person.ClubTotalLeagueWins"),
		(L("Promotions"), "Person.CurrentClubTotalNoPromotions", "Person.ClubTotalNoPromotions"),
		(L("Relegations"), "Person.CurrentClubTotalNoRelegations", "Person.ClubTotalNoRelegations"),
		(L("Days in charge"), "Person.CurrentClubDaysInCharge", ""),
	];

	private static async Task<int> ManagerStats(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		// Every figure the page shows, by its path; read once two readings agree (the page fills in).
		Dictionary<string, string> figures = [];
		async Task<Dictionary<string, string>> Read()
		{
			try
			{
				return (await Fm.GetJson("texts", ("anchorpath", "Person.NumberOfClubsManaged"), ("up", "14"), ("paths", "1"))).EnumerateArray()
					.Where(t => Field(t, "Path").StartsWith("Person.", StringComparison.Ordinal))
					.GroupBy(t => Field(t, "Path")).ToDictionary(g => g.Key, g => Field(g.First(), "Text"));
			}
			catch (FmError) { return []; }
		}
		var opened = await OpenSubpage(Screen.Career, Screen.ManagerialStats, async () => (await Read()).Count > 0);
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);
		var previous = "";
		await Until(async () =>
		{
			figures = await Read();
			var now = string.Join("|", figures.OrderBy(f => f.Key).Select(f => f.Value));
			var settled = figures.Count > 0 && now == previous;
			previous = now;
			return settled;
		}, seconds: 8, every: 0.4);
		if (figures.Count == 0)
		{
			Console.Error.WriteLine(L("The managerial stats page could not be read."));
			return 1;
		}
		string Of(string path) => path.Length > 0 && figures.TryGetValue(path, out var v) ? v : "";

		if (json)
		{
			Console.WriteLine(JsonOut.Of(figures.ToDictionary(f => f.Key["Person.".Length..], f => f.Value)));
			return 0;
		}
		var club = Field(await Fm.Pull("Human.Team", "Name"), "Name");
		Table.Write([L("MANAGER"), ""], ManagerFacts.Select(f => new[] { f.Say, Of(f.Path) }).ToList());
		Console.WriteLine();
		Table.Write([L("TRANSFERS"), ""], ManagerTransfers.Select(f => new[] { f.Say, Of(f.Path) }).ToList());
		Console.WriteLine();
		Table.Write(["", club.Length > 0 ? club : L("THIS CLUB"), L("CAREER")],
			ManagerResults.Select(r => new[] { r.Say, Of(r.Club), Of(r.Career) }).ToList());
		return 0;
	}
}
