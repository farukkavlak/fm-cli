namespace FmCli;

/// <summary>
/// `fm __stops FILE [--k N] [--share S]`: scores what sentences at stops are read as asking
/// (tests/nl/stops/cases.tsv): right, wrong, and the worst miss, an answer given to a sentence that asked none.
/// </summary>
internal static partial class Program
{
	/// <summary>A command of each kind, as the stops give them: what the bench's stops are made of.</summary>
	private static string[] CommandOf(string kind, bool vision) => kind switch
	{
		"accept" or "reject" or "stall" => ["offers", "2", kind],
		"view" => vision ? ["vision"] : ["offers", "2", "view"],
		"play" => ["match", "--play"],
		"instant" => ["match", "--instant"],
		"goon" => ["match"],
		"skip" => ["next", "--skip"],
		"open" => ["next"],
		"end" => ["vision", "end"],
		"answer" => ["talk"],
		// an offer of the list: about something, leading to its own answers
		"about" => ["offers", "9"],
		_ => throw new ArgumentException(kind),
	};

	private static int StopBench(string[] args)
	{
		string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		float F(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
		if (Opt("--top") is { } t) Nl.StopKinds.Top = int.Parse(t);
		if (Opt("--bias") is { } b) Nl.StopKinds.Bias = F(b);
		if (Opt("--floor") is { } fl) Nl.StopKinds.Floor = F(fl);
		if (Opt("--margin") is { } m) Nl.StopKinds.Margin = F(m);
		if (Model() is null) { Console.Error.WriteLine("no model"); return 1; }
		var lines = File.ReadAllLines(args[1]).Where(l => l.Length > 0 && !l.StartsWith('#')).Select(l => l.Split('\t')).ToList();
		var (right, given, none, wrongKind, asked) = (0, 0, 0, 0, 0);
		foreach (var (kinds, sentence, wanted) in lines.Select(l => (l[0].Split(','), l[1], l[2])))
		{
			var vision = kinds.Contains("end");
			// "about=<label>": an answer about something, shown as the game would ("Başakşehir FK → Baray Yenilmez")
			var stop = new HeldStop("bench", [], [.. kinds.Select(kind => kind.Split('=') is [var k, var label] ? (label, CommandOf(k, vision)) : (kind, CommandOf(kind, vision)))]);
			var said = AnswersSaid(stop, sentence);
			var places = said?.Places ?? [];
			var got = string.Join("|", places.Select(at => DoneBy(stop.Answers[at].Command) ?? $"about>{string.Join(",", said!.Done)}").DefaultIfEmpty("-"));
			if (got == wanted) { right++; if (Environment.GetEnvironmentVariable("FM_BENCH_ALL") == "1") Console.WriteLine($"  {sentence,-36} wanted {wanted,-8} got {got,-14} {LastVotes} OK{(said?.Sure == false ? " (asks yes)" : "")}"); continue; }
			if (places.Count > 1) asked++;
			else if (wanted == "-") given++;
			else if (got == "-") none++;
			else wrongKind++;
			Console.WriteLine($"  {sentence,-36} wanted {wanted,-8} got {got,-14} {LastVotes}");
		}
		Console.WriteLine($"{right}/{lines.Count} right ({100.0 * right / lines.Count:0.0}%), wrong answer {wrongKind}, answered where none was asked {given}, asked between {asked}, left to the router {none}");
		return 0;
	}
}
