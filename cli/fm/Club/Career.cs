using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// A player's career, season by season: the "Career Stats" tile on his page
/// opens a table read as plain text — year, club, how he came (fee, loan, free), nation,
/// division, appearances, goals, assists, player of the match, average rating — and the
/// career totals after the last row.
/// </summary>
internal static partial class Program
{
	private sealed record CareerRow(string Year, string Team, string How, string Nation, string Division, string Apps, string Goals, string Assists, string PoM, string Rating);

	private static readonly Regex SeasonMark = new(@"^\d{4}(/\d{2})?$");

	private static async Task<int> Career(JsonElement card, bool json)
	{
		var player = card.GetProperty("Player");
		// The player's page remembers the tab it was left on; on the career tab there is no
		// tile to press, the table is already there.
		// The tile arrives a moment after the page itself. It is found by the panel it is laid
		// out in ("CareerStats"), whose name no language changes, not by its title.
		var opened = "";
		await Until(async () =>
			!(opened = await Fm.Get("click", ("within", "CareerStats"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal)
			|| (await Fm.GetJson("texts", ("anchor", "Total Apps"), ("up", "0"))).GetArrayLength() > 0, seconds: 8, every: 0.5);
		if (opened.StartsWith("error", StringComparison.Ordinal)
		    && (await Fm.GetJson("texts", ("anchor", "Total Apps"), ("up", "0"))).GetArrayLength() == 0)
			return Report(opened);

		// The table fills over a few frames, and the previous player's can still be showing:
		// it is taken once two readings in a row agree.
		List<string> texts = [];
		var previous = "";
		await Until(async () =>
		{
			texts = (await Fm.GetJson("texts", ("anchor", "Total Apps"), ("up", "16"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();
			var now = string.Join("|", texts);
			var settled = now == previous && texts.Any(t => SeasonMark.IsMatch(t));
			previous = now;
			return settled;
		}, seconds: 10, every: 0.6);

		// The rows are found by their own first cell, a season ("2026/27"); the header above them is
		// the game's words and differs by language (an English game's "Avg Rat" comes back
		// "Avg. Rating" through a Turkish one), so nothing above is read.
		var start = texts.FindIndex(SeasonMark.IsMatch);
		// The totals follow a "Total" label, which some pages draw with no width; then they are
		// the last five numbers, after the fees paid when there were any.
		var totalAt = texts.IndexOf("Total", Math.Max(start, 0));
		var labelled = totalAt >= 0;
		if (!labelled)
		{
			totalAt = Math.Max(start, texts.Count - 5);
			if (totalAt > start && MoneyFormat.IsMoney(texts[totalAt - 1])) totalAt--;
		}
		var marks = Enumerable.Range(start, Math.Max(0, totalAt - start)).Where(i => SeasonMark.IsMatch(texts[i])).ToList();
		if (start < 0 || marks.Count == 0)
		{
			Console.Error.WriteLine(L("The career table could not be read."));
			return 1;
		}

		// A goalkeeper's second column is clean sheets, not goals.
		var keeper = texts.Contains("Cln Sh");

		// Each row ends in the same seven cells — nation, division, appearances, goals, assists,
		// player of the match, rating — and starts with the year and the club; whatever is left
		// in between says how he came.
		var rows = new List<CareerRow>();
		for (var k = 0; k < marks.Count; k++)
		{
			var end = k + 1 < marks.Count ? marks[k + 1] : totalAt;
			var cells = texts.Skip(marks[k]).Take(end - marks[k]).ToList();
			if (cells.Count < 9) continue;
			var tail = cells.Skip(cells.Count - 7).ToList();
			var middle = cells.Skip(1).Take(cells.Count - 8).ToList();
			rows.Add(new(cells[0], middle[0], middle.Count > 1 ? middle[^1] : "", tail[0], tail[1], tail[2], tail[3], tail[4], tail[5], tail[6]));
		}

		// After "Total": the fees paid for him in all, then the career's own numbers.
		var totals = texts.Skip(labelled ? totalAt + 1 : totalAt).ToList();
		var spent = totals.FirstOrDefault(MoneyFormat.IsMoney) ?? "";
		var numbers = totals.Where(t => Regex.IsMatch(t, @"^[\d.]+$")).ToList();

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

		static string How(string how) => how switch { "Loan" => L("loan"), "Free" => L("free"), "Undisc." => L("undisclosed"), "" => "", _ => how };
	}
}
