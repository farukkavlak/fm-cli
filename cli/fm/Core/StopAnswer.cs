using FmCli.Nl;

namespace FmCli;

/// <summary>
/// A sentence typed at a stop, held to that stop's answers first (T215): "ikincisi", "kabul et", "maçı oyna",
/// "fenerin teklifine bak". Three things are read from it, none from the CLI's own label text:
/// - a place in the list as shown ("the second", "ikincisini", "2"), the numbers the stop was shown with;
/// - what is to be done (accept, reject, stall, see, play, straight to the result, go on, skip, end, open,
///   answer), held against what each answer's command does, not against its label;
/// - the game's own words an answer is about (the clubs and players of an offer, a message's subject),
///   for answers that are about something; the user's words are held against them as the game shows them.
/// A sentence that is a question, or says nothing of the stop, is left to the router: the stop waits.
/// </summary>
internal static partial class Program
{
	/// <summary>What an answer does, by its command: the same in every language and save.</summary>
	internal static string? DoneBy(string[] command) => command switch
	{
		["offers", _, "accept", ..] => "accept",
		["offers", _, "reject", ..] => "reject",
		["offers", _, "stall", ..] => "stall",
		["offers", _, "view", ..] or ["offers"] or ["vision"] => "view",
		["match", "--play", ..] => "play",
		["match", "--instant", ..] => "instant",
		["match"] => "goon",
		["next", "--skip", ..] => "skip",
		["next", ..] => "open",
		["vision", "end", ..] => "end",
		["talk", ..] or ["setpieces", "new", ..] => "answer",
		["tour", ..] => "goon",
		// one offer of the list, one message: about something, said by its own words
		_ => null,
	};

	/// <summary>Places in a list as said: the n-th, by the count from one; "last" is -1.</summary>
	private static readonly (int Place, string[] Cues)[] SaidPlace =
	[
		(1, ["ilk*", "birinci*", "first", "1st"]), (2, ["ikinci*", "second", "2nd"]), (3, ["ucuncu*", "third", "3rd"]),
		(4, ["dorduncu*", "fourth", "4th"]), (5, ["besinci*", "fifth", "5th"]), (6, ["altinci*", "sixth", "6th"]),
		(-1, ["son$", "sonuncu*", "sondaki", "last"]),
	];

	/// <summary>Little words that say nothing about which answer: left out when an answer's own words are looked for.</summary>
	private static readonly HashSet<string> Little =
		["bu", "su", "o", "bir", "ve", "ile", "icin", "et", "etsene", "edelim", "yap", "olsun", "lutfen", "teklif", "teklifi", "teklifini", "teklifine", "mesaj", "mesaji",
		 "the", "a", "an", "it", "that", "this", "one", "of", "for", "to", "please", "offer", "message", "from", "and"];

	/// <param name="Sure">false when the model read what to do but not clearly: the answer is shown and asked about first.</param>
	internal sealed record SaidAnswers(List<int> Places, IReadOnlySet<string> Done, bool Sure = true);

	/// <summary>
	/// Which of the stop's answers a sentence says, by their places in its list (0 based): one, several as
	/// good, or none; null when the sentence is a question or says nothing of this stop. Done is what it said is
	/// to be done, kept for the stop an answer about something leads to (an offer chosen, then its answers).
	/// </summary>
	internal static SaidAnswers? AnswersSaid(HeldStop stop, string sentence)
	{
		var count = stop.Answers.Count;
		if (count == 0) return null;
		var words = new Text(sentence).Words;
		if (words.Length == 0) return null;
		bool Says(string cue) => words.Any(w => Text.Matches(w, cue, typo: false));

		if (sentence.TrimEnd().EndsWith('?')) return null;

		// A place: a number of the list said in the sentence, or a word for one.
		// A number past the list's end makes a place word part of something else ("ilk 11": the first eleven).
		var past = words.Any(w => int.TryParse(w, out var n) && n > count);
		var places = words.Where(w => int.TryParse(w, out var n) && n >= 1 && n <= count).Select(int.Parse)
			.Concat(past ? [] : SaidPlace.Where(p => p.Cues.Any(Says)).Select(p => p.Place == -1 ? count : p.Place))
			.Where(p => p >= 1 && p <= count).Distinct().ToList();
		if (places.Count > 1) return null;

		// The answer's own words, for the answers about something.
		var own = words.Where(w => w.Length >= 3 && !Little.Contains(w) && !w.All(char.IsDigit)).ToList();
		int Fit(string w, string label) => NameFit.Words(label).Select(l => Fits(w, l)).DefaultIfEmpty(0).Max();
		var fit = stop.Answers.Select(a => DoneBy(a.Command) != null || own.Count == 0 || places.Count == 1 ? 0 : own.Sum(w => Fit(w, a.Label))).ToList();
		var all = Enumerable.Range(0, count).ToList();
		var best = fit.Max();
		var candidates = best > 0 ? all.Where(i => fit[i] == best).ToList() : all;

		// What is to be done, read without the words for the place or for what the answer is about:
		// "the first one" and "Başakşehir Baray" say nothing of stalling.
		bool Place(string w) => int.TryParse(w, out _) || SaidPlace.Any(p => p.Cues.Any(c => Text.Matches(w, c, typo: false)));
		bool Named(string w) => best > 0 && candidates.Any(i => Fit(w, stop.Answers[i].Label) > 0);
		var rest = words.Where(w => !(places.Count == 1 && Place(w)) && !Named(w) && !(best > 0 && Little.Contains(w))).ToList();
		var done = KindAsked(stop, rest.Count > 0 && rest.Count < words.Length ? string.Join(' ', rest) : rest.Count == 0 ? "" : sentence);
		if (places.Count == 1) return new SaidAnswers([places[0] - 1], done, LastSure);

		if (done.Count > 0)
		{
			var doing = candidates.Where(i => DoneBy(stop.Answers[i].Command) is { } d && done.Contains(d)).ToList();
			if (doing.Count > 0) return new SaidAnswers(doing, done, LastSure);
			// Said what to do, and none of these does it: an answer about something leads to answers that may
			// (an offer, then accepting it), when the sentence said which, or there is one; only for what such
			// answers lead to ("kapat" at a message is not opening it).
			var about = candidates.Where(i => DoneBy(stop.Answers[i].Command) == null).ToList();
			if (about.Count > 0 && (best > 0 || about.Count == 1 || LastSure && done.Overlaps(["open", "view", "answer"]))
			    && done.Overlaps(["accept", "reject", "stall", "view", "open", "answer"]))
				return new SaidAnswers(about, done, LastSure);
			return null;
		}
		return best > 0 ? new SaidAnswers(candidates, done) : null;
	}

	/// <summary>
	/// The command the sentence says at the stop: one answer run; several as good asked among, by the numbers the
	/// stop showed them with; null when it says none, and the sentence goes to the router.
	/// </summary>
	internal static (string[] Command, IReadOnlySet<string> Done, bool Sure)? StopAnswer(HeldStop stop, string sentence, bool ask = true)
	{
		if ((stop.Kind == LinesKind ? LinesSaid(stop, sentence) : AnswersSaid(stop, sentence)) is not { Places.Count: > 0 } said) return null;
		var at = said.Places[0];
		if (said.Places.Count > 1)
		{
			if (!ask) return null;
			Console.WriteLine(L("Did you mean one of these?"));
			foreach (var i in said.Places) Console.WriteLine($"  {i + 1}. {stop.Answers[i].Label}");
			Console.Write(L("Which one (its number, Enter: none)? "));
			var typed = Console.ReadLine()?.Trim();
			if (!int.TryParse(typed, out var n) || !said.Places.Contains(n - 1)) return ([], new HashSet<string>(), true);
			at = n - 1;
		}
		var (label, command) = stop.Answers[at];
		// one of the game's own lines leads nowhere further: it is the answer
		var leads = stop.Kind != LinesKind && DoneBy(command) == null;
		// Read, but not clearly: shown, and run on a yes (an answer that leads on is only a look, and goes).
		if (!said.Sure && !leads && said.Places.Count == 1 && !SureOf(stop, at, ask)) return ([], new HashSet<string>(), true);
		Console.WriteLine(Dim($"→ {at + 1}. {label}"));
		// What was said to be done, for the answers the chosen one leads to; nothing more when it did it.
		return (command, leads ? said.Done : new HashSet<string>(), said.Sure);
	}

	/// <summary>The answer the model read without being sure of it, asked about: true on a yes.</summary>
	private static bool SureOf(HeldStop stop, int at, bool ask)
	{
		if (!ask) return false;
		Console.WriteLine(L("Did you mean this?"));
		Console.WriteLine($"  {at + 1}. {stop.Answers[at].Label}");
		return Confirm(L("Run it?"));
	}

	/// <summary>How the model read the last sentence: each label's share, for the bench.</summary>
	internal static string LastVotes = "";

	/// <summary>Whether the model read the last sentence clearly.</summary>
	private static bool LastSure = true;

	private static Nl.StopKinds? stopKinds;

	/// <summary>What an answer about something leads to answers for (an offer: accept, stall …; a message: open it).</summary>
	private static readonly string[] LeadsTo = ["accept", "reject", "stall", "view", "open", "answer"];

	/// <summary>
	/// What the sentence asks done, read by the model among the kinds of answer the stop has (and, for answers
	/// about something, the kinds those lead to); none when the model is not installed, or it reads something else.
	/// </summary>
	private static HashSet<string> KindAsked(HeldStop stop, string sentence)
	{
		LastSure = true;
		if (sentence.Length == 0 || Model() is not { } router || embedder == null) return [];
		stopKinds ??= new Nl.StopKinds(router, embedder, ModelDir);
		var kinds = stop.Answers.Select(a => DoneBy(a.Command)).OfType<string>().ToHashSet();
		if (stop.Answers.Any(a => DoneBy(a.Command) == null)) kinds.UnionWith(LeadsTo);
		// The router's own examples for what these answers run ask the same in other words; going on is every stop's.
		var theirs = stop.Answers.Select(a => a.Command switch { ["setpieces", "new", ..] => "fm_setpieces_new", [var c, ..] => $"fm_{c}", _ => "" })
			.Append("fm_next").Append("fm_continue").ToHashSet();
		var (asked, sure, votes) = stopKinds.Ask(sentence, kinds, theirs);
		LastSure = sure;
		LastVotes = string.Join(" ", votes.Take(3).Select(v => $"{v.Label}:{v.Share:0.00}"));
		return [.. asked];
	}

	/// <summary>The one answer of the stop that does what was said; null when none or several do.</summary>
	internal static string[]? DoneAnswer(HeldStop stop, IReadOnlySet<string> done, bool sure = true)
	{
		var doing = Enumerable.Range(0, stop.Answers.Count).Where(i => DoneBy(stop.Answers[i].Command) is { } d && done.Contains(d)).ToList();
		if (doing is not [var at] || !sure && !SureOf(stop, at, ask: true)) return null;
		Console.WriteLine(Dim($"→ {at + 1}. {stop.Answers[at].Label}"));
		return stop.Answers[at].Command;
	}

	private static readonly string[] Endings = ["nin", "nun", "in", "un", "yi", "yu", "ya", "ye", "ni", "nu", "na", "ne", "i", "u", "a", "e", "dan", "den", "da", "de"];

	/// <summary>A typed word against a word of the game's own: as a name fits, or its start once a Turkish ending goes ("fenerin" for Fenerbahçe).</summary>
	private static int Fits(string typed, string word) =>
		NameFit.Fit(typed, word) is var fit and > 0 ? fit
		: Endings.Any(e => typed.Length - e.Length >= 4 && typed.EndsWith(e, StringComparison.Ordinal) && word.StartsWith(typed[..^e.Length], StringComparison.Ordinal)) ? 2 : 0;

	/// <summary>A word typed alone that is a command, an alias of one or the start of one only: it stays that command.</summary>
	internal static bool IsCommandWord(string word)
	{
		var folded = Fold(word);
		return CommandNames.Contains(word) || CommandAliases.ContainsKey(folded) || word.StartsWith('-')
		       || folded.Length >= 2 && CommandNames.Count(c => c.StartsWith(folded, StringComparison.Ordinal)) == 1;
	}
}
