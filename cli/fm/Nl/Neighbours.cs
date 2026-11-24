using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// Picks the tool by the nearest known sentences: every example sentence is embedded once
/// (kept beside the model, keyed by the sentences and the model), a typed sentence is embedded
/// and its k nearest examples vote, each by its similarity. The vote is the example's "call"
/// as written in tests/nl: a tool name, null for "nothing does this", or a list for several asks.
/// </summary>
internal sealed class Neighbours
{
	private readonly Embedder _embedder;
	private readonly List<(string Text, string Call)> _examples;
	private readonly float[][] _shippedIndex;
	private float[][] _index;
	private float[] _weight;
	private readonly int _shipped;
	private readonly int _k;

	/// <summary>How much more a sentence the user taught counts than a shipped example (T206 in docs/AI.md).</summary>
	public static float LearnedWeight = 2f;

	/// <summary>From this similarity up, a taught sentence that is the nearest of all decides (T206 in docs/AI.md).</summary>
	public static float LearnedNear = 0.9f;

	/// <summary>The share a deciding taught sentence gets: enough to run without offering a list.</summary>
	private const float LearnedShare = 0.85f;

	public Neighbours(Embedder embedder, List<(string Text, string Call)> examples, string cacheDir, int k = 5)
	{
		_embedder = embedder;
		_examples = [.. examples];
		_shipped = examples.Count;
		_k = k;
		_index = _shippedIndex = Cached(cacheDir, examples) ?? Build(cacheDir, examples);
		_weight = [.. Enumerable.Repeat(1f, _shipped)];
	}

	/// <summary>
	/// The user's own sentences (Learned), counted <see cref="LearnedWeight"/> times a shipped
	/// example's, in place of those given before. Unlike a shipped example, one the user types
	/// again finds itself.
	/// </summary>
	public void Learn(List<(string Text, string Call)> learned, string cacheDir)
	{
		_examples.RemoveRange(_shipped, _examples.Count - _shipped);
		var index = learned.Count == 0 ? [] : Cached(cacheDir, learned, "learned") ?? Build(cacheDir, learned, "learned");
		_examples.AddRange(learned);
		_index = [.. _shippedIndex, .. index];
		_weight = [.. Enumerable.Repeat(1f, _shipped), .. Enumerable.Repeat(LearnedWeight, learned.Count)];
	}

	/// <summary>The winning call as JSON (a tool name in quotes, "null" or a list).</summary>
	public string Pick(string text, float[]? vector = null) => Rank(text, vector, _k)[0].Call;

	/// <summary>
	/// The calls the nearest <paramref name="k"/> examples vote for, best first, each with its
	/// share of the vote (the shares add up to one).
	/// </summary>
	public List<(string Call, float Weight)> Rank(string text, float[]? vector, int k)
	{
		var q = vector ?? _embedder.Embed([text])[0];
		var top = new List<(float Sim, int At)>();
		for (var j = 0; j < _index.Length; j++)
		{
			// a shipped example must not find itself, or the dev score says nothing
			if (j < _shipped && _examples[j].Text == text) continue;
			top.Add((Embedder.Dot(q, _index[j]), j));
		}
		var votes = new Dictionary<string, float>();
		var nearest = top.OrderByDescending(t => t.Sim).Take(k).ToList();
		foreach (var (sim, at) in nearest)
			votes[_examples[at].Call] = votes.GetValueOrDefault(_examples[at].Call) + sim * _weight[at];
		var total = votes.Values.Sum();
		var ranked = votes.OrderByDescending(v => v.Value).Select(v => (v.Key, v.Value / total)).ToList();
		// a sentence the user taught, nearer than any shipped one and near enough, is what they mean
		if (nearest.Count > 0 && nearest[0].At >= _shipped && nearest[0].Sim >= LearnedNear)
		{
			var call = _examples[nearest[0].At].Call;
			var share = ranked.First(r => r.Key == call).Item2;
			var raised = Math.Max(share, LearnedShare);
			var rest = share < 1 ? (1 - raised) / (1 - share) : 0;
			ranked = [(call, raised), .. ranked.Where(r => r.Key != call).Select(r => (r.Key, r.Item2 * rest))];
		}
		return ranked;
	}

	/// <summary>
	/// The tools the nearest <paramref name="pool"/> examples point at, best first, each by the
	/// sum of its examples' similarities: a list example counts for each of its tools, "nothing
	/// does this" for none. A wider pool than the vote's finds the right tool more often
	/// (tests/nl/README.md); the summing rule matters less than the pool size.
	/// </summary>
	public List<(string Tool, float Weight)> Candidates(string text, float[]? vector, int pool = 50)
	{
		var q = vector ?? _embedder.Embed([text])[0];
		var near = new List<(float Sim, int At)>();
		for (var j = 0; j < _index.Length; j++)
			if (j >= _shipped || _examples[j].Text != text) near.Add((Embedder.Dot(q, _index[j]), j));
		var by = new Dictionary<string, float>();
		foreach (var (sim, at) in near.OrderByDescending(t => t.Sim).Take(pool))
		{
			var call = JsonNode.Parse(_examples[at].Call);
			var tools = call is JsonArray a ? a.Select(x => x!.GetValue<string>()) : call is JsonValue v ? [v.GetValue<string>()] : [];
			foreach (var t in tools.Distinct())
				by[t] = by.GetValueOrDefault(t) + sim * _weight[at];
		}
		var total = by.Values.Sum();
		return [.. by.OrderByDescending(v => v.Value).Select(v => (v.Key, v.Value / total))];
	}

	public float[][] Embed(IReadOnlyList<string> texts) => _embedder.Embed(texts);

	private float[][] Build(string cacheDir, List<(string Text, string Call)> examples, string kind = "index")
	{
		var index = _embedder.Embed([.. examples.Select(e => e.Text)]);
		try
		{
			var path = CachePath(cacheDir, examples, kind);
			// the user's sentences change as they teach: only the index of the current ones is kept
			if (kind != "index")
				foreach (var old in Directory.GetFiles(cacheDir, $"{kind}-*.bin")) File.Delete(old);
			using var w = new BinaryWriter(File.Create(path));
			w.Write(index.Length);
			w.Write(index[0].Length);
			foreach (var v in index)
				foreach (var x in v) w.Write(x);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
		return index;
	}

	private static float[][]? Cached(string cacheDir, List<(string Text, string Call)> examples, string kind = "index")
	{
		var path = CachePath(cacheDir, examples, kind);
		if (!File.Exists(path)) return null;
		using var r = new BinaryReader(File.OpenRead(path));
		var n = r.ReadInt32();
		var dim = r.ReadInt32();
		if (n != examples.Count) return null;
		var index = new float[n][];
		for (var i = 0; i < n; i++)
		{
			index[i] = new float[dim];
			for (var d = 0; d < dim; d++) index[i][d] = r.ReadSingle();
		}
		return index;
	}

	private static string CachePath(string cacheDir, List<(string Text, string Call)> examples, string kind = "index")
	{
		var model = new FileInfo(Path.Combine(cacheDir, "model.onnx"));
		var key = $"lower|one-by-one|{model.Length}|{model.LastWriteTimeUtc.Ticks}|" + string.Join('\n', examples.Select(e => e.Text));
		var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();
		return Path.Combine(cacheDir, $"{kind}-{hash}.bin");
	}

	/// <summary>
	/// The example sentences of a tests/nl folder: its dev cases (held and final ones, by their
	/// "split", are left out unless asked for) and its train/ sentences.
	/// </summary>
	public static List<(string Text, string Call)> Examples(string dir, bool withHeld)
	{
		var rows = new List<(string, string)>();
		void Read(string file, bool dev)
		{
			foreach (var line in File.ReadLines(file))
			{
				if (line.Trim().Length == 0) continue;
				var c = JsonNode.Parse(line)!.AsObject();
				var text = (c["tr"] ?? c["en"])!.GetValue<string>();
				if (dev && !withHeld && c["split"]?.GetValue<string>() != "dev") continue;
				rows.Add((text, Canonical(c["call"])));
			}
		}
		foreach (var f in Directory.GetFiles(dir, "*.jsonl").Order(StringComparer.Ordinal)) Read(f, true);
		var train = Path.Combine(dir, "train");
		if (Directory.Exists(train))
			foreach (var f in Directory.GetFiles(train, "*.jsonl").Order(StringComparer.Ordinal)) Read(f, false);
		return rows;
	}

	/// <summary>Every example sentence the CLI carries (tests/nl, held ones too, and its train/ sentences).</summary>
	public static List<(string Text, string Call)> Embedded()
	{
		var asm = typeof(Neighbours).Assembly;
		var rows = new List<(string, string)>();
		var names = asm.GetManifestResourceNames().Where(n => n.StartsWith("nl/", StringComparison.Ordinal)).ToList();
		foreach (var name in names.Where(n => !n.StartsWith("nl/train/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)
			.Concat(names.Where(n => n.StartsWith("nl/train/", StringComparison.Ordinal)).Order(StringComparer.Ordinal)))
		{
			using var reader = new StreamReader(asm.GetManifestResourceStream(name)!);
			while (reader.ReadLine() is { } line)
			{
				if (line.Trim().Length == 0) continue;
				var c = JsonNode.Parse(line)!.AsObject();
				rows.Add(((c["tr"] ?? c["en"])!.GetValue<string>(), Canonical(c["call"])));
			}
		}
		return rows;
	}

	/// <summary>Whether the examples are embedded already for this model (else the first use takes a while).</summary>
	public static bool Indexed(string cacheDir, List<(string Text, string Call)> examples) => File.Exists(CachePath(cacheDir, examples));

	private static string Canonical(JsonNode? call) => call?.ToJsonString() ?? "null";
}
