using System.Text.Json;
using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// The rule router: a typed sentence becomes the MCP call it asks for, or none. Every tool has
/// cues (words and short phrases, each with a weight); the tool whose cues add up highest wins,
/// and its own reader fills the arguments from the sentence. Below a score of one nothing is
/// called. A sentence with "sonra", "then" or "ve"/"and" between two asks is split when each
/// half stands on its own. What a call needs and the sentence does not say is asked, not
/// guessed (see tests/nl/README.md).
/// </summary>
internal static partial class Router
{
	public sealed record Call(string Tool, JsonObject Args, List<string> Ask, double Score);

	public sealed record Result(List<Call> Calls, List<(string Tool, double Score)> Others);

	private const double Enough = 1.0;
	private static readonly string[] Strong = ["sonra", "ardindan", "then", "afterwards"];
	private static readonly string[] Weak = ["ve", "and", ","];

	public static Result Route(string sentence)
	{
		var whole = Score(new Text(sentence));
		var best = whole.FirstOrDefault();
		var split = Split(sentence, best.Score);
		if (split != null) return new Result(split, []);
		if (best.Tool is null or NoTool || best.Score < Enough) return new Result([], [.. whole.Where(w => w.Tool != NoTool).Take(3)]);
		return new Result([Fill(best.Tool, new Text(sentence), best.Score)], [.. whole.Skip(1).Where(w => w.Tool != NoTool).Take(2)]);
	}

	private static List<(string Tool, double Score)> Score(Text text)
	{
		var ctx = new Ctx(text);
		var scores = new List<(string, double)>();
		foreach (var rule in Rules)
		{
			var s = 0.0;
			foreach (var cue in rule.Cues)
				if (ctx.HasCue(cue.Words)) s += cue.Weight;
			if (s > 0) scores.Add((rule.Tool, s));
		}
		return [.. scores.OrderByDescending(s => s.Item2)];
	}

	/// <summary>
	/// The sentence cut at "sonra", "then", "ve", "and" and commas, in its own words; each part
	/// says whether a strong cut ("sonra", "then") came after it.
	/// </summary>
	private static List<(string Text, bool Strong)> Parts(string sentence)
	{
		var words = sentence.Replace(",", " , ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var parts = new List<(string Text, bool Strong)>();
		var start = 0;
		for (var i = 0; i < words.Length; i++)
		{
			var w = Text.Fold(words[i]);
			if (!Strong.Contains(w) && !Weak.Contains(w)) continue;
			parts.Add((string.Join(' ', words[start..i]), Strong.Contains(w)));
			start = i + 1;
		}
		parts.Add((string.Join(' ', words[start..]), false));
		return [.. parts.Where(p => p.Text.Length > 0)];
	}

	/// <summary>Two or more asks in one sentence, when every part names its own tool.</summary>
	private static List<Call>? Split(string sentence, double wholeScore)
	{
		var parts = Parts(sentence);
		if (parts.Count < 2) return null;

		var routed = parts.Select(p => (p.Text, p.Strong, Best: Score(new Text(p.Text)).FirstOrDefault())).ToList();
		// a part that names no tool belongs with the one before it ("rinaldi ve tosto dinlensin" stays whole)
		if (routed.Any(r => r.Best.Tool is null or NoTool || r.Best.Score < Enough)) return null;
		var tools = routed.Select(r => r.Best.Tool).ToList();
		var anyStrong = routed.Any(r => r.Strong);
		if (!anyStrong && tools.Distinct().Count() < tools.Count) return null;
		if (routed.Sum(r => r.Best.Score) + 0.01 < wholeScore) return null;
		return [.. routed.Select(r => Fill(r.Best.Tool, new Text(r.Text), r.Best.Score))];
	}

	/// <summary>
	/// The asks of a sentence, each in its own words, for one candidate list per ask: the parts
	/// of the rule split when it holds, else the whole sentence. Splitting at every cut, or where
	/// the nearest examples vote for several asks, did no better (tests/nl/README.md).
	/// </summary>
	public static List<string> Asks(string sentence) =>
		Route(sentence).Calls.Count > 1 ? [.. Parts(sentence).Select(p => p.Text)] : [sentence];

	private static Call Fill(string tool, Text text, double score)
	{
		var ctx = new Ctx(text, tool);
		Rules.FirstOrDefault(r => r.Tool == tool)?.Args?.Invoke(ctx);
		foreach (var need in Required.GetValueOrDefault(tool, []))
			ctx.Ask(need);
		foreach (var (_, when, unless, need) in Needs.Where(n => n.Tool == tool))
			if (ctx.Args.ContainsKey(when) && (unless is null || !ctx.Args.ContainsKey(unless))) ctx.Ask(need);
		return new Call(tool, ctx.Args, ctx.Asks, score);
	}

	/// <summary>What a typed sentence was taken to mean: the calls to make, or the choices to offer.</summary>
	public sealed record Understood(List<Call> Calls, List<(Call Call, float Weight)> Options);

	/// <summary>From this share of the nearest examples' vote up, the pick is taken as meant.</summary>
	public const float Sure = 0.8f;
	/// <summary>Below the sure share three choices are offered, below this one five.</summary>
	public const float Unsure = 0.5f;
	/// <summary>When "nothing does this" wins, a tool with this share of the vote is still offered.</summary>
	public const float Maybe = 0.2f;

	/// <summary>
	/// The model picks the tool by the nearest examples and the rules fill its arguments; a
	/// sentence the rules split into several asks stays split. A pick with less than the sure
	/// share of the vote is offered with the runners-up (tests/nl/README.md has the measurements).
	/// </summary>
	/// <remarks>
	/// <paramref name="seen"/> is the sentence as the model is to see it: the save's people put in
	/// as a name it was taught with (<see cref="NameFit.StandIn"/>). It knows "rinaldiye bak" and
	/// not "hreljaya bak"; the arguments are still read from the words typed.
	/// </remarks>
	public static Understood Understand(string text, Neighbours near, string? seen = null)
	{
		var r = Route(text);
		if (r.Calls.Count > 1) return new Understood(r.Calls, []);
		seen ??= text;
		var vector = near.Embed([seen])[0];
		var pick = near.Pick(seen, vector);
		var rank = near.Rank(seen, vector, 15);
		if (pick.StartsWith('[')) return new Understood(r.Calls, []);
		var tools = rank.Where(v => v.Call.StartsWith('"')).Select(v => (Tool: JsonNode.Parse(v.Call)!.GetValue<string>(), v.Weight)).ToList();
		if (pick == "null")
			return new Understood([], [.. tools.Where(t => t.Weight >= Maybe).Take(3).Select(t => (Fill(t.Tool, new Text(text), 0), t.Weight))]);
		var tool = JsonNode.Parse(pick)!.GetValue<string>();
		var weight = tools.FirstOrDefault(t => t.Tool == tool).Weight;
		if (weight >= Sure) return new Understood([Fill(tool, new Text(text), 0)], []);
		var n = weight >= Unsure ? 3 : 5;
		// the pick is among them however the wider vote ranks it; shown by share
		var options = tools.Where(t => t.Tool == tool).Concat(tools.Where(t => t.Tool != tool)).Take(n).OrderByDescending(t => t.Weight);
		return new Understood([], [.. options.Select(t => (Fill(t.Tool, new Text(text), 0), t.Weight))]);
	}

	/// <summary>
	/// `fm __route`: one {"text": "..."} per line in, one {"call", "args", "ask", "others"} per
	/// line out. tools/nl-score.py runs it over the test set. A line that also names a "tool"
	/// (picked by another router, tools/nl-embed.py --hybrid) gets that tool's arguments filled
	/// by its reader as "forced", next to the rule router's own answer. With --model DIR the
	/// nearest example sentences pick the tool and the rules only fill its arguments (a sentence
	/// the rules split into several asks stays split); --cases DIR is the tests/nl folder whose
	/// dev and train sentences are the examples; "votes" then lists the five best candidate
	/// tools of the 50 nearest examples, and "asks" the five best of each ask when the sentence
	/// splits into several, with "ask_texts" the words of each; "share" is the pick's share of the
	/// 15 nearest, the one Understand holds against Sure.
	/// </summary>
	public static int RouteLines(string[] args)
	{
		string? Opt(string name) => Array.IndexOf(args, name) is var i and >= 0 && i + 1 < args.Length ? args[i + 1] : null;
		var texts = new List<(string Text, string? Tool)>();
		string? line;
		while ((line = Console.ReadLine()) != null)
		{
			if (line.Trim().Length == 0) continue;
			var input = JsonNode.Parse(line);
			texts.Add((input?["text"]?.GetValue<string>() ?? "", input?["tool"] is JsonValue t ? t.GetValue<string>() : null));
		}

		var picks = new string?[texts.Count];
		var ranks = new List<(string Call, float Weight)>?[texts.Count];
		var perAsk = new List<List<string>>?[texts.Count];
		var askTexts = new List<string>?[texts.Count];
		var shares = new float?[texts.Count];
		if (Opt("--model") is { } model)
		{
			model = Path.GetFullPath(model.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
			using var embedder = new Embedder(model);
			var near = new Neighbours(embedder, Neighbours.Examples(Opt("--cases") ?? "tests/nl", withHeld: false), model);
			var vectors = near.Embed([.. texts.Select(t => t.Text)]);
			for (var i = 0; i < texts.Count; i++)
			{
				picks[i] = near.Pick(texts[i].Text, vectors[i]);
				// the pick's share of the 15 nearest, as Understand weighs it against Sure
				shares[i] = near.Rank(texts[i].Text, vectors[i], 15).FirstOrDefault(v => v.Call == picks[i]).Weight;
				ranks[i] = [.. near.Candidates(texts[i].Text, vectors[i]).Select(c => (JsonSerializer.Serialize(c.Tool), c.Weight))];
				var asks = Asks(texts[i].Text);
				if (asks.Count > 1)
				{
					perAsk[i] = [.. asks.Select(a => near.Candidates(a, null).Select(c => c.Tool).Take(5).ToList())];
					askTexts[i] = asks;
				}
			}
		}

		using var stdout = Console.OpenStandardOutput();
		using var w = new StreamWriter(stdout) { AutoFlush = false };
		for (var i = 0; i < texts.Count; i++)
		{
			var (text, forcedTool) = texts[i];
			var r = Route(text);
			var o = new JsonObject();
			if (r.Calls.Count == 0) o["call"] = null;
			else if (r.Calls.Count == 1)
			{
				o["call"] = r.Calls[0].Tool;
				o["args"] = r.Calls[0].Args;
				if (r.Calls[0].Ask.Count > 0) o["ask"] = new JsonArray([.. r.Calls[0].Ask.Select(a => (JsonNode)a)]);
			}
			else
			{
				o["call"] = new JsonArray([.. r.Calls.Select(c => (JsonNode)c.Tool)]);
				o["args"] = new JsonArray([.. r.Calls.Select(c => (JsonNode)c.Args)]);
			}
			if (forcedTool is { Length: > 0 })
				o["forced"] = Forced(text, forcedTool);
			o["score"] = r.Calls.Count > 0 ? r.Calls.Max(c => c.Score) : 0;
			o["others"] = new JsonArray([.. r.Others.Select(x => (JsonNode)$"{x.Tool} {x.Score:0.#}")]);
			if (Environment.GetEnvironmentVariable("FM_NL_WORDS") != null) o["words"] = string.Join(' ', new Text(text).Words);
			if (picks[i] is { } pick && r.Calls.Count < 2 && !pick.StartsWith('['))
				o = pick == "null" ? new JsonObject { ["call"] = null } : Forced(text, JsonNode.Parse(pick)!.GetValue<string>());
			if (ranks[i] is { } rank)
				o["votes"] = new JsonArray([.. rank.Take(5).Select(v => (JsonNode)new JsonObject { ["call"] = JsonNode.Parse(v.Call), ["weight"] = Math.Round(v.Weight, 3) })]);
			if (perAsk[i] is { } pa)
				o["asks"] = new JsonArray([.. pa.Select(a => (JsonNode)new JsonArray([.. a.Select(t => (JsonNode)t)]))]);
			if (shares[i] is { } sh)
				o["share"] = Math.Round(sh, 3);
			if (askTexts[i] is { } at)
				o["ask_texts"] = new JsonArray([.. at.Select(t => (JsonNode)t)]);
			w.WriteLine(o.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
		}
		w.Flush();
		return 0;
	}

	private static JsonObject Forced(string text, string tool)
	{
		var c = Fill(tool, new Text(text), 0);
		var f = new JsonObject { ["call"] = c.Tool, ["args"] = c.Args };
		if (c.Ask.Count > 0) f["ask"] = new JsonArray([.. c.Ask.Select(a => (JsonNode)a)]);
		return f;
	}
}
