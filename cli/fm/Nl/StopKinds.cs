using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// What a sentence at a stop asks done (accept, reject, stall, see, play, straight to the result …), by the
/// model, not by word lists: the sentence is set against example sentences for each kind of answer
/// (stop-answers.tsv) and against the router's own examples, which stand for anything else. The nearest
/// vote; only a kind the stop has an answer for can win, and only by a clear share. Endings, typos and ways
/// of saying it the examples do not hold are the model's to bridge.
/// </summary>
internal sealed class StopKinds
{
	private readonly Neighbours _router;
	private readonly Neighbours _kinds;

	private const string Prefix = "stop:";

	public StopKinds(Neighbours router, Embedder embedder, string cacheDir)
	{
		_router = router;
		_kinds = new Neighbours(embedder, Examples(), cacheDir);
	}

	/// <summary>The example sentences, each under "stop:kind".</summary>
	public static List<(string Text, string Call)> Examples()
	{
		using var stream = typeof(StopKinds).Assembly.GetManifestResourceStream("stop-answers.tsv")!;
		using var reader = new StreamReader(stream);
		var rows = new List<(string, string)>();
		while (reader.ReadLine() is { } line)
			if (line.Length > 0 && !line.StartsWith('#') && line.Split('\t') is [var kind, var text])
				rows.Add((text, Prefix + kind));
		return rows;
	}

	/// <summary>
	/// The kind of answer the sentence asks among the ones given, or null: something else, or not clear.
	/// <paramref name="theirs"/> are the router's tools the stop's own answers run: their examples ask the same
	/// things in other words (fm_offers "teklifi kabul et"), so they do not stand for anything else here.
	/// </summary>
	public (List<string> Kinds, bool Sure, List<(string Label, float Share)> Votes) Ask(string sentence, IReadOnlySet<string> kinds, IReadOnlySet<string> theirs)
	{
		var q = _kinds.Embed([sentence])[0];
		bool Other(string call) => !Tools(call).Any(theirs.Contains);
		// Each kind by how near its nearest examples come; anything else by the router's nearest.
		static float Near(IEnumerable<float> sims) => sims.OrderDescending().Take(Top).DefaultIfEmpty(0).Average();
		var by = _kinds.Nearest(q, 200).GroupBy(n => n.Call[Prefix.Length..]).ToDictionary(g => g.Key, g => Near(g.Select(n => n.Sim)));
		// Anything else: the router's examples, and the questions asked at a stop ("-" in stop-answers.tsv).
		var other = Math.Max(Near(_router.Nearest(q, Top, Other).Select(n => n.Sim)), by.GetValueOrDefault("-"));
		by.Remove("-");
		var ranked = by.Select(b => (Label: b.Key, Share: b.Value)).Append((Label: "-", Share: other)).OrderByDescending(r => r.Share).ToList();
		// The best kind the stop has, ahead of anything else (by the bias) and of the kinds it has not; the kinds it
		// has within the margin of the best are asked between, not guessed ("decline" came near "accept").
		var mine = ranked.Where(r => kinds.Contains(r.Label)).ToList();
		if (mine.Count == 0) return ([], false, ranked);
		var best = mine[0];
		if (best.Share < Floor || best.Share < other - Bias || ranked.Any(r => r.Label != "-" && !kinds.Contains(r.Label) && r.Share > best.Share)) return ([], false, ranked);
		List<string> asked = [.. mine.Where(r => r.Share >= best.Share - Margin && r.Share >= other - Bias).Select(r => r.Label)];
		// Sure: ahead of every other reading, anything else included, by a clear gap; else it is asked about first.
		var sure = asked.Count == 1 && best.Share - ranked.Where(r => r.Label != best.Label).Select(r => r.Label == "-" ? r.Share - SureBias : r.Share).DefaultIfEmpty(0).Max() >= SureGap;
		return (asked, sure, ranked);
	}

	/// <summary>How near a kind's nearest example must come at least: below it the sentence is too far from all of them to say.</summary>
	public static float Floor = 0f;

	/// <summary>
	/// The gap to every other reading (anything else counted less by <see cref="SureBias"/>) from which the answer
	/// runs without a yes asked first. Measured on tests/nl/stops with noisy forms: 15 of 18 wrong readings came
	/// closer than this, and 67 of 241 right ones.
	/// </summary>
	public static float SureGap = 0.05f;

	public static float SureBias = 0.05f;

	/// <summary>How many of the nearest examples of a kind make its nearness.</summary>
	public static int Top = 1;

	/// <summary>How much nearer than anything else a kind must be: a kind's examples are few beside the router's.</summary>
	public static float Bias = 0.1f;

	/// <summary>How much nearer than every other kind the best one must be.</summary>
	public static float Margin = 0.02f;

	private static IEnumerable<string> Tools(string call)
	{
		if (call.StartsWith(Prefix, StringComparison.Ordinal)) return [];
		var node = JsonNode.Parse(call);
		return node is JsonArray a ? a.Select(x => x!.GetValue<string>()) : node is JsonValue v ? [v.GetValue<string>()] : [];
	}
}
