using System.Text.Json;

namespace FmCli;

/// <summary>
/// Where `next` stopped, as a thing to answer: its kind, what it is about (in the game's words) and
/// the answers it takes, each one a command held by the game's own ids (a message's UniqueId, the
/// offer's place in the game's own list, read again when it runs). `Explain` names it; the session
/// shows it with numbered answers, takes a number as one of them, and once answered goes on to
/// where it was told to go (`next --until match`). Outside the session the stop is only said, as before.
/// </summary>
internal static partial class Program
{
	internal sealed record HeldStop(string Kind, List<string> About, List<(string Label, string[] Command)> Answers)
	{
		/// <summary>What `Explain` wrote for it, said in place of the answers when it has none.</summary>
		public string Told { get; set; } = "";
		public List<string> Notes { get; } = [];
	}

	/// <summary>The stop `next` came to last; null when it went on, or nothing has been asked of it yet.</summary>
	internal static HeldStop? StopNow { get; private set; }

	/// <summary>Where `next` was last told to go (match, message); null for a single step.</summary>
	internal static string? GoalNow { get; set; }

	/// <summary>Set by the session: stops are shown by it, as answers, and `Explain` writes nothing of its own.</summary>
	internal static bool SessionShowsStops { get; set; }

	internal static void ForgetStop() => StopNow = null;

	private static void Halt(string kind, IEnumerable<string>? about = null, params (string Label, string[] Command)[] answers) =>
		StopNow = new HeldStop(kind, about?.Where(a => a.Length > 0).Select(GameWords.Of).ToList() ?? [], answers.ToList());

	/// <summary>The answer that opens a message, held by the game's own id for it.</summary>
	private static (string, string[]) Open(JsonElement message) =>
		(GameWords.Of(Field(message, "NewsTitleString")), ["reply", $"id:{Field(message, "UniqueId")}"]);

	/// <summary>A stop of waiting messages: each one an answer.</summary>
	private static void HaltOnMessages(string kind, IEnumerable<JsonElement> messages)
	{
		var list = messages.Where(m => Field(m, "UniqueId").Length > 0).ToList();
		// An offer found first stays the stop: the offers' own messages would only say them again, and any other
		// message waits behind the offers for the next stop. Our accepted bid is answered in its message, so its
		// messages are answers beside its own.
		if (StopNow is { Kind: "offer", Answers.Count: > 0 }) return;
		var before = StopNow is { Kind: "bid" } first ? first : null;
		Halt(before?.Kind ?? kind, null, [.. before?.Answers ?? [], .. list.Select(Open)]);
	}

	/// <summary>The kind of stop as said to the user.</summary>
	private static string StopName(string kind) => kind switch
	{
		"offer" => L("Offers for our players"),
		"bid" => L("A bid of ours was accepted"),
		"setpieces" => L("The set piece wizard"),
		"vision" => L("The club vision meeting"),
		"talk" => L("A conversation is open"),
		"tour" => L("A guided tour"),
		"message" => L("A message waits for an answer"),
		"again" => L("A message may want answering again"),
		"read" => L("A message to read"),
		"press" => L("A press conference"),
		"match" => L("Match day"),
		"teamnews" => L("Team news before the match"),
		"teamtalk" => L("A team talk"),
		"tunnel" => L("A tunnel interview"),
		"question" => L("An open question"),
		"busy" => L("The game is busy"),
		"holiday" => L("On holiday"),
		_ => L("The game is waiting"),
	};

	/// <summary>
	/// The session's way of saying a stop: its kind, what it is about, then its answers numbered, which a number
	/// alone then picks (the pick list is the stop's until another list is shown).
	/// </summary>
	internal static void ShowStop(HeldStop stop)
	{
		Console.WriteLine();
		if (Ui.Rich) Ui.Line($"[bold]{Ui.Safe(StopName(stop.Kind))}[/]");
		else Console.WriteLine(StopName(stop.Kind));
		foreach (var about in stop.About) Console.WriteLine($"  {about}");
		if (stop.Answers.Count == 0)
		{
			foreach (var line in stop.Told.Split('\n', StringSplitOptions.RemoveEmptyEntries)) Console.WriteLine($"  {line.TrimEnd()}");
		}
		else
		{
			for (var i = 0; i < stop.Answers.Count; i++)
				Console.WriteLine($"  {i + 1}. {stop.Answers[i].Label}");
		}
		foreach (var note in stop.Notes) Console.WriteLine($"  {note}");
		if (stop.Answers.Count > 0)
			Ui.Hint(stop.Answers.Count == 1
				? L("To answer: 1   ·   a question is answered and the stop waits")
				: L($"To answer: 1–{stop.Answers.Count}   ·   a question is answered and the stop waits"));
		PickStop(stop);
	}

	/// <summary>The stop's answers as the list a number alone picks from.</summary>
	internal static void PickStop(HeldStop stop)
	{
		if (stop.Answers.Count > 0) ShowedList(L("answers"), stop.Answers.ToList());
	}

	/// <summary>Whether a command run is an answer to the stop: one of its own, or the same command with more said.</summary>
	internal static bool Answers(HeldStop stop, string[] ran) =>
		ran.Length > 0 && stop.Answers.Any(a =>
			a.Command.Length > 0 && QuietCommand(ran[0]) == QuietCommand(a.Command[0])
			&& (ran.SequenceEqual(a.Command) || ran.Length > 1 || a.Command.Length == 1));
}
