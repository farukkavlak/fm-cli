namespace FmCli.Nl;

/// <summary>A person or club of the save, held by the game's own id (UniqueId).</summary>
internal sealed record SaveName(string Kind, string Id, string Name)
{
	public const string Player = "player";
	public const string Club = "club";
}

/// <summary>
/// Which of the save's people or clubs a name typed in a sentence means: "rinaldiye" is Rinaldi,
/// "dadukun" Dadük, "kalafta" Kalafat (a typo), "frosinonenin rinaldi" Rinaldi with the club's
/// word left out. Only letters are compared, folded as the router folds them; nothing about the
/// language the game is in.
/// </summary>
internal static class NameFit
{
	/// <summary>How well a typed word fits a word of a name, best first; 0 is no fit.</summary>
	private const int Exact = 4, Ended = 3, Start = 2, Typo = 1;

	/// <summary>
	/// Turkish case and possessive endings, and the English plural, as the router folds them
	/// ("ı" to "i", "ü" to "u"): what can follow a name typed without an apostrophe.
	/// </summary>
	private static readonly HashSet<string> Endings =
	[
		"i", "u", "a", "e", "s", "yi", "yu", "ya", "ye", "ni", "nu", "na", "ne", "in", "un", "nin", "nun",
		"da", "de", "ta", "te", "nda", "nde", "dan", "den", "tan", "ten", "ndan", "nden",
		"la", "le", "yla", "yle", "ca", "ce", "lar", "ler", "es", "si", "su", "sin", "sun",
	];

	public static string[] Words(string name) =>
		Text.Fold(name).Split([' ', '-', '\'', '.', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

	public static int Fit(string typed, string word)
	{
		if (typed == word) return Exact;
		if (typed.StartsWith(word, StringComparison.Ordinal) && Endings.Contains(typed[word.Length..])) return Ended;
		if (typed.Length >= 3 && word.StartsWith(typed, StringComparison.Ordinal)) return Start;
		if (word.Length < 5 || typed.Length < 4) return 0;
		// One letter off from five letters on, two from nine; the first letter holds ("rinaldi" is no "final").
		// The typed word can carry an ending after the typo ("kalaftı").
		var cap = word.Length >= 9 ? 2 : 1;
		for (var n = Math.Max(4, word.Length - cap); n <= Math.Min(typed.Length, word.Length + cap); n++)
		{
			var head = typed[..n];
			if (head[0] != word[0] || !(n == typed.Length || Endings.Contains(typed[n..]))) continue;
			if (Text.Distance(head, word, cap) <= cap) return Typo;
		}
		return 0;
	}

	/// <summary>
	/// How well typed words fit a name: each typed word to a word of its own in the name, in any
	/// order; 0 when one fits none. A name whose words were all typed counts a little more, so
	/// "emir" is Emir before Emirhan.
	/// </summary>
	public static double Score(IReadOnlyList<string> typed, string name)
	{
		var words = Words(name);
		var used = new bool[words.Length];
		var total = 0.0;
		foreach (var t in typed)
		{
			var (best, at) = (0, -1);
			for (var i = 0; i < words.Length; i++)
				if (!used[i] && Fit(t, words[i]) is var f && f > best) (best, at) = (f, i);
			if (best == 0) return 0;
			used[at] = true;
			total += best;
		}
		return total + (used.All(u => u) ? 0.5 : 0);
	}

	/// <summary>The best fits of the typed words among the known of one kind: one, several as good, or none.</summary>
	public static List<SaveName> Who(IReadOnlyList<string> typed, IEnumerable<SaveName> known, string kind)
	{
		if (typed.Count == 0) return [];
		var scored = known.Where(k => k.Kind == kind).Select(k => (SaveName: k, Score: Score(typed, k.Name))).Where(s => s.Score > 0).ToList();
		if (scored.Count == 0) return [];
		var top = scored.Max(s => s.Score);
		return [.. scored.Where(s => s.Score == top).Select(s => s.SaveName).DistinctBy(k => k.Id)];
	}

	/// <summary>
	/// A club named ahead of a player ("frosinonenin rinaldi", "pendikspor emir"): the club and the
	/// words left for the player. The club's words have to come first and fit it well (a typo is
	/// not enough to take a word from the player); null when they do not, or nothing is left.
	/// </summary>
	public static (SaveName Club, string[] Left)? ClubFirst(IReadOnlyList<string> typed, IEnumerable<SaveName> known)
	{
		var clubs = known.Where(k => k.Kind == SaveName.Club).ToList();
		for (var n = Math.Min(3, typed.Count - 1); n >= 1; n--)
		{
			var head = typed.Take(n).ToList();
			var fits = clubs.Where(c => Words(c.Name) is var w && head.All(t => w.Any(x => Fit(t, x) >= Start))).ToList();
			if (fits.Count == 1) return (fits[0], [.. typed.Skip(n)]);
		}
		return null;
	}

	/// <summary>
	/// The longest run of the typed words that fits someone, when all of them together fit nobody:
	/// the words of that run and who it fits. Null when no run does.
	/// </summary>
	public static (List<string> Words, List<SaveName> Fits)? Within(IReadOnlyList<string> typed, IEnumerable<SaveName> known, string kind)
	{
		var all = known.ToList();
		for (var n = typed.Count - 1; n >= 1; n--)
		{
			var found = Enumerable.Range(0, typed.Count - n + 1).Select(at => typed.Skip(at).Take(n).ToList())
				.Select(run => (Run: run, Fits: Who(run, all, kind), Score: Who(run, all, kind) is [var first, ..] ? Score(run, first.Name) : 0))
				.Where(r => r.Fits.Count > 0).OrderByDescending(r => r.Score).ToList();
			if (found.Count > 0) return (found[0].Run, found[0].Fits);
		}
		return null;
	}

	/// <summary>Names the model was taught with, put in for the save's people in turn.</summary>
	private static readonly string[] Taught = ["rinaldi", "tosto", "moretti"];

	/// <summary>
	/// The sentence with each of the save's players in it put as a name the model was taught with,
	/// a club named just before him left out: "pendikspor susak hakkinda bilgi" is seen as "rinaldi
	/// hakkinda bilgi". A word only starting a name is not taken for it, nor a word under four letters, nor
	/// a common word on its own ("usta", "emir"). Null when there is nobody to put in.
	/// </summary>
	public static string? StandIn(string sentence, IEnumerable<SaveName> known)
	{
		var all = known.ToList();
		var words = new Text(sentence).Words;
		var players = all.Where(k => k.Kind == SaveName.Player).Select(k => (k, Words(k.Name))).ToList();
		bool Fits(string w) => w.Length >= 4 && players.Any(p => p.Item2.Any(x => Fit(w, x) is Exact or Ended or Typo));
		var output = new List<string>();
		var put = 0;
		for (var i = 0; i < words.Length; i++)
		{
			if (!Fits(words[i])) { output.Add(words[i]); continue; }
			var j = i;
			while (j + 1 < words.Length && j - i < 2 && Fits(words[j + 1]) && Who(words[i..(j + 2)], all, SaveName.Player).Count > 0) j++;
			// "usta bir forvet": a common word on its own is the word, not Muhammet Usta
			if (Who(words[i..(j + 1)], all, SaveName.Player).Count == 0 || (j == i && Ctx.IsCommon(words[i]))) { output.Add(words[i]); continue; }
			// "pendikspor susak": the club's words just before him go with him
			while (output.Count > 0 && all.Any(c => c.Kind == SaveName.Club && Words(c.Name).Any(x => Fit(output[^1], x) >= Start))) output.RemoveAt(output.Count - 1);
			output.Add(Taught[put++ % Taught.Length]);
			i = j;
		}
		return put == 0 ? null : string.Join(' ', output);
	}
}
