namespace FmCli;

/// <summary>
/// The match preparation in training (Match Day › Match Preparation Training, FINDINGS §8.60):
/// a tactical focus with the ball and without it, and two set-piece focuses, each with what it means.
/// </summary>
internal static partial class Program
{
	private static async Task<int> Prep()
	{
		if (await NotNow() is { } refused) return refused;
		var opened = await OpenPage("Match Preparation Training", "Match Day");
		if (opened.StartsWith("error", StringComparison.Ordinal)) return Report(opened);

		List<string> texts = [];
		await Until(async () => (texts = (await Fm.GetJson("texts", ("anchor", "Tactical Preparation"), ("up", "8"))).EnumerateArray()
			.Select(t => t.GetString() ?? "").ToList()).Contains("Set Piece Preparation"), seconds: 10, every: 0.5);
		var tactical = texts.IndexOf("Tactical Preparation");
		var setPieces = texts.IndexOf("Set Piece Preparation");
		if (tactical < 0 || setPieces < 0)
		{
			Console.Error.WriteLine("Maç hazırlığı okunamadı.");
			return 1;
		}

		// Each focus is its name and then a sentence on what it works on.
		static IEnumerable<(string Name, string Means)> Pairs(List<string> part)
		{
			for (var i = 0; i + 1 < part.Count; i += 2) yield return (part[i], part[i + 1]);
		}
		var end = texts.IndexOf("Training Overview") is var e && e > setPieces ? e : texts.Count;
		var tacticalPart = Pairs(texts.Skip(tactical + 1).Take(setPieces - tactical - 1).ToList()).ToList();
		var setPart = Pairs(texts.Skip(setPieces + 1).Take(end - setPieces - 1).ToList()).ToList();

		string[] tacticalNames = ["Topla", "Topsuz"];
		Console.WriteLine("Maç hazırlığı");
		for (var i = 0; i < tacticalPart.Count; i++)
			Console.WriteLine($"  {(i < 2 ? tacticalNames[i] : "Taktik")}: {tacticalPart[i].Name} — {tacticalPart[i].Means}");

		// The set pieces are not in a fixed order; the name says which side of them it is.
		foreach (var (name, means) in setPart)
		{
			var side = name.StartsWith("Defending", StringComparison.Ordinal) ? "Duran top (savunma)"
				: name.StartsWith("Attacking", StringComparison.Ordinal) ? "Duran top (hücum)" : "Duran top";
			Console.WriteLine($"  {side}: {name} — {means}");
		}
		return 0;
	}
}
