using System.Text;
using System.Text.RegularExpressions;

namespace FmCli.Nl;

/// <summary>
/// A typed sentence as the router reads it: lower case, Turkish letters folded, split into words.
/// "rinaldi'ye" keeps "rinaldi" (a Turkish suffix after an apostrophe goes), "who's" stays one
/// word ("whos"), "4-4-2", "18-23", "2.5m" and "20%" stay whole.
/// </summary>
internal sealed partial class Text
{
	public readonly string Raw;
	public readonly string[] Words;

	public Text(string raw)
	{
		Raw = raw;
		Words = Split(raw);
	}

	public static string Fold(string s)
	{
		// Spelled out: under InvariantGlobalization, Normalize leaves these letters as they are.
		var sb = new StringBuilder(s.Length);
		foreach (var c in s)
			sb.Append(Lower(c));
		return sb.ToString().Replace("\u0307", "");
	}

	private static char Lower(char c) => char.ToLowerInvariant(c) switch
	{
		'ş' or 'Ş' => 's', 'İ' or 'ı' or 'î' or 'í' or 'ï' => 'i', 'ğ' or 'Ğ' => 'g', 'ü' or 'Ü' or 'û' or 'ú' => 'u', 'ö' or 'Ö' or 'ó' => 'o', 'ç' or 'Ç' => 'c',
		'â' or 'à' or 'á' or 'ä' or 'ã' or 'å' => 'a', 'é' or 'è' or 'ë' or 'ê' => 'e', 'ñ' or 'ń' => 'n', 'š' or 'ś' => 's', 'ž' or 'ź' or 'ż' => 'z',
		'č' or 'ć' => 'c', 'đ' => 'd', 'ł' => 'l', 'ø' or 'õ' or 'ô' => 'o', 'ý' => 'y', 'ř' => 'r',
		'’' or '`' or '´' => '\'', var l => l,
	};

	private static readonly HashSet<string> EnglishS = ["who", "what", "where", "how", "it", "that", "there", "let", "he", "she", "here", "when", "whats"];

	private static string[] Split(string raw)
	{
		var s = Fold(raw);
		s = Apostrophe().Replace(s, m =>
		{
			var (head, tail) = (m.Groups[1].Value, m.Groups[2].Value);
			if (tail is "t" or "re" or "ll" or "ve" or "d" or "m") return head + tail;
			if (tail == "s") return EnglishS.Contains(head) ? head + "s" : head;
			return head;
		});
		return [.. Word().Matches(s).Select(m => m.Value.TrimEnd('.'))];
	}

	[GeneratedRegex(@"([a-z0-9]+)'([a-z]*)")]
	private static partial Regex Apostrophe();

	[GeneratedRegex(@"\d+(?:-\d+)+|\d+(?:[.,]\d+)*[a-z]*%?|[a-z][a-z0-9]*")]
	private static partial Regex Word();

	/// <summary>Damerau distance, capped: anything past <paramref name="cap"/> is cap + 1.</summary>
	public static int Distance(string a, string b, int cap = 1)
	{
		if (Math.Abs(a.Length - b.Length) > cap) return cap + 1;
		var w = b.Length + 1;
		Span<int> d = stackalloc int[(a.Length + 1) * w];
		for (var i = 0; i <= a.Length; i++) d[i * w] = i;
		for (var j = 0; j <= b.Length; j++) d[j] = j;
		for (var i = 1; i <= a.Length; i++)
		{
			var rowMin = int.MaxValue;
			for (var j = 1; j <= b.Length; j++)
			{
				var cost = a[i - 1] == b[j - 1] ? 0 : 1;
				var v = Math.Min(Math.Min(d[(i - 1) * w + j] + 1, d[i * w + j - 1] + 1), d[(i - 1) * w + j - 1] + cost);
				if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) v = Math.Min(v, d[(i - 2) * w + j - 2] + 1);
				d[i * w + j] = v;
				rowMin = Math.Min(rowMin, v);
			}
			if (rowMin > cap) return cap + 1;
		}
		return d[a.Length * w + b.Length];
	}

	/// <summary>
	/// Whether a typed word is this cue word. A cue of four letters or more matches as the start
	/// of the word ("kadro" in "kadroyu"), shorter ones and those ending in "$" only whole; from
	/// five letters on one typo is forgiven. "*" at the end makes a short cue a start too.
	/// </summary>
	public static bool Matches(string word, string cue, bool typo = true)
	{
		var exact = cue.EndsWith('$');
		var start = cue.EndsWith('*');
		if (exact || start) cue = cue[..^1];
		if (word == cue) return true;
		var prefix = start || (!exact && cue.Length >= 4);
		if (prefix && word.StartsWith(cue, StringComparison.Ordinal)) return true;
		if (!typo || cue.Length < 5 || word.Length < 4) return false;
		// one typo leaves the first letter in place, or swaps it with the second ("talimat" is no "calim")
		if (word[0] != cue[0] && !(word[0] == cue[1] && word[1] == cue[0])) return false;
		if (!prefix) return Distance(word, cue) <= 1;
		for (var n = cue.Length - 1; n <= cue.Length + 1; n++)
			if (n <= word.Length && Distance(word[..n], cue) <= 1) return true;
		return false;
	}
}
