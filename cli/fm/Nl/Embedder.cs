using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace FmCli.Nl;

/// <summary>
/// A sentence as a vector, from a model tools/nl-train.py exported (a directory holding
/// model.onnx, tokenizer.json and prefix.txt). Sentences that ask the same tool land close
/// together, so the nearest known sentences vote for the tool (see <see cref="Neighbours"/>).
/// </summary>
internal sealed class Embedder : IDisposable
{
	private const int MaxTokens = 64;
	private readonly InferenceSession _session;
	private readonly Unigram _tok;
	private readonly string _prefix;

	public Embedder(string dir)
	{
		var so = new SessionOptions { IntraOpNumThreads = 4 };
		_session = new InferenceSession(Path.Combine(dir, "model.onnx"), so);
		_tok = Unigram.Load(Path.Combine(dir, "tokenizer.json"));
		var prefix = Path.Combine(dir, "prefix.txt");
		_prefix = File.Exists(prefix) ? File.ReadAllText(prefix) : "";
	}

	public void Dispose() => _session.Dispose();

	/// <summary>
	/// Unit-length vectors, one per text. One at a time by default: the int8 model scales each
	/// batch as a whole, so a sentence embedded beside others (and their padding) comes out a
	/// little different from the same sentence alone, which is how a typed one is embedded.
	/// </summary>
	public float[][] Embed(IReadOnlyList<string> texts, int batch = 1)
	{
		var result = new float[texts.Count][];
		for (var start = 0; start < texts.Count; start += batch)
		{
			var ids = texts.Skip(start).Take(batch).Select(t => _tok.Encode(_prefix + Lower(t), MaxTokens)).ToList();
			var len = ids.Max(i => i.Length);
			var input = new DenseTensor<long>([ids.Count, len]);
			var mask = new DenseTensor<long>([ids.Count, len]);
			for (var b = 0; b < ids.Count; b++)
				for (var j = 0; j < len; j++)
				{
					input[b, j] = j < ids[b].Length ? ids[b][j] : Unigram.Pad;
					mask[b, j] = j < ids[b].Length ? 1 : 0;
				}
			using var output = _session.Run([NamedOnnxValue.CreateFromTensor("input_ids", input), NamedOnnxValue.CreateFromTensor("attention_mask", mask)]);
			var hidden = ((DenseTensor<float>)output.First().AsTensor<float>()).Buffer.Span;
			var dim = hidden.Length / (ids.Count * len);
			for (var b = 0; b < ids.Count; b++)
			{
				// mean of the real tokens, then unit length
				var v = new float[dim];
				for (var j = 0; j < ids[b].Length; j++)
					for (int d = 0, at = (b * len + j) * dim; d < dim; d++)
						v[d] += hidden[at + d];
				var norm = MathF.Sqrt(v.Sum(x => x * x));
				for (var d = 0; d < dim; d++) v[d] /= norm;
				result[start + b] = v;
			}
		}
		return result;
	}

	/// <summary>
	/// Lower case before the model sees it: the tokenizer tells "KADRO" from "kadro", and odd
	/// capitals were the biggest cause of a real ask read as "nothing" (12.8% of such noisy
	/// sentences, held). "İ" goes to a plain "i" (tools/nl-embed.py does the same).
	/// </summary>
	public static string Lower(string text) => text.Replace('İ', 'i').ToLowerInvariant();

	public static float Dot(float[] a, float[] b) => System.Numerics.Tensors.TensorPrimitives.Dot(a, b);
}

/// <summary>
/// The SentencePiece unigram tokenizer of the XLM-R family, read from a Hugging Face
/// tokenizer.json: compatibility-normalised text split on spaces, "▁" before every word, and
/// each word cut into the pieces whose scores add up highest. Gives the same ids as the
/// Python tokenizer on every sentence of tests/nl.
/// </summary>
internal sealed class Unigram
{
	public const int Bos = 0, Pad = 1, Eos = 2, Unk = 3;
	private readonly Dictionary<string, (int Id, float Score)> _pieces;
	private readonly int _maxLen;
	private readonly float _unkScore;

	private Unigram(Dictionary<string, (int, float)> pieces, int maxLen, float unkScore)
	{
		_pieces = pieces;
		_maxLen = maxLen;
		_unkScore = unkScore;
	}

	public static Unigram Load(string path)
	{
		using var doc = JsonDocument.Parse(File.ReadAllBytes(path));
		var vocab = doc.RootElement.GetProperty("model").GetProperty("vocab");
		var pieces = new Dictionary<string, (int, float)>(vocab.GetArrayLength(), StringComparer.Ordinal);
		var id = 0;
		var min = float.MaxValue;
		var maxLen = 0;
		foreach (var entry in vocab.EnumerateArray())
		{
			var piece = entry[0].GetString()!;
			var score = (float)entry[1].GetDouble();
			// the special tokens are never cut out of text
			if (id > Unk) pieces.TryAdd(piece, (id, score));
			min = Math.Min(min, score);
			maxLen = Math.Max(maxLen, piece.Length);
			id++;
		}
		return new Unigram(pieces, maxLen, min - 10f);
	}

	public int[] Encode(string text, int maxTokens)
	{
		var ids = new List<int> { Bos };
		foreach (var word in Normalize(text).Split(' ', StringSplitOptions.RemoveEmptyEntries))
		{
			Cut("▁" + word, ids);
			if (ids.Count >= maxTokens - 1) break;
		}
		if (ids.Count > maxTokens - 1) ids.RemoveRange(maxTokens - 1, ids.Count - (maxTokens - 1));
		ids.Add(Eos);
		return [.. ids];
	}

	/// <summary>
	/// Compatibility form; tabs, line breaks and invisible characters become spaces. Under
	/// InvariantGlobalization Normalize leaves compatibility letters ("ﬁ", full-width "Ａ") as
	/// they are, where the Python tokenizer folds them; typed sentences hardly ever hold one.
	/// </summary>
	private static string Normalize(string text)
	{
		var sb = new StringBuilder(text.Length);
		foreach (var c in text.Normalize(NormalizationForm.FormKC))
		{
			var cat = char.GetUnicodeCategory(c);
			sb.Append(char.IsWhiteSpace(c) || cat is System.Globalization.UnicodeCategory.Control or System.Globalization.UnicodeCategory.Format ? ' ' : c);
		}
		return sb.ToString();
	}

	/// <summary>Best cut of one word (Viterbi); a letter no piece covers is unknown, unknowns in a row are one.</summary>
	private void Cut(string word, List<int> ids)
	{
		var n = word.Length;
		var best = new float[n + 1];
		var from = new int[n + 1];
		var id = new int[n + 1];
		Array.Fill(best, float.NegativeInfinity);
		best[0] = 0;
		for (var i = 0; i < n; i++)
		{
			if (float.IsNegativeInfinity(best[i]) || char.IsLowSurrogate(word[i])) continue;
			var one = char.IsHighSurrogate(word[i]) && i + 1 < n ? 2 : 1;
			var covered = false;
			for (var len = 1; len <= _maxLen && i + len <= n; len++)
			{
				if (i + len < n && char.IsLowSurrogate(word[i + len])) continue;
				if (!_pieces.TryGetValue(word.Substring(i, len), out var p)) continue;
				if (len == one) covered = true;
				if (best[i] + p.Score > best[i + len])
				{
					best[i + len] = best[i] + p.Score;
					from[i + len] = i;
					id[i + len] = p.Id;
				}
			}
			if (!covered && best[i] + _unkScore > best[i + one])
			{
				best[i + one] = best[i] + _unkScore;
				from[i + one] = i;
				id[i + one] = Unk;
			}
		}
		var path = new List<int>();
		for (var at = n; at > 0; at = from[at])
			path.Add(id[at]);
		path.Reverse();
		for (var k = 0; k < path.Count; k++)
			if (path[k] != Unk || k == 0 || path[k - 1] != Unk)
				ids.Add(path[k]);
	}
}
