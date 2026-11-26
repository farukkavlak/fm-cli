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

	/// <summary>
	/// The user's words for each thing an answer does, folded (Turkish letters as the router folds them), as
	/// Text.Matches reads a cue: four letters or more match as a word's start, shorter ones whole, "*" a start too.
	/// </summary>
	private static readonly (string Done, string[] Cues)[] SaidDone =
	[
		("accept", ["kabul", "kabl", "evet", "tamam", "onayla", "olur$", "uygun", "sat$", "satal*", "satin$", "accept", "yes", "ok$", "okay", "agree", "sell"]),
		("reject", ["reddet", "red$", "ret$", "hayir", "olmaz", "istemiyorum", "reject", "refuse", "decline", "no$", "nope", "turn down", "turn it down"]),
		("stall", ["beklet", "oyala", "bekle", "sonra$", "stall", "wait", "later", "hold"]),
		("view", ["goster", "bak$", "bakal*", "bakay*", "gor$", "goreyim", "incele", "detay", "view", "see$", "show", "look", "details"]),
		("play", ["oyna*", "izle*", "canli", "play", "watch", "live"]),
		("instant", ["sonuc", "direkt", "hizli", "simule", "aninda", "instant", "result", "quick", "sim$", "simulate"]),
		("goon", ["devam", "ilerle", "surdur", "continue", "go$", "go on", "carry on", "proceed"]),
		("skip", ["atla", "gec$", "gecelim", "cevaplama*", "cevapsiz", "skip", "ignore", "without"]),
		("end", ["bitir", "sonlandir", "kapat", "end$", "finish", "close", "done"]),
		("open", ["ac$", "acal*", "acay*", "oku", "open", "read"]),
		("answer", ["cevapla", "yanitla", "konus", "soyle", "answer", "reply", "respond", "talk", "say$"]),
	];

	/// <summary>Places in a list as said: the n-th, by the count from one; "last" is -1.</summary>
	private static readonly (int Place, string[] Cues)[] SaidPlace =
	[
		(1, ["ilk*", "birinci*", "first", "1st"]), (2, ["ikinci*", "second", "2nd"]), (3, ["ucuncu*", "third", "3rd"]),
		(4, ["dorduncu*", "fourth", "4th"]), (5, ["besinci*", "fifth", "5th"]), (6, ["altinci*", "sixth", "6th"]),
		(-1, ["son$", "sonuncu*", "sondaki", "last"]),
	];

	/// <summary>Words that start a question, or ask one in Turkish ("mı"): a question at a stop is answered and the stop waits.</summary>
	private static readonly string[] Asking =
		["kim$", "kimi$", "kime$", "ne$", "neler", "nedir", "neden", "nasil", "kac$", "kaca$", "hangi*", "mi$", "mu$", "misin", "musun", "midir", "nerede", "niye",
		 "who", "whos", "what", "whats", "when", "how", "which", "why", "where", "should", "is$", "are$", "does", "do$", "can$"];

	/// <summary>Little words that say nothing about which answer: left out when an answer's own words are looked for.</summary>
	private static readonly HashSet<string> Little =
		["bu", "su", "o", "bir", "ve", "ile", "icin", "et", "etsene", "edelim", "yap", "olsun", "lutfen", "teklif", "teklifi", "teklifini", "teklifine", "mesaj", "mesaji",
		 "the", "a", "an", "it", "that", "this", "one", "of", "for", "to", "please", "offer", "message", "from", "and"];

	internal sealed record SaidAnswers(List<int> Places, IReadOnlySet<string> Done);

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
		var text = " " + string.Join(' ', words) + " ";
		bool Says(string cue) => cue.Contains(' ') ? text.Contains($" {cue} ", StringComparison.Ordinal) : words.Any(w => Text.Matches(w, cue, typo: cue.TrimEnd('$', '*').Length >= 6));
		// One typo is forgiven in a long cue only: a name is no word for doing ("baray" is not "bakay").

		if (sentence.TrimEnd().EndsWith('?') || Asking.Any(Says)) return null;

		var done = SaidDone.Where(d => d.Cues.Any(Says)).Select(d => d.Done).ToHashSet();

		// A place: a number of the list said in the sentence, or a word for one.
		var places = words.Where(w => int.TryParse(w, out var n) && n >= 1 && n <= count).Select(int.Parse)
			.Concat(SaidPlace.Where(p => p.Cues.Any(Says)).Select(p => p.Place == -1 ? count : p.Place))
			.Where(p => p >= 1 && p <= count).Distinct().ToList();
		if (places.Count == 1) return new SaidAnswers([places[0] - 1], done);
		if (places.Count > 1) return null;

		// The answer's own words, for the answers about something: the user's words not taken by a cue.
		var cued = SaidDone.SelectMany(d => d.Cues).Concat(Asking).ToList();
		var own = words.Where(w => w.Length >= 3 && !Little.Contains(w) && !w.All(char.IsDigit)
		                           && !cued.Any(c => !c.Contains(' ') && Text.Matches(w, c, typo: false))).ToList();
		var fit = stop.Answers.Select(a => DoneBy(a.Command) != null || own.Count == 0 ? 0
			: own.Sum(w => NameFit.Words(a.Label).Select(l => Fits(w, l)).DefaultIfEmpty(0).Max())).ToList();
		var all = Enumerable.Range(0, count).ToList();
		var best = fit.Max();
		// Words of its own that fit no answer: the sentence is about something else ("rinaldiyi sat" at a message).
		if (best == 0 && own.Count > 0 && fit.Count(f => f == 0) == count && stop.Answers.Any(a => DoneBy(a.Command) == null))
			return null;
		var candidates = best > 0 ? all.Where(i => fit[i] == best).ToList() : all;

		if (done.Count > 0)
		{
			var doing = candidates.Where(i => DoneBy(stop.Answers[i].Command) is { } d && done.Contains(d)).ToList();
			if (doing.Count > 0) return new SaidAnswers(doing, done);
			// Said what to do, and none of these does it: an answer about something leads to answers that may
			// (an offer, then accepting it), when the sentence said which, or there is one; only for what such
			// answers lead to ("kapat" at a message is not opening it).
			var about = candidates.Where(i => DoneBy(stop.Answers[i].Command) == null).ToList();
			if (about.Count > 0 && (best > 0 || about.Count == 1 || done.Overlaps(["open", "view", "answer"]))
			    && done.Overlaps(["accept", "reject", "stall", "view", "open", "answer"]))
				return new SaidAnswers(about, done);
			return null;
		}
		return best > 0 ? new SaidAnswers(candidates, done) : null;
	}

	/// <summary>
	/// The command the sentence says at the stop: one answer run; several as good asked among, by the numbers the
	/// stop showed them with; null when it says none, and the sentence goes to the router.
	/// </summary>
	internal static (string[] Command, IReadOnlySet<string> Done)? StopAnswer(HeldStop stop, string sentence, bool ask = true)
	{
		if (AnswersSaid(stop, sentence) is not { Places.Count: > 0 } said) return null;
		var at = said.Places[0];
		if (said.Places.Count > 1)
		{
			if (!ask) return null;
			Console.WriteLine(L("Did you mean one of these?"));
			foreach (var i in said.Places) Console.WriteLine($"  {i + 1}. {stop.Answers[i].Label}");
			Console.Write(L("Which one (its number, Enter: none)? "));
			var typed = Console.ReadLine()?.Trim();
			if (!int.TryParse(typed, out var n) || !said.Places.Contains(n - 1)) return null;
			at = n - 1;
		}
		var (label, command) = stop.Answers[at];
		Console.WriteLine(Dim($"→ {at + 1}. {label}"));
		// What was said to be done, for the answers the chosen one leads to; nothing more when it did it.
		return (command, DoneBy(command) == null ? said.Done : new HashSet<string>());
	}

	/// <summary>The one answer of the stop that does what was said; null when none or several do.</summary>
	internal static string[]? DoneAnswer(HeldStop stop, IReadOnlySet<string> done)
	{
		var doing = Enumerable.Range(0, stop.Answers.Count).Where(i => DoneBy(stop.Answers[i].Command) is { } d && done.Contains(d)).ToList();
		if (doing is not [var at]) return null;
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
