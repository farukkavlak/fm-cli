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
			("Form", After("Form")),
			("Diziliş", After("Most Used Formation")),
			("Gol kralı", $"{After("Top Goalscorer", 2)} ({After("Top Goalscorer")})"),
			("Asist kralı", $"{After("Most Assists", 2)} ({After("Most Assists")})"),
			("En yüksek ortalama", $"{After("Highest Average Rating", 2)} ({After("Highest Average Rating")})"),
		};
		if (json)
		{
			Console.WriteLine(JsonSerializer.Serialize(new { Team = team, League = league, Summary = rows.ToDictionary(r => r.Item1, r => r.Item2), News = news }));
			return 0;
		}
		Console.WriteLine($"{team} · {league}");
		foreach (var (label, value) in rows) Console.WriteLine($"  {label}: {(value is "" or " ()" ? "-" : value.Replace("None", "-"))}");
		if (news.Count > 0)
		{
			Console.WriteLine("Kadro haberleri:");
			foreach (var n in news) Console.WriteLine($"  {n}");
		}
		return 0;
	}
}
