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

	/// <summary>
	/// The page is a card over the game, with the player's talks beside it ("Praise Recent Form"…):
	/// left open, the game is taken to be waiting on a question and time does not move (Hackett, 11
	/// Nisan 2026). It is closed by going back, once read.
	/// </summary>
	private static async Task<int> PlayerMatches(JsonElement card, bool json)
	{
		try { return await ReadPlayerMatches(card, json); }
		finally
		{
			try
			{
				if ((await Fm.GetJson("texts", ("anchor", "Match Ratings"), ("up", "0"))).GetArrayLength() > 0
				    && (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(c => Field(c.Value, "Name") == "BackButton") is { } back)
					await Fm.Get("press", ("ptr", Field(back, "Pointer")));
			}
			catch (Exception) { }
		}
	}

	private static async Task<int> ReadPlayerMatches(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		async Task<List<string>> Texts() => (await Fm.GetJson("texts", ("anchor", "All Competitions"), ("up", "10"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();

		// The page stays loaded beneath once opened, so another player's can still be read.
		var before = string.Join("|", await Texts());
		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("text", "Last 5 Club Games"), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"{name} için form kutusu yok (bu sezon maçı olmayabilir)."));
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
			Console.Error.WriteLine(L($"{name}: maç puanları sayfası açılmadı."));
			return 1;
		}

		string After(string label) => texts.IndexOf(label) is var i && i >= 0 && i + 1 < texts.Count ? texts[i + 1] : "";
		var summary = new { Apps = After("Apps"), Best = After("Max. Rating"), Worst = After("Min. Rating"), Average = After("Average Rating"), Goals = After("Goals Scored"), Assists = After("Assists") };

		// The list lays out only the rows in view; the rest come in as it is scrolled.
		var rows = new Dictionary<int, MatchRow>();
		var goalkeeper = await Keeper(texts);
		foreach (var row in Rows(texts, goalkeeper)) rows[row.Number] = row;
		if (int.TryParse(summary.Apps, out var apps) && rows.Count < apps && rows.Count > 0)
		{
			foreach (var at in new[] { "0.5", "1" })
			{
				await Fm.Get("scroll", ("anchor", rows.Values.First().Date), ("to", at));
				await Task.Delay(500);
				foreach (var row in Rows(await Texts(), goalkeeper)) rows.TryAdd(row.Number, row);
			}
			await Fm.Get("scroll", ("anchor", rows.Values.First().Date), ("to", "0"));
		}
		// A match he sat out, or came on too late to be rated in, is given as 0.00; the page's own lowest
		// rating then reads 11.00 (Collins, 11 Nisan 2026). Neither is a rating.
		static string Real(string rating) => GameNumbers.Read(rating) is > 0 and <= 10 ? rating : "-";
		var all = rows.Values.OrderByDescending(r => r.Number).Select(r => r with { Rating = Real(r.Rating) }).ToList();
		summary = summary with { Best = Real(summary.Best), Worst = Real(summary.Worst), Average = Real(summary.Average) };
		var keeper = goalkeeper;

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { Name = name, Goalkeeper = keeper, Summary = summary, Matches = all }));
			return 0;
		}

		var cleanSheets = all.Count(r => r.Conceded == "0" && r.Position != "-");
		Console.WriteLine(L($"{name} — {summary.Apps} maç · ortalama {summary.Average} · en iyi {summary.Best} · en kötü {summary.Worst} · ")
			+ (keeper ? L($"{cleanSheets} gol yemediği maç") : L($"{summary.Goals} gol · {summary.Assists} asist")));
		var subs = all.Any(r => r.OnOff.Length > 0);
		var rich = Ui.Rich;

		// His ratings as a row of bars, oldest first, on the scale ratings mostly live on.
		var ratings = all.AsEnumerable().Reverse()
			.Select(r => GameNumbers.Read(r.Rating))
			.Where(r => r != null).Select(r => r.Value).ToList();
		if (ratings.Count > 1)
		{
			var bars = ratings.Select(r => (Bar: Ui.Bar(r, 5.5, 9), Rating: r)).ToList();
			if (rich) Ui.Line(L($"[dim]puan[/]  {string.Concat(bars.Select(b => b.Rating >= 7.5 ? $"[green]{b.Bar}[/]" : b.Rating < 6.5 ? $"[red]{b.Bar}[/]" : b.Bar.ToString()))}  [dim]eski → yeni[/]"));
			else Console.WriteLine(L($"puan  {string.Concat(bars.Select(b => b.Bar))}  eski → yeni"));
		}
		// The page gives the score home side first and does not say which side he was on.
		var heads = new List<string> { "#", L("TARİH"), L("RAKİP"), L("SKOR (EV-DEP)"), L("MEVKİ") };
		if (subs) heads.Add(rich ? "" : L("GİRDİ/ÇIKTI"));
		heads.AddRange(keeper ? ["xGA", L("YEDİĞİ")] : ["xG", L("GOL"), L("AS")]);
		heads.Add("PUAN");
		List<string> Cells(MatchRow r)
		{
			var cells = new List<string>
			{
				rich ? $"[dim]{r.Number}[/]" : r.Number.ToString(CultureInfo.InvariantCulture), rich ? Short(r.Date) : r.Date,
				rich ? Ui.Safe(r.Opponent) : r.Opponent, r.Score, r.Position == "-" ? (rich ? L("[dim]oynamadı[/]") : L("oynamadı")) : rich ? $"{r.Position} [dim]{r.Role}[/]" : $"{r.Position} {r.Role}",
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
		static string OnOff(string text) => Regex.Replace(text, @"^(on|off) (\d+)$", m => $"{m.Groups[2].Value}' {(m.Groups[1].Value == "on" ? L("girdi") : L("çıktı"))}");
		static string Count(string n, string colour) => n is "" or "0" ? "[dim]0[/]" : $"[{colour} bold]{n}[/]";
		static string Colour(string rating) => GameNumbers.Read(rating) is { } r
			? r >= 7.5 ? $"[green bold]{rating}[/]" : r < 6.5 ? $"[red]{rating}[/]" : rating : rating;
	}

	/// <summary>Whether the table is a goalkeeper's: xG against in place of xG, whatever the game calls it.</summary>
	private static async Task<bool> Keeper(List<string> texts)
	{
		await GameWords.Ask(Fm, "xGA");
		return texts.Any(t => t == "xGA" || GameWords.Same(t, "xGA"));
	}

	/// <summary>
	/// The table's rows out of the page's texts: each starts with the game's number and its date,
	/// and ends with xG, assists, goals and rating (a keeper's xG against, goals conceded and
	/// rating); a substitute's minute sits before the xG.
	/// </summary>
	private static IEnumerable<MatchRow> Rows(List<string> texts, bool keeper)
	{
		var start = texts.IndexOf("Inf");
		if (start < 0) yield break;
		// The headings after "Inf" come back in other words in another language ("Pts" for "Rating" in
		// Turkish); the rows are known by their number and date, not by where the headings end.
		var cells = texts.Skip(start + 1).ToList();
		static bool Date(string t) => Regex.IsMatch(t, @"^\d{2}/\d{2}/\d{4}$");
		var starts = Enumerable.Range(0, Math.Max(0, cells.Count - 1)).Where(i => Regex.IsMatch(cells[i], @"^\d+$") && Date(cells[i + 1])).ToList();
		for (var s = 0; s < starts.Count; s++)
		{
			var row = cells.Skip(starts[s]).Take((s + 1 < starts.Count ? starts[s + 1] : cells.Count) - starts[s]).ToList();
			if (row.Count < 7) continue;
			// Whatever the page shows after the table is not the last row's.
			// "on 72" / "gir 72.": a substitute's minute, where another row has its xG.
			var onOff = GameNumbers.Read(row[6]) == null;
			var size = 6 + (onOff ? 1 : 0) + (keeper ? 3 : 4);
			if (row.Count < size) continue;
			row = row.Take(size).ToList();
			yield return keeper
				? new MatchRow(int.Parse(row[0], CultureInfo.InvariantCulture), row[1], row[2], row[3], row[4], row[5], onOff ? row[6] : "", row[^3], "", "", row[^2], row[^1])
				: new MatchRow(int.Parse(row[0], CultureInfo.InvariantCulture), row[1], row[2], row[3], row[4], row[5], onOff ? row[6] : "", row[^4], row[^3], row[^2], "", row[^1]);
		}
	}
}
