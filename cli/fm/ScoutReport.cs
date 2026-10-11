using System.Text.Json;

namespace FmCli;

/// <summary>
/// The scouts' report on a player (FINDINGS §8.123): the tile on his page that carries their
/// grade opens it — who watched him last, what they make of his ability and potential, how
/// keen he is on a move, what he would cost, the points for and against, and where he would
/// rank in our squad at his position.
/// </summary>
internal static partial class Program
{
	private static async Task<int> PlayerReport(JsonElement card, bool json)
	{
		var name = Field(card.GetProperty("Player"), "Name");
		async Task<List<string>> Around(string anchor, string up) =>
			(await Fm.GetJson("texts", ("anchor", anchor), ("up", up))).EnumerateArray().Select(t => t.ValueKind == JsonValueKind.String ? t.GetString() : "").ToList();

		// The page remembers the tab it was left on, so the report may already be showing; else
		// the tile that opens it arrives a moment after the page, a button showing the scouts'
		// sentence. One of our own players has no such tile, nor has one nobody has watched.
		List<string> texts = [];
		string press = null;
		var showing = false;
		await Until(async () =>
		{
			if ((await Around("Pros", "0")).Count > 0) return showing = true;
			var tile = await Around("Scouting Report", "5");
			var at = tile.IndexOf("Scouting Report");
			if (at >= 0 && at + 2 < tile.Count) press = tile[at + 2];
			return press != null;
		}, seconds: 5, every: 0.5);
		if (!showing)
		{
			if (press == null)
			{
				Console.Error.WriteLine($"{name} için scout raporu yok (kendi oyuncumuz ya da hiç izlenmemiş). İzletmek için: scout player \"{name}\" --for <süre>");
				return 1;
			}
			var clicked = await Fm.Get("click", ("text", press));
			if (clicked.StartsWith("error", StringComparison.Ordinal)) return Report(clicked);
		}
		var previous = "";
		await Until(async () =>
		{
			texts = await Around("Pros", "12");
			var now = string.Join("|", texts);
			var settled = now == previous && texts.Contains("Cons");
			previous = now;
			return settled;
		}, seconds: 10, every: 0.5);
		if (!texts.Contains("Cons"))
		{
			Console.Error.WriteLine($"{name}: scout raporu açılmadı.");
			return 1;
		}

		string After(string label, int n = 1) => texts.IndexOf(label) is var at && at >= 0 && at + n < texts.Count ? texts[at + n] : "";
		List<string> Between(string from, string to) => texts.SkipWhile(t => t != from).Skip(1).TakeWhile(t => t != to).Select(t => t.Replace("\n", " — ")).ToList();
		var scouted = texts.FirstOrDefault(t => t.EndsWith("Last Scouted By", StringComparison.Ordinal));
		var by = scouted == null ? "" : $"{After(scouted)} ({scouted.Replace(" - Last Scouted By", "")})";
		var pros = Between("Pros", "Cons");
		var cons = Between("Cons", "Matches Watched");
		var comparison = texts.FirstOrDefault(t => t.StartsWith("Squad Comparison", StringComparison.Ordinal));
		var ranks = new List<(string Rank, string Player)>();
		if (comparison != null)
		{
			var cells = texts.SkipWhile(t => t != "Potential" || texts.IndexOf(t) < texts.IndexOf(comparison)).Skip(1).ToList();
			cells = texts.Skip(texts.LastIndexOf("Potential") + 1).ToList();
			for (var i = 0; i + 1 < cells.Count && int.TryParse(cells[i], out _); i += 2) ranks.Add((cells[i], cells[i + 1]));
		}

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new
			{
				Name = name, LastScoutedBy = by, Grade = After("Scout Recommendation"), Recommendation = After("Scout Recommendation", 2),
				CurrentAbility = After("Current Ability"), PotentialAbility = After("Potential Ability"),
				TransferInterest = After("Transfer Interest"), LoanInterest = After("Loan Interest"), Value = After("Transfer Value"), Wage = After("Estimated Wage"),
				Pros = pros, Cons = cons, MatchesWatched = After("Matches Watched"), BestRole = After("Best Role"),
				SquadComparison = new { Position = comparison?.Replace("Squad Comparison - ", ""), Ranks = ranks.Select(r => new { r.Rank, r.Player }) },
			}));
			return 0;
		}

		Console.WriteLine($"{name} — scout raporu{(by.Length > 0 ? $" · son izleyen {by}" : "")}");
		Console.WriteLine($"  Not: {After("Scout Recommendation")} — {After("Scout Recommendation", 2)}");
		Console.WriteLine($"  Yetenek: {After("Current Ability")} · Potansiyel: {After("Potential Ability")}");
		Console.WriteLine($"  Transfere ilgisi: {After("Transfer Interest")} · kiralığa: {After("Loan Interest")}");
		Console.WriteLine($"  Değer: {After("Transfer Value")} · tahmini maaş: {After("Estimated Wage")}");
		if (After("Best Role").Length > 0) Console.WriteLine($"  En iyi rol: {After("Best Role")} · izlenen maç: {After("Matches Watched")}");
		Console.WriteLine("Artılar:");
		foreach (var line in pros) Console.WriteLine($"  + {line}");
		Console.WriteLine("Eksiler:");
		foreach (var line in cons) Console.WriteLine($"  - {line}");
		if (ranks.Count > 0)
		{
			Console.WriteLine($"Kadroda sırası ({comparison.Replace("Squad Comparison - ", "")}):");
			foreach (var (rank, player) in ranks) Console.WriteLine($"  {rank,2}  {player}{(Fold(player) == Fold(name) ? "  ←" : "")}");
		}
		return 0;
	}
}
