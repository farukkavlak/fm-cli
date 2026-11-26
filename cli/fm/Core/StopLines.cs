using FmCli.Nl;

namespace FmCli;

/// <summary>
/// A stop whose answers are the game's own lines (T221): a message's answers, the lines of a conversation or
/// a press conference. A sentence is held against the lines as the game shows them, by the model's sense of
/// what each says (the same in any language the user and the game share), never by words the CLI keeps:
/// "kin beslemiyorum" finds "hiçbir şekilde kin beslemem". The pick is sent by its place or the game's id.
/// A place said ("ikincisi") is that line. One line clearly nearest runs; a few near alike are asked between;
/// a sentence near none of them goes to the router, and the stop waits. Measured on tests/nl/lines (TR+EN, 70
/// sentences tuned on, 53 held): held TR 16/23 right, EN 28/30; no wrong line said without a yes asked first.
/// </summary>
internal static partial class Program
{
	/// <summary>The kind of a stop of the game's own lines.</summary>
	internal const string LinesKind = "lines";

	/// <summary>How near the nearest line must come for the sentence to be about the lines at all.</summary>
	internal static float LinesFloor = 0.5f;

	/// <summary>How far ahead of the next one the nearest line must be to run without asking.</summary>
	internal static float LinesGap = 0.1f;

	/// <summary>The lines within this of the nearest are asked between, when it is not clearly ahead.</summary>
	internal static float LinesNear = 0.06f;

	/// <summary>How much the user's own words found in a line add to its nearness, all of them in one line.</summary>
	internal static float LinesWords = 0.3f;

	/// <summary>How much nearer than the router's nearest example the best line must come.</summary>
	internal static float LinesOther = 0.1f;

	/// <summary>How the last sentence came out against the lines: each one's nearness, for the bench.</summary>
	internal static string LastLineSims = "";

	/// <summary>
	/// The lines a sentence says, by their places (0 based): one (Sure: clearly), several near alike, or null
	/// when it is a question or near none of them.
	/// </summary>
	internal static SaidAnswers? LinesSaid(HeldStop stop, string sentence)
	{
		var count = stop.Answers.Count;
		if (count == 0 || sentence.TrimEnd().EndsWith('?')) return null;
		var words = new Nl.Text(sentence).Words;
		if (words.Length == 0) return null;
		var past = words.Any(w => int.TryParse(w, out var n) && n > count);
		var places = words.Where(w => int.TryParse(w, out var n) && n >= 1 && n <= count).Select(int.Parse)
			.Concat(past ? [] : SaidPlace.Where(p => p.Cues.Any(c => words.Any(w => Nl.Text.Matches(w, c, typo: false)))).Select(p => p.Place == -1 ? count : p.Place))
			.Where(p => p >= 1 && p <= count).Distinct().ToList();
		if (places.Count == 1 && words.Length <= 3) return new SaidAnswers([places[0] - 1], new HashSet<string>());

		if (Model() is not { } router || embedder == null) return null;
		// A line said in part is near that part: each line is held whole and by its clauses (split at its
		// punctuation), and counts as near as the nearest of them.
		var parts = stop.Answers.Select(a => Clauses(a.Label)).ToList();
		var vectors = embedder.Embed([sentence, .. parts.SelectMany(p => p)]);
		var sims = new List<float>();
		var at = 1;
		foreach (var p in parts)
		{
			sims.Add(vectors.Skip(at).Take(p.Count).Max(v => Nl.Embedder.Dot(vectors[0], v)));
			at += p.Count;
		}
		// The user's own words found in a line, as the game wrote it ("onur" in "büyük bir onur"): each counts by how
		// few lines hold it, so a word every line has tells nothing. Endings and typos are borne as names are.
		var lineWords = stop.Answers.Select(a => NameFit.Words(a.Label).ToList()).ToList();
		var typed = words.Where(w => w.Length >= 3 && !w.All(char.IsDigit)).ToList();
		for (var w = 0; w < typed.Count && LinesWords > 0; w++)
		{
			var holding = Enumerable.Range(0, count).Where(i => lineWords[i].Any(l => Fits(typed[w], l) > 0)).ToList();
			if (holding.Count == 0 || holding.Count == count) continue;
			foreach (var i in holding) sims[i] += LinesWords / holding.Count / typed.Count;
		}
		LastLineSims = string.Join(" ", sims.Select((s, i) => $"{i + 1}:{s:0.00}"));
		var order = Enumerable.Range(0, count).OrderByDescending(i => sims[i]).ToList();
		var best = sims[order[0]];
		if (best < LinesFloor) return null;
		// A command said in words that a line also has ("kulübün kararlarını göster") comes about as near to what the
		// router knows as asking for something: a line not clearly nearer than that is shown and said on a yes. The
		// router's examples for saying a line (talk, reply) do not count against the lines.
		var other = router.Nearest(vectors[0], 1, call => !call.Contains("fm_talk", StringComparison.Ordinal) && !call.Contains("fm_reply", StringComparison.Ordinal))
			.Select(n => n.Sim).DefaultIfEmpty(0).Max();
		LastLineSims += $" other:{other:0.00}";
		var clear = best - other >= LinesOther;
		var second = count > 1 ? sims[order[1]] : -1f;

		// A message's answer can be one that cannot be taken back (a job, a contract): read from a sentence, it is
		// shown and runs on a yes. A line of a conversation clearly nearest runs.
		var heavy = stop.Answers.Any(a => a.Command is ["reply", ..]);
		if (best - second >= LinesGap) return new SaidAnswers([order[0]], new HashSet<string>(), Sure: !heavy && clear);
		return new SaidAnswers([.. order.Where(i => sims[i] >= best - LinesNear).Take(3)], new HashSet<string>(), Sure: false);
	}

	/// <summary>A line whole, then its clauses: the pieces between its punctuation, the short ones left out.</summary>
	internal static List<string> Clauses(string line) =>
		[line, .. line.Split(['.', ',', ';', ':', '!', '?'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(c => c.Split(' ').Length >= 3 && c != line.Trim())];

	/// <summary>
	/// `fm __lines FILE [--floor F] [--gap G] [--near N]`: scores sentences against a stop's lines
	/// (tests/nl/lines/*.tsv). A line "= a | b | c" sets the lines; then "sentence TAB n" wants line n, 0 none.
	/// </summary>
	private static int LinesBench(string[] args)
	{
		string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		float F(string v) => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
		if (Opt("--floor") is { } fl) LinesFloor = F(fl);
		if (Opt("--gap") is { } g) LinesGap = F(g);
		if (Opt("--near") is { } n) LinesNear = F(n);
		if (Opt("--words") is { } ws) LinesWords = F(ws);
		if (Opt("--other") is { } ot) LinesOther = F(ot);
		if (Model() is null) { Console.Error.WriteLine("no model"); return 1; }
		HeldStop? stop = null;
		var (cases, right, asked, wrong, missed, given) = (0, 0, 0, 0, 0, 0);
		var (yes, wrongSure, givenSure) = (0, 0, 0);
		foreach (var line in File.ReadAllLines(args[1]).Where(l => l.Length > 0 && !l.StartsWith('#')))
		{
			if (line.StartsWith("= ", StringComparison.Ordinal))
			{
				stop = new HeldStop(LinesKind, [], [.. line[2..].Split(" | ").Select((l, i) => (l.Trim(), new[] { "talk", $"{i + 1}" }))]);
				continue;
			}
			if (stop == null || line.Split('\t') is not [var sentence, var want]) continue;
			cases++;
			var wanted = int.Parse(want) - 1;
			var said = LinesSaid(stop, sentence);
			string verdict;
			if (said is not { Places.Count: > 0 }) { verdict = wanted < 0 ? "ok" : "MISSED"; if (wanted < 0) right++; else missed++; }
			else if (wanted < 0) { verdict = said.Sure && said.Places.Count == 1 ? "GIVEN" : "GIVEN(asked)"; given++; if (said.Sure && said.Places.Count == 1) givenSure++; }
			else if (said.Places.Count > 1) { verdict = said.Places.Contains(wanted) ? "asked" : "WRONG(asked)"; if (said.Places.Contains(wanted)) asked++; else wrong++; }
			else if (said.Places[0] == wanted) { verdict = said.Sure ? "ok" : "ok(yes)"; right++; if (!said.Sure) yes++; }
			else { verdict = said.Sure ? "WRONG" : "WRONG(yes)"; wrong++; if (said.Sure) wrongSure++; }
			if (verdict != "ok" || Environment.GetEnvironmentVariable("FM_BENCH_ALL") == "1")
				Console.WriteLine($"  {verdict,-12} {sentence,-48} want {want} got {string.Join(",", said?.Places.Select(p => p + 1) ?? [])}  {LastLineSims}");
		}
		Console.WriteLine($"{right}/{cases} right ({100.0 * right / Math.Max(cases, 1):0.0}%; {yes} of them on a yes), asked between (right among them) {asked}, wrong {wrong} ({wrongSure} without asking), missed {missed}, answered where none was meant {given} ({givenSure} without asking)");
		return 0;
	}
}
