using System.Text.Json;

namespace FmCli;

/// <summary>
/// A competition's leaders (FINDINGS §8.70): Match Day › Competitions › &lt;competition&gt; opens its
/// page, whose "Stats" tile reads as plain text — the teams with most goals, possession, yellow
/// cards and clean sheets, then the players with the best average rating, most goals, assists
/// and clean sheets, each as label, name, figure.
/// </summary>
internal static partial class Program
{
	private static readonly Dictionary<string, string> LeaderTurkish = new()
	{
		["Most Goals"] = "En çok gol", ["Most Possession"] = "Topa en çok sahip olan", ["Most Yellow Cards"] = "En çok sarı kart",
		["Most Clean Sheets"] = "En çok gol yemeden maç", ["Highest Average Rating"] = "En yüksek ortalama puan", ["Most Assists"] = "En çok asist",
	};

	private static async Task<int> League(bool json, string[] args)
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Overview", "Match Day");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		JsonElement menu = default;
		await Until(async () => (menu = (await Fm.GetJson("dropdowns", ("json", "1"), ("deep", "1"))).EnumerateArray()
			.FirstOrDefault(d => Rows(d).Contains("Competitions ›") && Rows(d).Contains("Match Squad"))).ValueKind == JsonValueKind.Object, seconds: 8, every: 0.5);
		if (menu.ValueKind != JsonValueKind.Object)
		{
			Console.Error.WriteLine("Turnuvalar menüsü bulunamadı.");
			return 1;
		}
		var competitions = Submenu(menu, Rows(menu).IndexOf("Competitions ›")).Where(c => c != "Overview").ToList();

		var wanted = string.Join(" ", Words(args));
		// The league is the default: of the club's competitions, the one whose name says so.
		var competition = wanted.Length == 0
			? competitions.FirstOrDefault(c => c.Contains("League", StringComparison.Ordinal) && !c.StartsWith("UEFA", StringComparison.Ordinal)) ?? competitions.FirstOrDefault()
			: Choose(competitions, wanted, "Turnuva");
		if (competition == null) return 1;

		var before = string.Join("|", await LeaderTexts());
		var said = await Fm.Get("select", ("ptr", Field(menu, "Pointer")), ("names", $"Competitions>{competition}"), ("fire", "1"));
		if (said.StartsWith("error", StringComparison.Ordinal)) return Report(said);

		// Another competition's tile can still be up for a moment, and a tile fills in its names
		// and figures a beat after its labels. It is taken once it reads the same twice, has
		// changed from before (or had time to, when it was this one already) and every leader
		// has a name — or has had time to, since a competition barely begun leaves names out.
		List<string> texts = [];
		var previous = "";
		var reads = 0;
		await Until(async () =>
		{
			texts = await LeaderTexts();
			reads++;
			var now = string.Join("|", texts);
			var whole = Leaders(texts, "Teams", "Players").Concat(Leaders(texts, "Players", null)).All(l => l.Name != "-" && l.Figure != "-");
			var settled = texts.Contains("Players") && now == previous && (now != before || reads >= 4) && (whole || reads >= 8);
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (!texts.Contains("Players"))
		{
			Console.Error.WriteLine($"{competition} sayfasında istatistik yok (turnuva henüz başlamamış olabilir).");
			return 1;
		}

		var teams = Leaders(texts, "Teams", "Players");
		var players = Leaders(texts, "Players", null);
		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Competition = competition,
				Teams = teams.Select(t => new { Stat = t.Label, Team = t.Name, t.Figure }),
				Players = players.Select(p => new { Stat = p.Label, Player = p.Name, p.Figure }),
			}));
			return 0;
		}

		Console.WriteLine($"{competition} — liderler");
		void Block(string title, List<(string Label, string Name, string Figure)> rows)
		{
			Console.WriteLine(title);
			foreach (var (label, name, figure) in rows)
			{
				var said = LeaderTurkish.GetValueOrDefault(label, label);
				if (Ui.Rich) Ui.Line($"  [dim]{Ui.Safe(said),-26}[/] [bold]{Ui.Safe(name)}[/] [yellow]{Ui.Safe(figure)}[/]");
				else Console.WriteLine($"  {said}: {name} ({figure})");
			}
		}
		Block("Takımlar", teams);
		Block("Oyuncular", players);
		if (competitions.Count > 1) Console.WriteLine($"\nBaşka turnuva: league <{string.Join(" | ", competitions)}>");
		return 0;
	}

	private static async Task<List<string>> LeaderTexts() =>
		(await Fm.GetJson("texts", ("anchor", "Most Goals"), ("up", "10"))).EnumerateArray().Select(t => t.GetString() ?? "").ToList();

	/// <summary>
	/// The leaders between one heading and the next: each known label, then the name and the
	/// figure after it. A competition barely begun can leave the name out, so the cells are
	/// taken up to the next label rather than three at a time.
	/// </summary>
	private static List<(string Label, string Name, string Figure)> Leaders(List<string> texts, string from, string to)
	{
		var start = texts.IndexOf(from) + 1;
		var end = to != null && texts.IndexOf(to) is var e && e > start ? e : texts.Count;
		var rows = new List<(string, string, string)>();
		if (start <= 0) return rows;
		for (var i = start; i < end; i++)
		{
			if (!LeaderTurkish.ContainsKey(texts[i])) continue;
			var cells = texts.Skip(i + 1).Take(end - i - 1).TakeWhile(t => !LeaderTurkish.ContainsKey(t)).ToList();
			rows.Add((texts[i], cells.Count > 1 ? cells[0] : "-", cells.Count > 0 ? cells[^1] : "-"));
		}
		return rows;
	}
}
