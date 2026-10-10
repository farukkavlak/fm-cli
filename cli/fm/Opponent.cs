using System.Text.Json;

namespace FmCli;

/// <summary>
/// The next opposition report's summary (FINDINGS §8.57): their form, usual formation, top scorer
/// and assister, best-rated player and team news, read off the page in order.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Opponent(bool json)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Next Opposition Report", "Match Day");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<string> texts = [];
		await Until(async () => (texts = (await Fm.GetJson("texts", ("anchor", "Next Opposition Report"), ("up", "12"))).EnumerateArray()
			.Select(t => t.GetString() ?? "").ToList()).Contains("Most Used Formation"), seconds: 10, every: 0.5);
		if (!texts.Contains("Most Used Formation"))
		{
			Console.Error.WriteLine("Rakip raporu okunamadı (sıradaki maç yok olabilir).");
			return 1;
		}

		string After(string label, int n = 1) => texts.IndexOf(label) is var at && at >= 0 && at + n < texts.Count ? texts[at + n] : "";
		var team = After("Next Opposition Report");
		var league = After("Next Opposition Report", 2);
		var news = new List<string>();
		var from = texts.IndexOf("Team News");
		string[] stops = ["Team Report", "Data Hub", "Match Preview", "Key Players", "Overview"];
		if (from >= 0)
			foreach (var t in texts.Skip(from + 1))
			{
				if (stops.Contains(t) || news.Count >= 10) break;
				news.Add(t);
			}

		var rows = new List<(string, string)>
		{
			// The form is drawn, not written: with nothing written after its heading the next
			// heading is what follows.
			("Form", After("Form") == "Most Used Formation" ? "" : After("Form")),
			("Diziliş", After("Most Used Formation")),
			("Gol kralı", $"{After("Top Goalscorer", 2)} ({After("Top Goalscorer")})"),
			("Asist kralı", $"{After("Most Assists", 2)} ({After("Most Assists")})"),
			("En yüksek ortalama", $"{After("Highest Average Rating", 2)} ({After("Highest Average Rating")})"),
		};
		// The analysts' numbers for the season are on the portal's own report page: value and
		// label in pairs, then a verdict in a phrase and a sentence. An opponent from a league
		// that is not loaded has none ("No Data Available").
		var stats = new List<(string Label, string Value)>();
		string scope = "", verdict = "";
		if (!(await OpenPage("Opposition Report", "Portal")).StartsWith("error", StringComparison.Ordinal))
		{
			List<string> block = [];
			await Until(async () =>
			{
				block = (await Fm.GetJson("texts", ("anchor", "General Performance"), ("up", "7"))).EnumerateArray()
					.Select(t => t.ValueKind == System.Text.Json.JsonValueKind.String ? t.GetString() : "").ToList();
				// The tile says "No Data Available" for a moment before its numbers arrive, so only
				// numbers end the wait; without any it runs out and there are none.
				return block.Count > 3 && double.TryParse(block[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
			}, seconds: 5, every: 0.5);
			if (block.Count > 3 && !block.Contains("No Data Available"))
			{
				scope = block[1];
				var at = 2;
				for (; at + 1 < block.Count && double.TryParse(block[at], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _); at += 2)
					stats.Add((block[at + 1], block[at]));
				verdict = string.Join(" — ", block.Skip(at));
			}
		}
		static string Turkish(string label) => label switch
		{
			"Gl/Gm" => "maç başı gol", "Np-xG/90" => "penaltısız xG/90", "Conc/Gm" => "maç başı yenen", "xGA/Gm" => "maç başı xGA",
			"Sh/Gm" => "maç başı şut", "Shot %" => "isabet %", "Pas %" => "pas %", "Tck %" => "top kapma %", _ => label,
		};

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Team = team, League = league, Summary = rows.ToDictionary(r => r.Item1, r => r.Item2), News = news,
				Performance = new { Scope = scope, Stats = stats.ToDictionary(x => x.Label, x => x.Value), Verdict = verdict },
			}));
			return 0;
		}
		Console.WriteLine($"{team} · {league}");
		foreach (var (label, value) in rows) Console.WriteLine($"  {label}: {(value is "" or " ()" ? "-" : value.Replace("None", "-"))}");
		if (stats.Count > 0)
		{
			Console.WriteLine($"Genel performans ({scope}):");
			Console.WriteLine("  " + string.Join(" · ", stats.Select(x => $"{Turkish(x.Label)} {x.Value}")));
			if (verdict.Length > 0) Console.WriteLine($"  {verdict}");
		}
		if (news.Count > 0)
		{
			Console.WriteLine("Kadro haberleri:");
			foreach (var n in news) Console.WriteLine($"  {n}");
		}
		return 0;
	}
}
