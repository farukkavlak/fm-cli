using System.Text.Json;

namespace FmCli;

/// <summary>
/// The next opponent (FINDINGS §8.57, §8.121, §8.169): who they are, their form and usual
/// formation, their standout players and manager, how the meeting is expected to go — all from the
/// game's data on the next fixture's opponent — then from the portal's opposition report, by each
/// text's data path, the team news (row.Text), previous meetings (prevmeetingfixture.*) and the
/// analysts' tiles (polygonobservation.*). Nothing is read by its place after a heading.
/// </summary>
internal static partial class Program
{
	private sealed record AnalystTile(string Name, string Scope, List<(string Label, string Value)> Stats, string Verdict);

	private static async Task<int> Opponent(bool json)
	{
		if (await NotNow() is { } refused) return refused;

		var fixture = await Fm.Pull("Human.Team.NextFixture", "MatchVenueShort,KickoffTime,FixtureDateDistanceInDays");
		const string them = "Human.Team.NextFixture.TeamOpponent";
		var team = await Fm.Pull(them, "Name,Description,MostUsedFormation,RecentFormValues,NextOppositionThreatLevelAssessment");
		if (Field(team, "Name").Length == 0)
		{
			Console.Error.WriteLine("Rakip okunamadı (sıradaki maç yok olabilir).");
			return 1;
		}
		async Task<string> Standout(string which)
		{
			var name = Field(await Fm.Pull($"{them}.{which}.Player", "Name"), "Name");
			var value = Field(await Fm.Pull($"{them}.{which}", "StatValue"), "StatValue");
			return name.Length == 0 ? "" : value.Length > 0 ? $"{name} ({value})" : name;
		}
		var manager = Field(await Fm.Pull($"{them}.Manager", "Name"), "Name");
		var form = string.Join(" ", Standings.Form(Field(team, "RecentFormValues")));
		var venue = Field(fixture, "MatchVenueShort") switch { "H" => "iç saha", "A" => "deplasman", "N" => "tarafsız saha", var other => other };
		var days = Field(fixture, "FixtureDateDistanceInDays");
		var rows = new List<(string Label, string Value)>
		{
			("Form", form),
			("Diziliş", Field(team, "MostUsedFormation")),
			("Gol kralı", await Standout("TopGoalscorerPlayer")),
			("Asist kralı", await Standout("MostAssistsPlayer")),
			("En yüksek ortalama", await Standout("HighestAverageRatingPlayer")),
			("Menajer", manager),
		};

		// The portal's report page, read by each text's data path.
		var news = new List<string>();
		var meetings = new List<string>();
		var tiles = new List<AnalystTile>();
		if (!(await OpenPage(Screen.OppositionReport, under: Screen.Home)).StartsWith("error", StringComparison.Ordinal))
		{
			List<(string Text, string Path)> page = [];
			// The analysts' tiles say "No Data Available" for a moment before their numbers arrive;
			// the page is read once two readings agree.
			var previous = "";
			await Until(async () =>
			{
				// Held by the opponent's description, which every language writes (the expected outcome is
				// left empty in a Turkish game); the whole report is fourteen levels above it.
				page = (await Fm.GetJson("texts", ("anchorpath", "Team.Description"), ("up", "14"), ("paths", "1"), ("most", "1")))
					.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
				var now = string.Join("|", page.Select(t => t.Text));
				var settled = now == previous && page.Any(t => t.Path.StartsWith("polygonobservation.", StringComparison.Ordinal));
				previous = now;
				return settled;
			}, seconds: 6, every: 0.5);

			string lastPlain = null;
			AnalystTile tile = null;
			foreach (var (text, path) in page)
			{
				switch (path)
				{
					case "row.Text": news.Add(text); break;
					// A meeting's date is drawn without a path just before its result.
					case "prevmeetingfixture.ResultLinkableNoSpaces": meetings.Add(lastPlain is { } date ? $"{date} {text}" : text); break;
					case "polygonobservation.GraphType":
						tile = new AnalystTile(text, "", [], "");
						tiles.Add(tile);
						break;
					case "polygonobservation.PerformanceSummary" when tile != null:
						tiles[^1] = tile = tile with { Verdict = text };
						break;
					case "" when tile != null && tile.Verdict.Length == 0:
						// Within a tile: its scope first, then each number and its label.
						if (tile.Scope.Length == 0) tiles[^1] = tile = tile with { Scope = text };
						else if (lastPlain is { } number && IsNumber(number) && !IsNumber(text)) tile.Stats.Add((text, number));
						break;
				}
				lastPlain = path.Length == 0 ? text : null;
			}
		}
		static bool IsNumber(string text) =>
			double.TryParse(text.TrimEnd('%'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

		static string Turkish(string label) => label switch
		{
			"Gl/Gm" => "maç başı gol", "Np-xG/90" => "penaltısız xG/90", "Conc/Gm" => "maç başı yenen", "xGA/Gm" => "maç başı xGA",
			"Sh/Gm" => "maç başı şut", "Shot %" => "isabet %", "Pas %" => "pas %", "Tck %" => "top kapma %", _ => label,
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Team = Field(team, "Name"), Description = Field(team, "Description"), Venue = Field(fixture, "MatchVenueShort"),
				Kickoff = Field(fixture, "KickoffTime"), InDays = days,
				Summary = rows.ToDictionary(r => r.Label, r => r.Value), News = news, PreviousMeetings = meetings,
				Expectation = Field(team, "NextOppositionThreatLevelAssessment"),
				Tiles = tiles.Select(t => new { t.Name, t.Scope, Stats = t.Stats.ToDictionary(x => x.Label, x => x.Value), t.Verdict }),
			}));
			return 0;
		}
		Console.WriteLine($"{Field(team, "Name")} · {Field(team, "Description")}");
		Console.WriteLine($"  Maç: {venue}{(days.Length > 0 ? $" · {days} gün sonra" : "")}{(Field(fixture, "KickoffTime") is { Length: > 0 } k ? $" · {k}" : "")}");
		foreach (var (label, value) in rows)
			if (value.Length > 0) Console.WriteLine($"  {label}: {value}");
		if (meetings.Count > 0) Console.WriteLine($"  Önceki maçlar: {string.Join(" · ", meetings)}");
		if (Field(team, "NextOppositionThreatLevelAssessment") is { Length: > 0 } expected) Console.WriteLine($"  Beklenti: {expected}");
		foreach (var t in tiles.Where(t => t.Stats.Count > 0 || t.Verdict.Length > 0))
		{
			Console.WriteLine($"{t.Name}{(t.Scope.Length > 0 && t.Stats.Count > 0 ? $" ({t.Scope})" : "")}: {t.Verdict}");
			if (t.Stats.Count > 0) Console.WriteLine("  " + string.Join(" · ", t.Stats.Select(x => $"{Turkish(x.Label)} {x.Value}")));
		}
		if (news.Count > 0)
		{
			Console.WriteLine("Kadro haberleri:");
			foreach (var n in news) Console.WriteLine($"  {n}");
		}
		return 0;
	}
}
