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

	// The game names the match ratings page itself: its element lives in the card while the page is
	// up, whatever language it reads in, and is empty once it is closed.
	private const string FormCard = "PlayerFormCard";
	// The form tile sits in the card's own "PlayerForm" binding; its title is the game's words,
	// so it is addressed by the part of the game's data it lives in, not by what it says.
	private const string FormTile = "PlayerForm";
	// The summary's own binding: the page has drawn its numbers when this path has answered.
	private const string FormStats = "formstats.GamesPlayed";

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
				if ((await Fm.GetJson("texts", ("within", FormCard))).GetArrayLength() > 0
				    && (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().Cast<JsonElement?>().FirstOrDefault(c => Field(c!.Value, "Name") == "BackButton") is { } back)
					await Fm.Get("press", ("ptr", Field(back, "Pointer")));
			}
			catch (Exception) { }
		}
	}

	private static async Task<int> ReadPlayerMatches(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		// The page is read out of the card the game names itself (?within=PlayerFormCard), each line
		// with the data path it is bound to, so nothing has to be recognised by its words.
		async Task<List<(string Text, string Path)>> Page() => (await Fm.GetJson("texts", ("within", FormCard), ("paths", "1")))
			.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();

		// The page stays loaded beneath once opened, so another player's can still be read.
		var before = string.Join("|", (await Page()).Select(t => t.Text));
		var clicked = "";
		await Until(async () => !(clicked = await Fm.Get("click", ("within", FormTile), ("name", "layout-tile-interaction-button"))).StartsWith("error", StringComparison.Ordinal), seconds: 8, every: 0.5);
		if (clicked.StartsWith("error", StringComparison.Ordinal))
		{
			Console.Error.WriteLine(L($"{name} için form kutusu yok (bu sezon maçı olmayabilir)."));
			return 1;
		}
		List<(string Text, string Path)> page = [];
		var previous = "";
		var reads = 0;
		// Done when the summary's own path has answered and the lines have held still — the game
		// names the page in no words, so none are looked for.
		await Until(async () =>
		{
			page = await Page();
			reads++;
			var now = string.Join("|", page.Select(t => t.Text));
			var settled = page.Any(t => t.Path == FormStats) && now == previous && (now != before || reads >= 6);
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		var texts = page.Select(t => t.Text).ToList();
		if (!page.Any(t => t.Path == FormStats))
		{
			Console.Error.WriteLine(L($"{name}: maç puanları sayfası açılmadı."));
			return 1;
		}

		string After(string label) => texts.IndexOf(label) is var i && i >= 0 && i + 1 < texts.Count ? texts[i + 1] : "";
		var summary = new { Apps = After("Apps"), Best = After("Max. Rating"), Worst = After("Min. Rating"), Average = After("Average Rating"), Goals = After("Goals Scored"), Assists = After("Assists") };

		// The list lays out only the rows in view; the rest come in as it is scrolled. Whether it goes on
		// says the game itself: the view holds the whole list when the scroll answers " of 0" (as the
		// numbers table is read), otherwise the window is asked for from the top and from further down.
		// A scrolled window is taken once it has changed and held for two readings — the scrolled rows
		// were measured to land in ~30 ms, so the fixed half second that used to be here only guessed the
		// machine's speed; the game may also have been left scrolled from a look before this one.
		var rows = new Dictionary<int, MatchRow>();
		var goalkeeper = await Keeper(texts);
		foreach (var row in Rows(texts, goalkeeper)) rows[row.Number] = row;
		if (rows.Count > 0)
		{
			static string Key(IEnumerable<MatchRow> inView) => string.Join(",", inView.Select(r => r.Number));
			var window = Key(Rows(texts, goalkeeper));
			var anchor = rows.Values.First().Date;
			if (!(await Fm.Get("scroll", ("anchor", anchor), ("to", "0"))).Trim().EndsWith(" of 0", StringComparison.Ordinal))
			{
				foreach (var at in new[] { "0.5", "1" })
				{
					var scrolled = await Fm.Get("scroll", ("anchor", anchor), ("to", at));
					if (scrolled.StartsWith("error", StringComparison.Ordinal)) break;
					var held = window;
					var moved = false;
					List<MatchRow> inView = [];
					await Until(async () =>
					{
						inView = Rows((await Page()).Select(t => t.Text).ToList(), goalkeeper).ToList();
						var now = Key(inView);
						moved |= now != window;
						var settled = moved && now == held;
						held = now;
						return settled;
					}, seconds: 4, every: 0.25);
					foreach (var row in inView) rows.TryAdd(row.Number, row);
					if (inView.Count > 0)
					{
						window = Key(inView);
						anchor = inView[0].Date;
					}
				}
				await Fm.Get("scroll", ("anchor", anchor), ("to", "0"));
			}
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
			.Where(r => r != null).Select(r => r!.Value).ToList();
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
		heads.Add(L("PUAN"));
		List<string> Cells(MatchRow r)
		{
			var cells = new List<string>
			{
				rich ? $"[dim]{r.Number}[/]" : r.Number.ToString(CultureInfo.InvariantCulture), r.Date,
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
	/// rating); a substitute's minute sits before the xG. The headings are not needed to find them:
	/// nothing above the table pairs a bare number with a date.
	/// </summary>
	private static IEnumerable<MatchRow> Rows(List<string> texts, bool keeper)
	{
		// A date as the game writes it: the order is the game's own preference
		// (dd/mm/yyyy, mm/dd/yyyy, yyyy/mm/dd, yyyy/dd/mm — BAGIMLILIKLAR §C), so only the year's four
		// digits and the separators are taken as they are, never which end the year sits at.
		static bool Date(string t) => Regex.IsMatch(t, @"^(?:\d{4}([./-])\d{1,2}\1\d{1,2}|\d{1,2}([./-])\d{1,2}\2\d{4})$");
		var starts = Enumerable.Range(0, Math.Max(0, texts.Count - 1)).Where(i => Regex.IsMatch(texts[i], @"^\d+$") && Date(texts[i + 1])).ToList();
		for (var s = 0; s < starts.Count; s++)
		{
			var row = texts.Skip(starts[s]).Take((s + 1 < starts.Count ? starts[s + 1] : texts.Count) - starts[s]).ToList();
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
