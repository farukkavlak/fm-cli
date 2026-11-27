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
	/// Whether the rule split stands against the nearest examples. A split only at commas or
	/// "ve"/"and" is dropped when the whole sentence's pick is none of the parts' tools and some
	/// part's tool is not among that part's own three nearest: the parts are then the pick's
	/// arguments ("sol bek ara, 23 yaş altı, 2 milyona kadar"), not asks of their own
	/// ("kaydet ve devam et" stays split).
	/// </summary>
	private static bool SplitHolds(string text, Result r, List<(string Tool, float Weight)> tools, Neighbours near)
	{
		if (r.Calls.Count < 2) return false;
		if (new Text(text).Words.Any(w => Strong.Contains(w))) return true;
		if (tools.FirstOrDefault() is not { Weight: >= Unsure } top || r.Calls.Any(c => c.Tool == top.Tool)) return true;
		var parts = Parts(text);
		if (parts.Count != r.Calls.Count) return true;
		return parts.Zip(r.Calls).All(pc => near.Candidates(pc.First.Text, null).Take(3).Any(c => c.Tool == pc.Second.Tool));
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

	/// <summary>
	/// What a typed sentence was taken to mean: the calls to make, or the choices to offer.
	/// <paramref name="Tools"/> are the tools the nearest examples voted for, best first (what a
	/// pick is told apart from when it is learned).
	/// </summary>
	/// <paramref name="Pick"/> is the tool the nearest examples picked (null for none).
	public sealed record Understood(List<Call> Calls, List<(Call Call, float Weight)> Options, List<string> Tools, string? Pick = null, bool FromCloud = false);

	/// <summary>The weight an option the cloud model proposed is shown with: it has no share of the vote.</summary>
	public const float FromCloudWeight = -1;

	/// <summary>How many of the nearest examples' tools the cloud model is shown.</summary>
	public const int CloudTools = 10;

	/// <summary>
	/// Smart mode (T207): a sentence the router is not sure of, put to the cloud model with the
	/// tools the nearest examples point at. Its pick gets our reader's arguments, then the model's
	/// where Cloud.Merge allows. When it picks the router's own pick, the two agree and it is taken
	/// as meant; when it picks another, that one is offered first with the router's. Null when it
	/// picks none or cannot be asked: the router's own choices stand (T207 in docs/AI.md).
	/// </summary>
	public static async Task<Understood?> AskCloud(string text, Neighbours near, string? seen, Cloud.Settings settings, Understood local)
	{
		if (local.Calls.Count > 0) return null;
		var tools = near.Candidates(seen ?? text, null).Select(c => c.Tool).Take(CloudTools)
			.Concat(local.Options.Select(o => o.Call.Tool)).Distinct()
			.Select(t => (Name: t, Card: Mcp.Card(t))).Where(t => t.Card != null)
			.Select(t => (t.Name, t.Card!.Value.Description, t.Card.Value.Schema)).ToList();
		var proposal = await Cloud.Propose(settings, text, tools);
		if (proposal.Tool is not { } tool) return null;
		var call = Fill(tool, new Text(text), 0);
		Cloud.Merge(call, proposal.Args, text);
		if (tool == local.Pick) return local with { Calls = [call], Options = [], FromCloud = true };
		return local with { Calls = [], Options = [(call, FromCloudWeight), .. local.Options.Where(o => o.Call.Tool != tool)], FromCloud = true };
	}

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
		seen ??= text;
		var vector = near.Embed([seen])[0];
		var rank = near.Rank(seen, vector, 15);
		var tools = rank.Where(v => v.Call.StartsWith('"')).Select(v => (Tool: JsonNode.Parse(v.Call)!.GetValue<string>(), v.Weight)).ToList();
		if (SplitHolds(text, r, tools, near)) return new Understood(r.Calls, [], []);
		var pick = near.Pick(seen, vector);
		if (pick.StartsWith('[')) return new Understood(r.Calls, [], []);
		List<string> voted = [.. tools.Select(t => t.Tool).Take(5)];
		if (pick == "null")
			return new Understood([], [.. tools.Where(t => t.Weight >= Maybe).Take(3).Select(t => (Fill(t.Tool, new Text(text), 0), t.Weight))], voted);
		var tool = JsonNode.Parse(pick)!.GetValue<string>();
		var weight = tools.FirstOrDefault(t => t.Tool == tool).Weight;
		if (weight >= Sure) return new Understood([Fill(tool, new Text(text), 0)], [], voted, tool);
		var n = weight >= Unsure ? 3 : 5;
		// the pick is among them however the wider vote ranks it; shown by share
		var options = tools.Where(t => t.Tool == tool).Concat(tools.Where(t => t.Tool != tool)).Take(n).OrderByDescending(t => t.Weight);
		return new Understood([], [.. options.Select(t => (Fill(t.Tool, new Text(text), 0), t.Weight))], voted, tool);
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
	/// 15 nearest, the one Understand holds against Sure. --learned FILE adds a learned.json's
	/// sentences as the user's (tools/nl-learn.py), --learned-weight and --learned-near try other values.
	/// --cloud asks the cloud model set up (FM_AI_*) where the router is not sure: "cloud" when it
	/// agreed (its call in "call"), "cloud_first" for a pick it would put first, "cloud_s" the wait
	/// (tools/nl-cloud.py).
	/// </summary>
	public static int RouteLines(string[] args) => RouteLinesAsync(args).GetAwaiter().GetResult();

	private static async Task<int> RouteLinesAsync(string[] args)
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
		var whole = new bool[texts.Count];
		var clouds = new Understood?[texts.Count];
		var cloudSeconds = new double?[texts.Count];
		if (Opt("--model") is { } model)
		{
			model = Path.GetFullPath(model.Replace("~", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
			using var embedder = new Embedder(model);
			var near = new Neighbours(embedder, Neighbours.Examples(Opt("--cases") ?? "tests/nl", withHeld: false), model);
			// tools/nl-learn.py: sentences taught as if the user had picked them
			if (Opt("--learned-weight") is { } lw) Neighbours.LearnedWeight = float.Parse(lw, System.Globalization.CultureInfo.InvariantCulture);
			if (Opt("--learned-near") is { } ln) Neighbours.LearnedNear = float.Parse(ln, System.Globalization.CultureInfo.InvariantCulture);
			if (Opt("--learned") is { } learned) near.Learn(Learned.Examples(Learned.Load(learned)), model);
			var vectors = near.Embed([.. texts.Select(t => t.Text)]);
			for (var i = 0; i < texts.Count; i++)
			{
				picks[i] = near.Pick(texts[i].Text, vectors[i]);
				// the pick's share of the 15 nearest, as Understand weighs it against Sure
				var fifteen = near.Rank(texts[i].Text, vectors[i], 15);
				shares[i] = fifteen.FirstOrDefault(v => v.Call == picks[i]).Weight;
				var r15 = Route(texts[i].Text);
				whole[i] = r15.Calls.Count > 1 && !SplitHolds(texts[i].Text, r15,
					[.. fifteen.Where(v => v.Call.StartsWith('"')).Select(v => (JsonNode.Parse(v.Call)!.GetValue<string>(), v.Weight))], near);
				ranks[i] = [.. near.Candidates(texts[i].Text, vectors[i]).Select(c => (JsonSerializer.Serialize(c.Tool), c.Weight))];
				var asks = Asks(texts[i].Text);
				if (asks.Count > 1)
				{
					perAsk[i] = [.. asks.Select(a => near.Candidates(a, null).Select(c => c.Tool).Take(5).ToList())];
					askTexts[i] = asks;
				}
			}
			// --cloud: smart mode, the cloud model (FM_AI_URL, FM_AI_KEY, FM_AI_MODEL) asked where the router is not sure
			if (args.Contains("--cloud") && Cloud.Current() is { } settings)
			{
				using var gate = new SemaphoreSlim(4);
				await Task.WhenAll(Enumerable.Range(0, texts.Count).Select(async i =>
				{
					var local = Understand(texts[i].Text, near);
					if (local.Calls.Count > 0) return;
					await gate.WaitAsync();
					try
					{
						var started = Environment.TickCount64;
						clouds[i] = await AskCloud(texts[i].Text, near, null, settings, local);
						cloudSeconds[i] = (Environment.TickCount64 - started) / 1000.0;
					}
					finally { gate.Release(); }
				}));
			}
		}

		using var stdout = Console.OpenStandardOutput();
		using var w = new StreamWriter(stdout) { AutoFlush = false };
		for (var i = 0; i < texts.Count; i++)
		{
			var (text, forcedTool) = texts[i];
			var r = Route(text);
			if (whole[i]) r = new Result([], r.Others);
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
			if (cloudSeconds[i] is { } cs)
			{
				o["cloud_s"] = Math.Round(cs, 2);
				if (clouds[i] is { Options: [var first, ..] } && first.Weight == FromCloudWeight)
					o["cloud_first"] = first.Call.Tool;
				if (clouds[i] is { Calls: [var c] })
				{
					o["call"] = c.Tool;
					o["args"] = (JsonObject)c.Args.DeepClone();
					o.Remove("ask");
					if (c.Ask.Count > 0) o["ask"] = new JsonArray([.. c.Ask.Select(a => (JsonNode)a)]);
					o["cloud"] = true;
				}
			}
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
