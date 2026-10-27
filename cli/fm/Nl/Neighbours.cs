using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
	private readonly float[][] _index;
	private readonly int _k;

	public Neighbours(Embedder embedder, List<(string Text, string Call)> examples, string cacheDir, int k = 5)
	{
		_embedder = embedder;
		_examples = examples;
		_k = k;
		_index = Cached(cacheDir, examples) ?? Build(cacheDir, examples);
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
			// an example must not find itself, or the dev score says nothing
			if (_examples[j].Text == text) continue;
			top.Add((Embedder.Dot(q, _index[j]), j));
		}
		var votes = new Dictionary<string, float>();
		foreach (var (sim, at) in top.OrderByDescending(t => t.Sim).Take(k))
			votes[_examples[at].Call] = votes.GetValueOrDefault(_examples[at].Call) + sim;
		var total = votes.Values.Sum();
		return [.. votes.OrderByDescending(v => v.Value).Select(v => (v.Key, v.Value / total))];
	}

	public float[][] Embed(IReadOnlyList<string> texts) => _embedder.Embed(texts);

	private float[][] Build(string cacheDir, List<(string Text, string Call)> examples)
	{
		var index = _embedder.Embed([.. examples.Select(e => e.Text)]);
		try
		{
			using var w = new BinaryWriter(File.Create(CachePath(cacheDir, examples)));
			w.Write(index.Length);
			w.Write(index[0].Length);
			foreach (var v in index)
				foreach (var x in v) w.Write(x);
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
		return index;
	}

	private static float[][]? Cached(string cacheDir, List<(string Text, string Call)> examples)
	{
		var path = CachePath(cacheDir, examples);
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

	private static string CachePath(string cacheDir, List<(string Text, string Call)> examples)
	{
		var model = new FileInfo(Path.Combine(cacheDir, "model.onnx"));
		var key = $"one-by-one|{model.Length}|{model.LastWriteTimeUtc.Ticks}|" + string.Join('\n', examples.Select(e => e.Text));
		var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)))[..12].ToLowerInvariant();
		return Path.Combine(cacheDir, $"index-{hash}.bin");
	}

	/// <summary>
	/// The example sentences of a tests/nl folder: its dev cases (held ones, a hash of the
	/// sentence like tools/nl-score.py, are left out unless asked for) and its train/ sentences.
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
				if (dev && !withHeld && Held(text)) continue;
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

	/// <summary>The held 30% of tests/nl: never in the index while scoring.</summary>
	public static bool Held(string text)
	{
		var h = SHA1.HashData(Encoding.UTF8.GetBytes(text));
		// the whole 160-bit hash as a number, mod 10 (as Python's int(hexdigest, 16) % 10)
		var rem = 0;
		foreach (var b in h) rem = (rem * 256 + b) % 10;
		return rem < 3;
	}

	private static string Canonical(JsonNode? call) => call?.ToJsonString() ?? "null";
}
