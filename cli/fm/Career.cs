using System.Text.Json;
using System.Text.RegularExpressions;
using Spectre.Console;

namespace FmCli;

/// <summary>
/// A player's career, season by season (FINDINGS §8.63): the "Career Stats" tile on his page
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
		// The tile arrives a moment after the page itself.
		var opened = "";
		await Until(async () =>
			!(opened = await Fm.Get("press", ("text", "Career Stats"))).StartsWith("error", StringComparison.Ordinal)
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

		var start = texts.IndexOf("Avg Rat") + 1;
		// The totals follow a "Total" label, which some pages draw with no width; then they are
		// the last five numbers, after the fees paid when there were any.
		var totalAt = texts.IndexOf("Total", Math.Max(start, 0));
		var labelled = totalAt >= 0;
		if (!labelled)
		{
			totalAt = Math.Max(start, texts.Count - 5);
			if (totalAt > start && Regex.IsMatch(texts[totalAt - 1], @"^[£€$]")) totalAt--;
		}
		var marks = Enumerable.Range(start, Math.Max(0, totalAt - start)).Where(i => SeasonMark.IsMatch(texts[i])).ToList();
		if (start <= 0 || marks.Count == 0)
		{
			Console.Error.WriteLine("Kariyer tablosu okunamadı.");
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
		var spent = totals.FirstOrDefault(t => t.StartsWith('£') || t.StartsWith('€') || t.StartsWith('$')) ?? "";
		var numbers = totals.Where(t => Regex.IsMatch(t, @"^[\d.]+$")).ToList();

		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(new { Name = Field(player, "Name"), Seasons = rows, Fees = spent, Totals = numbers }));
			return 0;
		}

		var name = Field(player, "Name");
		if (!Ui.Rich)
		{
			Console.WriteLine($"{name} — kariyer");
			Console.WriteLine($"SEZON\tTAKIM\tGELİŞ\tLİG\tMAÇ\t{(keeper ? "GOL YEMEDİ" : "GOL")}\tASİST\tMO\tPUAN");
			foreach (var r in rows) Console.WriteLine($"{r.Year}\t{r.Team}\t{How(r.How)}\t{r.Division}\t{r.Apps}\t{r.Goals}\t{r.Assists}\t{r.PoM}\t{r.Rating}");
			if (numbers.Count >= 2) Console.WriteLine($"Toplam: {numbers[0]} maç, {numbers[1]} {(keeper ? "gol yemeden maç" : "gol")}{(spent.Length > 0 ? $" · ödenen bonservisler {spent}" : "")}");
			return 0;
		}

		var table = new Spectre.Console.Table().Border(TableBorder.Rounded).BorderColor(Color.Grey)
			.Title($"[bold]{Ui.Safe(name)}[/] — kariyer")
			.AddColumn("Sezon").AddColumn("Takım").AddColumn("Geliş").AddColumn("Lig")
			.AddColumn(new TableColumn("Maç").RightAligned()).AddColumn(new TableColumn(keeper ? "Gol yemedi" : "Gol").RightAligned())
			.AddColumn(new TableColumn("Asist").RightAligned()).AddColumn(new TableColumn("Puan").RightAligned());
		string lastYear = null;
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
			table.Caption($"Toplam {numbers[0]} maç · {numbers[1]} {(keeper ? "gol yemeden maç" : "gol")}{(numbers.Count > 2 ? $" · {numbers[2]} asist" : "")}{(spent.Length > 0 ? $" · ödenen bonservisler {spent}" : "")}");
		Ui.Out.Write(table);
		return 0;

		static string How(string how) => how switch { "Loan" => "kiralık", "Free" => "bedelsiz", "Undisc." => "açıklanmadı", "" => "", _ => how };
	}
}
