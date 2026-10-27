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

	/// <summary>Two or more asks in one sentence, when every part names its own tool.</summary>
	private static List<Call>? Split(string sentence, double wholeScore)
	{
		var raw = Text.Fold(sentence).Replace(",", " , ");
		var words = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		var cuts = words.Select((w, i) => (w, i)).Where(x => Strong.Contains(x.w) || Weak.Contains(x.w)).ToList();
		if (cuts.Count == 0) return null;
		var parts = new List<(string Text, bool Strong)>();
		var start = 0;
		foreach (var (w, i) in cuts)
		{
			parts.Add((string.Join(' ', words[start..i]), Strong.Contains(w)));
			start = i + 1;
		}
		parts.Add((string.Join(' ', words[start..]), false));
		parts = [.. parts.Where(p => p.Text.Length > 0)];
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

	/// <summary>
	/// `fm __route`: one {"text": "..."} per line in, one {"call", "args", "ask", "others"} per
	/// line out. tools/nl-score.py runs it over the test set. A line that also names a "tool"
	/// (picked by another router, tools/nl-embed.py --hybrid) gets that tool's arguments filled
	/// by its reader as "forced", next to the rule router's own answer. With --model DIR the
	/// nearest example sentences pick the tool and the rules only fill its arguments (a sentence
	/// the rules split into several asks stays split); --cases DIR is the tests/nl folder whose
	/// dev and train sentences are the examples.
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
		if (Opt("--model") is { } model)
		{
			model = Path.GetFullPath(model.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
			using var embedder = new Embedder(model);
			var near = new Neighbours(embedder, Neighbours.Examples(Opt("--cases") ?? "tests/nl", withHeld: false), model);
			var vectors = near.Embed([.. texts.Select(t => t.Text)]);
			for (var i = 0; i < texts.Count; i++) picks[i] = near.Pick(texts[i].Text, vectors[i]);
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
