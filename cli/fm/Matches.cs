using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace FmCli;

/// <summary>
/// A player's season match by match: the Match Ratings page his form tile opens. Each row is
/// the game's number, date, position, role, opponent, score, when he came on or off, xG,
/// assists, goals and rating — for a goalkeeper xG against and goals conceded instead (his
/// clean-sheet column is an icon, so it is told by none conceded); above them his games, best,
/// worst and average rating.
/// </summary>
internal static partial class Program
{
	private sealed record MatchRow(int Number, string Date, string Position, string Role, string Opponent, string Score, string OnOff, string Xg, string Assists, string Goals, string Conceded, string Rating);

	private static async Task<int> PlayerMatches(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		async Task<List<string>> Texts() => (await Fm.GetJson("texts", ("anchor", "All Competitions"), ("up", "10"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();

		// The page stays loaded beneath once opened, so another player's can still be read.
		var before = string.Join("|", await Texts());
		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("text", "Last 5 Club Games"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine($"{name} için form kutusu yok (bu sezon maçı olmayabilir).");
			return 1;
		}
		List<string> texts = [];
		var previous = "";
		var reads = 0;
		await Until(async () =>
		{
			texts = await Texts();
			reads++;
			var now = string.Join("|", texts);
			var settled = texts.Contains("Match Ratings") && texts.Contains("Inf") && now == previous && (now != before || reads >= 6);
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (!texts.Contains("Inf"))
		{
			Console.Error.WriteLine($"{name}: maç puanları sayfası açılmadı.");
			return 1;
		}

		string After(string label) => texts.IndexOf(label) is var i && i >= 0 && i + 1 < texts.Count ? texts[i + 1] : "";
		var summary = new { Apps = After("Apps"), Best = After("Max. Rating"), Worst = After("Min. Rating"), Average = After("Average Rating"), Goals = After("Goals Scored"), Assists = After("Assists") };

		// The list lays out only the rows in view; the rest come in as it is scrolled.
		var rows = new Dictionary<int, MatchRow>();
		foreach (var row in Rows(texts)) rows[row.Number] = row;
		if (int.TryParse(summary.Apps, out var apps) && rows.Count < apps && rows.Count > 0)
		{
			foreach (var at in new[] { "0.5", "1" })
			{
				await Fm.Get("scroll", ("anchor", rows.Values.First().Date), ("to", at));
				await Task.Delay(500);
				foreach (var row in Rows(await Texts())) rows.TryAdd(row.Number, row);
			}
			await Fm.Get("scroll", ("anchor", rows.Values.First().Date), ("to", "0"));
		}
		var all = rows.Values.OrderByDescending(r => r.Number).ToList();
		var keeper = texts.Contains("xGA");

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Goalkeeper = keeper, Summary = summary, Matches = all }));
			return 0;
		}

		var cleanSheets = all.Count(r => r.Conceded == "0" && r.Position != "-");
		Console.WriteLine($"{name} — {summary.Apps} maç · ortalama {summary.Average} · en iyi {summary.Best} · en kötü {summary.Worst} · "
			+ (keeper ? $"{cleanSheets} gol yemediği maç" : $"{summary.Goals} gol · {summary.Assists} asist"));
		var subs = all.Any(r => r.OnOff.Length > 0);
		var rich = Ui.Rich;

		// His ratings as a row of bars, oldest first, on the scale ratings mostly live on.
		var ratings = all.AsEnumerable().Reverse()
			.Select(r => double.TryParse(r.Rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var rating) ? rating : (double?)null)
			.Where(r => r != null).Select(r => r.Value).ToList();
		if (ratings.Count > 1)
		{
			var bars = ratings.Select(r => (Bar: Ui.Bar(r, 5.5, 9), Rating: r)).ToList();
			if (rich) Ui.Line($"[dim]puan[/]  {string.Concat(bars.Select(b => b.Rating >= 7.5 ? $"[green]{b.Bar}[/]" : b.Rating < 6.5 ? $"[red]{b.Bar}[/]" : b.Bar.ToString()))}  [dim]eski → yeni[/]");
			else Console.WriteLine($"puan  {string.Concat(bars.Select(b => b.Bar))}  eski → yeni");
		}
		// The page gives the score home side first and does not say which side he was on.
		var heads = new List<string> { "#", "TARİH", "RAKİP", "SKOR (EV-DEP)", "MEVKİ" };
		if (subs) heads.Add(rich ? "" : "GİRDİ/ÇIKTI");
		heads.AddRange(keeper ? ["xGA", "YEDİĞİ"] : ["xG", "GOL", "AS"]);
		heads.Add("PUAN");
		List<string> Cells(MatchRow r)
		{
			var cells = new List<string>
			{
				rich ? $"[dim]{r.Number}[/]" : r.Number.ToString(CultureInfo.InvariantCulture), rich ? Short(r.Date) : r.Date,
				rich ? Ui.Safe(r.Opponent) : r.Opponent, r.Score, rich ? $"{r.Position} [dim]{r.Role}[/]" : $"{r.Position} {r.Role}",
			};
			if (subs) cells.Add(rich ? $"[dim]{OnOff(r.OnOff)}[/]" : OnOff(r.OnOff));
			if (keeper) cells.AddRange([r.Xg, rich && r.Conceded == "0" ? "[green bold]0[/]" : r.Conceded]);
			else cells.AddRange(rich ? [r.Xg, Count(r.Goals, "green"), Count(r.Assists, "cyan")] : [r.Xg, r.Goals, r.Assists]);
			cells.Add(rich ? Colour(r.Rating) : r.Rating);
			return cells;
		}
		if (rich)
		{
			var table = new Spectre.Console.Table { Border = Spectre.Console.TableBorder.Rounded, BorderStyle = new Spectre.Console.Style(Spectre.Console.Color.Grey) };
			foreach (var head in heads) table.AddColumn(new Spectre.Console.TableColumn($"[dim]{head}[/]"));
			foreach (var r in all) Spectre.Console.TableExtensions.AddRow(table, Cells(r).ToArray());
			Ui.Out.Write(table);
		}
		else
			Table.Write(heads, all.Select(r => Cells(r).ToArray()).ToList());
		return 0;

		static string Short(string date) => DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
			? $"{d.Day} {Months[d.Month - 1][..3]}" : date;
		// "on 75" is a substitute's minute on, "off 60" the minute he went off.
		static string OnOff(string text) => Regex.Replace(text, @"^(on|off) (\d+)$", m => $"{m.Groups[2].Value}' {(m.Groups[1].Value == "on" ? "girdi" : "çıktı")}");
		static string Count(string n, string colour) => n is "" or "0" ? "[dim]0[/]" : $"[{colour} bold]{n}[/]";
		static string Colour(string rating) => double.TryParse(rating, NumberStyles.Float, CultureInfo.InvariantCulture, out var r)
			? r >= 7.5 ? $"[green bold]{rating}[/]" : r < 6.5 ? $"[red]{rating}[/]" : rating : rating;
	}

	/// <summary>
	/// The table's rows out of the page's texts: each starts with the game's number and its date,
	/// and ends with xG, assists, goals and rating (a keeper's xG against, goals conceded and
	/// rating); a substitute's minute sits before the xG.
	/// </summary>
	private static IEnumerable<MatchRow> Rows(List<string> texts)
	{
		var start = texts.IndexOf("Inf");
		if (start < 0) yield break;
		var keeper = texts.Contains("xGA");
		var cells = texts.Skip(start + 1).SkipWhile(t => t != "Rating").Skip(1).ToList();
		static bool Date(string t) => Regex.IsMatch(t, @"^\d{2}/\d{2}/\d{4}$");
		var starts = Enumerable.Range(0, cells.Count - 1).Where(i => Regex.IsMatch(cells[i], @"^\d+$") && Date(cells[i + 1])).ToList();
		for (var s = 0; s < starts.Count; s++)
		{
			var row = cells.Skip(starts[s]).Take((s + 1 < starts.Count ? starts[s + 1] : cells.Count) - starts[s]).ToList();
			if (row.Count < 7) continue;
			// Whatever the page shows after the table is not the last row's.
			var onOff = Regex.IsMatch(row[6], @"^(on|off) \d+$");
			var size = 6 + (onOff ? 1 : 0) + (keeper ? 3 : 4);
			if (row.Count < size) continue;
			row = row.Take(size).ToList();
			yield return keeper
				? new MatchRow(int.Parse(row[0], CultureInfo.InvariantCulture), row[1], row[2], row[3], row[4], row[5], onOff ? row[6] : "", row[^3], "", "", row[^2], row[^1])
				: new MatchRow(int.Parse(row[0], CultureInfo.InvariantCulture), row[1], row[2], row[3], row[4], row[5], onOff ? row[6] : "", row[^4], row[^3], row[^2], "", row[^1]);
		}
	}
}
