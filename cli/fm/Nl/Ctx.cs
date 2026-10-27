using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FmCli.Nl;

/// <summary>
/// Words that mean one value: "sol bek" and "left back" are DL. A phrase is words in a row,
/// each matched like a cue. The longest phrase that appears wins, the earliest on a tie.
/// Every lexicon's words are known words, so they are never taken for a name.
/// </summary>
internal sealed class Lex
{
	public static readonly List<Lex> All = [];
	public readonly (string[] Words, string Value)[] Items;

	public Lex(params (string Phrase, string Value)[] items)
	{
		Items = [.. items.Select(i => (i.Phrase.Split(' '), i.Value)).OrderByDescending(i => i.Item1.Length)];
		All.Add(this);
	}
}

internal sealed record Cue(string[] Words, double Weight);

/// <summary>One sentence being read: what it holds (numbers, money, positions, names) and the call's arguments so far.</summary>
internal sealed partial class Ctx(Text text, string? tool = null)
{
	public readonly Text T = text;
	/// <summary>The tool whose arguments are being read, once it is picked.</summary>
	public readonly string? Tool = tool;
	public string[] W => T.Words;
	public readonly JsonObject Args = [];
	public readonly List<string> Asks = [];

	public void Set(string key, JsonNode? value)
	{
		if (value != null) Args[key] = value;
	}

	public void Ask(string key)
	{
		if (!Args.ContainsKey(key) && !Asks.Contains(key)) Asks.Add(key);
	}

	// ---- finding words

	/// <summary>Where the words of a phrase stand in a row, or -1.</summary>
	public int Find(string phrase, int from = 0) => Find(phrase.Split(' '), from);

	public int Find(string[] words, int from = 0)
	{
		for (var i = from; i + words.Length <= W.Length; i++)
		{
			var ok = true;
			for (var j = 0; j < words.Length && ok; j++) ok = Text.Matches(W[i + j], words[j]);
			if (ok) return i;
		}
		return -1;
	}

	public bool Has(params string[] phrases) => phrases.Any(p => Find(p) >= 0);

	/// <summary>A cue holds when its words appear in this order; "#name", "#pos", "#num", "#money", "#formation" stand for one of those.</summary>
	public bool HasCue(string[] words)
	{
		var at = 0;
		foreach (var w in words)
		{
			var found = w switch
			{
				"#name" => Names().Select(n => n.At).Where(i => i >= at).DefaultIfEmpty(-1).First(),
				"#pos" => Positions().Select(p => p.At).Where(i => i >= at).DefaultIfEmpty(-1).First(),
				"#num" => Nums().Select(n => n.At).Where(i => i >= at).DefaultIfEmpty(-1).First(),
				"#money" => Moneys().Select(m => m.At).Where(i => i >= at).DefaultIfEmpty(-1).First(),
				"#formation" => Formations().Select(f => f.At).Where(i => i >= at).DefaultIfEmpty(-1).First(),
				_ => FindWord(w, at),
			};
			if (found < 0) return false;
			at = found + 1;
		}
		return true;
	}

	private int FindWord(string cue, int from)
	{
		for (var i = from; i < W.Length; i++)
			if (Text.Matches(W[i], cue)) return i;
		return -1;
	}

	public (string Value, int At, int Len)? Lex(Lex lex, int from = 0)
	{
		(string, int, int)? best = null;
		foreach (var (words, value) in lex.Items)
		{
			var at = Find(words, from);
			if (at < 0) continue;
			if (best == null || words.Length > best.Value.Item3 || (words.Length == best.Value.Item3 && at < best.Value.Item2)) best = (value, at, words.Length);
		}
		return best;
	}

	public string? LexValue(Lex lex) => Lex(lex)?.Value;

	/// <summary>Every place a lexicon phrase appears, in order, overlaps left to the longer one.</summary>
	public List<(string Value, int At, int Len)> LexAll(Lex lex)
	{
		var hits = new List<(string Value, int At, int Len)>();
		foreach (var (words, value) in lex.Items)
			for (var at = Find(words); at >= 0; at = Find(words, at + 1))
				if (!hits.Any(h => at < h.At + h.Len && h.At < at + words.Length)) hits.Add((value, at, words.Length));
		return [.. hits.OrderBy(h => h.At)];
	}

	// ---- numbers and money

	[GeneratedRegex(@"^(\d+(?:[.,]\d+)?)([a-z%]*)$")]
	private static partial Regex NumberWord();

	private static readonly string[] Millions = ["milyon*", "million*", "m$", "mn$", "mil$"];
	private static readonly string[] Thousands = ["bin$", "binden", "bine", "k$", "thousand*", "bin*"];

	private List<(string Value, int At)>? moneys;

	/// <summary>Amounts of money in the sentence: "30 milyon", "2.5m", "750k", "750 bin", "15000".</summary>
	public List<(string Value, int At)> Moneys()
	{
		if (moneys != null) return moneys;
		moneys = [];
		for (var i = 0; i < W.Length; i++)
		{
			var m = NumberWord().Match(W[i]);
			if (!m.Success) continue;
			var num = m.Groups[1].Value.Replace(',', '.');
			var tail = m.Groups[2].Value;
			var next = i + 1 < W.Length ? W[i + 1] : "";
			string? unit = null;
			if (tail.StartsWith("milyon") || tail.StartsWith("million") || tail is "m" or "mn" || (tail.StartsWith('m') && tail.Length <= 4 && !tail.StartsWith("ma"))) unit = "M";
			else if (tail is "k" || tail.StartsWith("bin") || (tail.StartsWith('k') && tail.Length <= 3)) unit = "K";
			else if (tail.Length == 0 && Millions.Any(c => Text.Matches(next, c))) unit = "M";
			else if (tail.Length == 0 && Thousands.Any(c => Text.Matches(next, c))) unit = "K";
			if (unit != null) moneys.Add((Trim(num) + unit, i));
			else if (tail.Length == 0 && num.Replace(".", "").Length >= 4 && double.TryParse(num.Replace(".", ""), NumberStyles.Any, CultureInfo.InvariantCulture, out _)) moneys.Add((num.Replace(".", ""), i));
		}
		return moneys;
	}

	private static string Trim(string num) => num.Contains('.') ? num.TrimEnd('0').TrimEnd('.') : num;

	public string? Money(int nth = 0) => Moneys().Count > nth ? Moneys()[nth].Value : null;

	private static readonly Dictionary<string, int> NumberWords = new()
	{
		["iki"] = 2, ["uc"] = 3, ["dort"] = 4, ["bes"] = 5, ["alti"] = 6, ["yedi"] = 7, ["sekiz"] = 8, ["dokuz"] = 9,
		["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5, ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10,
		["twice"] = 2,
	};

	private List<(int Value, int At)>? nums;

	/// <summary>Whole numbers that are not money: "5", "2yi", "15inde", "twice". Ordinals count too ("ikinci" is 2).</summary>
	public List<(int Value, int At)> Nums()
	{
		if (nums != null) return nums;
		nums = [];
		var money = Moneys().Select(m => m.At).ToHashSet();
		for (var i = 0; i < W.Length; i++)
		{
			if (money.Contains(i)) continue;
			var m = Regex.Match(W[i], @"^(\d+)([a-z]*)$");
			if (m.Success && m.Groups[1].Value.Length <= 4)
			{
				var tail = m.Groups[2].Value;
				if (tail.Length == 0 || TurkishTail(tail) || tail is "st" or "nd" or "rd" or "th" or "x") nums.Add((int.Parse(m.Groups[1].Value), i));
			}
			else if (NumberWords.TryGetValue(W[i], out var n)) nums.Add((n, i));
			else if (W[i] == "on" && i + 1 < W.Length && Text.Matches(W[i + 1], "kere")) nums.Add((10, i));
			else if (OrdinalOf(W[i]) is int o) nums.Add((o, i));
		}
		return nums;
	}

	private static bool TurkishTail(string tail) => tail.Length <= 5 && Regex.IsMatch(tail, "^(y|n|s|t|d)?[aeiou]([a-z]{0,3})$");

	public int? Num() => Nums().Count > 0 ? Nums()[0].Value : null;

	public static int? OrdinalOf(string w) => w switch
	{
		"ilk" or "birinci" or "first" or "1st" => 1,
		"ikinci" or "second" or "2nd" => 2,
		"ucuncu" or "third" or "3rd" => 3,
		"dorduncu" or "fourth" or "4th" => 4,
		"besinci" or "fifth" or "5th" => 5,
		_ => null,
	};

	[GeneratedRegex(@"^\d-\d(-\d)*$|^[1-5]{3,5}$")]
	private static partial Regex FormationWord();

	public List<(string Value, int At)> Formations()
	{
		var list = new List<(string, int)>();
		for (var i = 0; i < W.Length; i++)
		{
			var w = Regex.Match(W[i], @"^[\d-]+").Value;
			if (w.Length >= 3 && FormationWord().IsMatch(w) && w.Replace("-", "").Sum(c => c - '0') == 10)
				list.Add((w.Contains('-') ? w : string.Join('-', w.ToCharArray()), i));
		}
		return list;
	}

	/// <summary>"18-23", or "21 yaş altı" as 16-21.</summary>
	public string? AgeRange()
	{
		foreach (var w in W)
		{
			var m = Regex.Match(w, @"^(1\d|2\d|3\d)-(1\d|2\d|3\d|4\d)$");
			if (m.Success) return m.Value;
		}
		var under = Find("yas alti");
		if (under < 0) under = Find("under");
		if (under >= 0)
			foreach (var (v, at) in Nums())
				if (Math.Abs(at - under) <= 2 && v is >= 15 and <= 40) return $"16-{v}";
		return null;
	}

	// ---- positions

	public static readonly Lex Position = new(
		("sag stoper", "DCR"), ("sol stoper", "DCL"), ("stoper", "DC"), ("centre back", "DC"), ("center back", "DC"), ("centre backs", "DC"), ("cb$", "DC"), ("dc$", "DC"),
		("sag bek*", "DR"), ("right back", "DR"), ("rb$", "DR"), ("sol bek*", "DL"), ("left back", "DL"), ("lb$", "DL"),
		("sag kanat", "AMR"), ("right winger", "AMR"), ("right wing", "AMR"), ("amr$", "AMR"), ("saga", "AMR"),
		("sol kanat", "AML"), ("left winger", "AML"), ("left wing", "AML"), ("aml$", "AML"), ("sola", "AML"),
		("forvet", "ST"), ("santrfor", "ST"), ("striker", "ST"), ("strikers", "ST"), ("st$", "ST"), ("up front", "ST"), ("golcu", "ST"),
		("kaleci", "GK"), ("kale$", "GK"), ("kalede", "GK"), ("kaleye", "GK"), ("in goal", "GK"), ("keeper", "GK"), ("goalkeeper", "GK"), ("gk$", "GK"),
		("dmc", "DM"), ("dm$", "DM"), ("on libero", "DM"), ("defensive midfielder", "DM"),
		("10 numara", "AMC"), ("amc$", "AMC"), ("ofansif orta saha", "AMC"),
		("orta saha", "MC"), ("midfielder", "MC"), ("mc$", "MC"), ("cm$", "MC"));

	public List<(string Value, int At, int Len)> Positions() => LexAll(Position);

	/// <summary>Lengths of time: never part of a name ("2 hafta", "for a month").</summary>
	public static readonly Lex Durations = new(
		("hafta", "week"), ("haftalik", "week"), ("week*", "week"), ("ay$", "month"), ("aylik", "month"), ("ayligina", "month"), ("month*", "month"),
		("yil$", "year"), ("yili", "year"), ("yillik", "year"), ("yilligina", "year"), ("year*", "year"), ("gun$", "day"), ("gunluk", "day"), ("day$", "day"), ("days", "day"));

	public string? Pos() => Positions().Select(p => p.Value).FirstOrDefault();

	// ---- names

	private static readonly HashSet<string> Stop =
	[
		"ve", "ile", "mi", "mu", "da", "de", "ki", "bu", "su", "o", "bir", "bi", "ne", "icin", "gibi", "ama", "sonra", "olarak", "ya", "ben", "bana", "beni", "bizim", "biz",
		"abi", "kanka", "hadi", "lan", "hocam", "bak", "bakalim", "hemen", "pls", "please", "plz", "hey", "yo", "ok", "bro", "mate", "rn", "thx", "lol", "quick", "can", "you",
		"the", "a", "an", "to", "for", "of", "on", "in", "at", "and", "or", "with", "me", "my", "i", "is", "are", "be", "do", "does", "our", "we", "us", "it", "this", "that",
		"i", "in", "nin", "e", "a", "ye", "ya", "yi", "u", "un", "nun", "dan", "den", "te", "ta", "le", "la", "ile", "mi", "kim", "who", "his", "her", "him", "he", "them", "their",
		"var", "yok", "olsun", "her", "hep", "cok", "az", "en", "daha", "tum", "butun", "hepsi", "hepsini", "biraz", "simdi", "now", "just", "also", "too", "ise", "iyi",
		"over", "under", "more", "less", "than", "from", "by", "as", "about", "what", "how", "whats", "hows", "whos", "which", "when", "where", "why", "some", "any", "all", "out",
		// question words, pronouns and fillers: never a name, even typed with one letter off ("nedden")
		"neden", "niye", "nicin", "nasil", "hangi", "hangisi", "hangileri", "kimle", "kimin", "kime", "kimi", "kimler", "kimse", "herkes", "hicbir", "nerede", "nereye",
		"nereden", "kac", "kacta", "kacinci", "sen", "siz", "onlar", "onu", "ona", "onun", "bunu", "buna", "bunun", "sunu", "suna", "bunlar", "sunlar", "artik", "yine",
		"gene", "belki", "sadece", "bile", "cunku", "eger", "hani", "acaba", "mesela", "falan", "tamam", "evet", "hayir", "lutfen", "diye", "kadar", "gore",
		"anyone", "anybody", "someone", "somebody", "everyone", "everybody", "nobody", "something", "anything", "everything", "nothing", "whom", "whose", "there",
		"here", "these", "those", "your", "yours", "you", "its", "they", "she", "been", "was", "were", "would", "could", "should", "have", "has", "had", "not", "yes",
		"very", "really", "still", "only", "even", "again", "maybe", "pls", "pleease",
		// someone not named: "şu çocuğa teklif yap", "sell this guy" ask who
		"biri", "birini", "birine", "birinin", "sey", "seyi", "cocuk", "cocugu", "cocuga", "cocugun", "adam", "adami", "adama", "adamin", "oyuncu", "oyuncuyu", "oyuncuya",
		"oyuncunun", "oyuncular", "someone", "somebody", "one", "ones", "guy", "lad", "kid", "player", "players", "want", "wanna", "need",
		// what a command tells the game to do: "sell this guy" names no "sell"
		"sell", "buy", "sign", "hire", "fire", "sack", "show", "give", "make", "put", "take", "send", "watch", "track", "add", "remove", "drop", "play", "start",
		"bring", "train", "teach", "offer", "bid", "ask", "tell", "answer", "reply", "open", "read", "find", "search", "compare", "extend", "renew", "cancel",
		"release", "loan", "scout", "swap", "replace", "keep", "let", "get", "set", "change", "move", "check", "list", "see", "look",
	];

	public static bool IsStop(string w) => Stop.Contains(w);

	private static readonly string[] LongStop = [.. Stop.Where(w => w.Length >= 5)];

	private static bool StopTypo(string w) => w.Length >= 4 && LongStop.Any(b => w[0] == b[0] && Text.Distance(w, b) <= 1);

	private static readonly Lazy<string[]> Known = new(() =>
	[
		.. Router.Rules.SelectMany(r => r.Cues).SelectMany(c => c.Words).Where(w => !w.StartsWith('#')).Distinct(),
		.. FmCli.Nl.Lex.All.SelectMany(l => l.Items).SelectMany(i => i.Words).Distinct(),
		// the values the lexicons give are words too ("Daha Sık", "More Often")
		.. FmCli.Nl.Lex.All.SelectMany(l => l.Items).SelectMany(i => Regex.Split(Text.Fold(i.Value), "[^a-z]+")).Where(w => w.Length >= 3).Select(w => w + "$").Distinct(),
	]);

	/// <summary>
	/// A known word, or one with an ending on it. The ending has to look like one ("kadroyu", "takes",
	/// "listesine"), so "moretti" is not "more" and "fontana" not "font".
	/// </summary>
	private static bool IsKnown(string w) => Known.Value.Any(k => Text.Matches(w, k, typo: false)
		&& (k.EndsWith('*') || w.Length == k.TrimEnd('$').Length || Ending(w[k.TrimEnd('$').Length..])));

	private static bool Ending(string tail) => tail.Length <= 6 && (TurkishEnding().IsMatch(tail) || tail is "s" or "es" or "ed" or "d" or "ing" or "er" or "ers" or "y" or "ly");

	/// <summary>A Turkish verb by its ending: "satmak", "istiyorum", "ilgilendigimizi", "ogrenebilir".</summary>
	[GeneratedRegex("^[a-z]{3,}(mak|mek|iyor|iyorum|iyoruz|uyor|uyorum|ecek|acak|ecegiz|acagiz|digimizi|digimiz|dugumuzu|digini|abilir|ebilir|abilirim|ebilirim|mali|meli)$")]
	private static partial Regex TurkishVerb();

	[GeneratedRegex("^(y|n|s|l|d|t|m|k)?[aeiou]([a-z]{0,4})$|^(lar|ler|dan|den|tan|ten|da|de|ta|te|ca|ce|ki|nin|nun|sin|lik)[a-z]{0,3}$")]
	private static partial Regex TurkishEnding();

	/// <summary>A known word typed one letter off; the first letter has to hold ("rinaldi" is no "final").</summary>
	private static bool NearKnown(string w) => Known.Value.Any(k => k.TrimEnd('$', '*').Length >= 5 && w[0] == k[0] && Text.Matches(w, k));

	private static readonly Lazy<Dictionary<string, string[]>> ToolWords = new(() => Router.Rules.GroupBy(r => r.Tool).ToDictionary(g => g.Key,
		g => g.SelectMany(r => r.Cues).SelectMany(c => c.Words).Select(w => w.TrimEnd('$', '*')).Where(w => w.Length >= 4 && !w.StartsWith('#')).Distinct().ToArray()));

	/// <summary>
	/// One of the picked tool's own words typed wrong: "sgin" for a contract's "sign", "maaasi" for its
	/// "maas". The first letter has to hold. Other tools' words are left alone, so "marin" stays a name
	/// though "macin" is a match word.
	/// </summary>
	private bool ToolTypo(string w) => Tool != null && w.Length >= 4 && ToolWords.Value.TryGetValue(Tool, out var words)
		&& words.Any(b => w[0] == b[0] && (b.Length >= 5 ? w.Length <= b.Length + 3 && Text.Matches(w, b) : w.Length <= 5 && Text.Distance(w, b) <= 1));

	private List<(string Name, int At, int Len)>? names;

	/// <summary>
	/// Words that are no known word, in runs: "kenan yildiz" is one name, "rinaldi ve tosto" two.
	/// A Turkish case ending on the last word goes ("pieragnoloya" → "pieragnolo").
	/// </summary>
	public List<(string Name, int At, int Len)> Names()
	{
		if (names != null) return names;
		names = [];
		var taken = new bool[W.Length];
		foreach (var lex in FmCli.Nl.Lex.All)
			foreach (var (_, at, len) in LexAll(lex))
				for (var k = at; k < at + len; k++) taken[k] = true;
		// "iki oyuncu", "compare two players": a number said in words is no name
		foreach (var (_, at) in Nums())
			taken[at] = true;
		// "20 bin", "3 milyon": the unit after an amount is part of it
		foreach (var (_, at) in Moneys())
			if (at + 1 < W.Length && Millions.Concat(Thousands).Any(u => Text.Matches(W[at + 1], u))) taken[at + 1] = true;
		bool Nameish(int i) => !taken[i] && Regex.IsMatch(W[i], "^[a-z][a-z0-9]{2,}$") && !Stop.Contains(W[i]) && !StopTypo(W[i]) && !IsKnown(W[i]) && !ToolTypo(W[i])
			&& !TurkishVerb().IsMatch(W[i]);
		for (var i = 0; i < W.Length; i++)
		{
			if (!Nameish(i)) continue;
			var j = i;
			while (j + 1 < W.Length && j - i < 2 && Nameish(j + 1)) j++;
			var (a, b) = (i, j);
			bool Uncommon(int x, int y) => W[x..(y + 1)].Any(w => !Common.Value.ContainsKey(w));
			while (b > a && CommonIn(W[b], "en") && Uncommon(a, b - 1)) b--;
			while (a < b && CommonIn(W[a], "en") && Uncommon(a + 1, b)) a++;
			var (i2, j2) = (a, b);
			while (b > a && NearKnown(W[b])) b--;
			while (a < b && NearKnown(W[a])) a++;
			if (!(a == b && NearKnown(W[a]) && i2 != j2))
				names.Add((string.Join(' ', W[a..(b + 1)].Select((w, k) => a + k == b ? Strip(w) : w)), a, b - a + 1));
			i = j;
		}
		// a common word is a name only when nothing less common is: "sell rinaldi" names rinaldi
		if (names.Any(n => !n.Name.Split(' ').All(IsCommon)))
			names = [.. names.Where(n => !n.Name.Split(' ').All(IsCommon))];
		return names;
	}

	/// <summary>The most used words, with the languages they are common in (tools/nl-words.py).</summary>
	private static readonly Lazy<Dictionary<string, string>> Common = new(() =>
	{
		using var stream = typeof(Ctx).Assembly.GetManifestResourceStream("common-words.txt");
		using var reader = new StreamReader(stream!);
		return reader.ReadToEnd().Split('\n').Where(l => l.Length > 0 && l[0] != '#').Select(l => l.Split('\t'))
			.ToDictionary(p => p[0], p => p.Length > 1 ? p[1] : "");
	});

	private static bool IsCommon(string w) => Common.Value.ContainsKey(w);


	private static bool CommonIn(string w, string lang) => Common.Value.TryGetValue(w, out var langs) && langs.Contains(lang);

	public string? Name(int nth = 0) => Names().Count > nth ? Names()[nth].Name : null;

	/// <summary>A name with a word in it that is not a common one: for a tool that runs as well without a name.</summary>
	public string? StrongName() => Names().Select(n => n.Name).FirstOrDefault(n => !n.Split(' ').All(IsCommon));

	/// <summary>Every name word on its own, for lists ("rinaldi tosto ve marin").</summary>
	public List<string> NameWords() => [.. Names().SelectMany(n => n.Name.Split(' ').Select(Strip))];

	public static string Strip(string w)
	{
		string[] tails = ["ndan", "nden", "nin", "nun", "yla", "yle", "dan", "den", "yi", "yu", "ya", "ye", "a", "e"];
		foreach (var t in tails)
		{
			if (!w.EndsWith(t) || w.Length - t.Length < 4) continue;
			var before = w[w.Length - t.Length - 1];
			var vowel = "aeiou".Contains(before);
			if ((t[0] is 'y' or 'n') ? vowel : (t.Length > 1 || !vowel)) return w[..^t.Length];
		}
		return w;
	}

	/// <summary>Words between two places, stop words left out.</summary>
	public string Between(int from, int to) => string.Join(' ', W[Math.Max(0, from)..Math.Min(W.Length, Math.Max(from, to))].Where(w => !Stop.Contains(w)));

	public static string Title(string s) => string.Join(' ', s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
}
