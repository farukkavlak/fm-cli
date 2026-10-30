using System.Text.Json;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// A player's career, season by season, from his own record (CareerStatsPerSeason) — year,
/// club, how he came (fee, loan, free), nation, division, appearances, goals or clean sheets —
/// with the league's assists, player of the match and average rating (SeasonStatLeagueOnly).
/// No page or tile is opened: the player's page only names whose record it is.
/// </summary>
internal static partial class Program
{
	private sealed record CareerRow(string Year, string Team, string How, string Nation, string Division, string Apps, string Goals, string Assists, string PoM, string Rating);

	private static async Task<int> Career(JsonElement card, bool json)
	{
		var player = card.GetProperty("Player");
		var path = Field(player, "Path");
		if (path.Length == 0)
		{
			Console.Error.WriteLine(L("The career table could not be read."));
			return 1;
		}

		var keeper = Field((await PullEach([(path, "IsGoalkeeper")]))[0], "IsGoalkeeper") == "True";
		List<JsonElement> seasons = [];
		await Until(async () =>
		{
			seasons = (await Fm.Pull($"{path}.CareerStatsPerSeason", "SeasonYearLong,Team.Name,SeasonTransferInfo,Nation.Name,Division.Name,Appearances,Goals,CleanSheets", list: true, shown: "SeasonTransferInfo")).EnumerateArray().ToList();
			return seasons.Count > 0 && seasons.All(r => Field(r, "SeasonYearLong").Length > 0);
		}, seconds: 8, every: 0.3);
		if (seasons.Count == 0)
		{
			Console.Error.WriteLine(L("The career table could not be read."));
			return 1;
		}
		var league = await PullEach(seasons.Select(r => ($"{Field(r, "Row")}.SeasonStatLeagueOnly", "Assists,PlayerOfTheMatchAwards,AverageRating")).ToList(),
			complete: r => r.EnumerateObject().Any());
		var rows = seasons.Zip(league).Select(x => new CareerRow(Field(x.First, "SeasonYearLong"), Field(x.First, "Team.Name"), Field(x.First, "SeasonTransferInfo"),
			Field(x.First, "Nation.Name"), Field(x.First, "Division.Name"), Field(x.First, "Appearances"), Field(x.First, keeper ? "CleanSheets" : "Goals"),
			Field(x.Second, "Assists"), Field(x.Second, "PlayerOfTheMatchAwards"), Field(x.Second, "AverageRating"))).ToList();

		static int Count(string n) => int.TryParse(n, out var v) ? v : 0;
		var spent = Field((await PullShown([(path, "CareerTransferFeesTotal")]))[0], "CareerTransferFeesTotal");
		var numbers = new List<string> { rows.Sum(r => Count(r.Apps)).ToString(), rows.Sum(r => Count(r.Goals)).ToString(), rows.Sum(r => Count(r.Assists)).ToString() };

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = Field(player, "Name"), Seasons = rows, Fees = spent, Totals = numbers }));
			return 0;
		}

		var name = Field(player, "Name");
		if (!Ui.Rich)
		{
			Console.WriteLine(L($"{name} — career"));
			Console.WriteLine(L($"SEASON\tTEAM\tJOINED\tLEAGUE\tAPPS\t{(keeper ? L("CLEAN SHEETS") : L("GOALS"))}\tASSISTS\tPOM\tRATING"));
			foreach (var r in rows) Console.WriteLine($"{r.Year}\t{r.Team}\t{How(r.How)}\t{r.Division}\t{r.Apps}\t{r.Goals}\t{r.Assists}\t{r.PoM}\t{r.Rating}");
			if (numbers.Count >= 2) Console.WriteLine(L($"Total: {numbers[0]} apps, {numbers[1]} {(keeper ? L("clean sheets") : L("goals"))}{(spent.Length > 0 ? L($" · transfer fees paid {spent}") : "")}"));
			return 0;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
			.Title(L($"[bold]{Ui.Safe(name)}[/] — career"))
			.AddColumn(L("Season")).AddColumn(L("Team")).AddColumn(L("Joined")).AddColumn(L("League"))
			.AddColumn(new TableColumn(L("Apps")).RightAligned()).AddColumn(new TableColumn(keeper ? L("Clean sheets") : L("Goals")).RightAligned())
			.AddColumn(new TableColumn(L("Assists")).RightAligned()).AddColumn(new TableColumn(L("Rating")).RightAligned());
		string? lastYear = null;
		foreach (var r in rows)
		{
			// One season can hold two clubs: the year is said once.
			var year = r.Year == lastYear ? "" : r.Year;
			lastYear = r.Year;
			var played = r.Apps is "0" or "-";
			string Dim(string text) => played ? $"[dim]{Ui.Safe(text)}[/]" : Ui.Safe(text);
			table.AddRow(Dim(year), played ? Dim(r.Team) : $"[bold]{Ui.Safe(r.Team)}[/]", Dim(How(r.How)), Dim($"{r.Division} ({r.Nation})"),
				Dim(r.Apps), Dim(r.Goals), Dim(r.Assists), Dim(r.Rating));
		}
		if (numbers.Count >= 2)
			table.Caption(L($"Total {numbers[0]} apps · {numbers[1]} {(keeper ? L("clean sheets") : L("goals"))}{(numbers.Count > 2 ? L($" · {numbers[2]} assists") : "")}{(spent.Length > 0 ? L($" · transfer fees paid {spent}") : "")}"));
		Ui.Out.Write(table);
		return 0;

		// How he came is the game's own short word ("Loan", "Free", a fee), in its language.
		static string How(string how) => GameWords.Of(how);
	}
}
