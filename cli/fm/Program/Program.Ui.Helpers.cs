using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<int> Dropdowns(bool json, string[] args)
	{
		var dropdowns = await Fm.GetJson("dropdowns", ("json", "1"));
		if (json)
		{
			Console.WriteLine(dropdowns.ToString());
			return 0;
		}

		var filter = Words(args).FirstOrDefault();
		var rows = dropdowns.EnumerateArray()
			.Where(d => args.Contains("--all") || Field(d, "Enabled") == "True")
			.Select(d => new[]
			{
				Field(d, "Name"),
				Field(d, "Label"),
				string.Join(" · ", d.GetProperty("Choices").EnumerateArray().Select(c => c.GetString() ?? "")),
			})
			.Where(r => filter == null || r.Any(cell => cell.Contains(filter, StringComparison.OrdinalIgnoreCase)))
			.ToList();

		Table.Write(new[] { L("LIST"), L("SELECTED"), L("OPTIONS") }, rows);
		return 0;
	}

	private static async Task<int> Press(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count == 0)
		{
			Console.Error.WriteLine(L("usage: press \"<label>\" [--name <name>] [--dry]   (for the labels: `buttons`)"));
			return 64;
		}

		var query = new List<(string, string)> { ("text", words[0]) };
		if (Option(args, "--name") is { } name) query.Add(("name", name));
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		var answer = await Arriving("press", query.ToArray());
		// A control's own label is the game's English ("Recent Results") while the screen shows
		// this language's word for it ("Recent Results"); a command typed in the shown words is
		// looked up again in the table.
		if (answer.Contains("no control labelled", StringComparison.Ordinal)
		    && GameWords.English(words[0]) is { } english && english != words[0])
		{
			var again = new List<(string, string)> { ("text", english) };
			if (Option(args, "--name") is { } n) again.Add(("name", n));
			if (args.Contains("--dry")) again.Add(("dry", "1"));
			answer = await Arriving("press", again.ToArray());
		}
		return Report(answer);
	}

	private static async Task<int> Select(string[] args)
	{
		var words = Words(args).ToList();
		if (words.Count < 2)
		{
			Console.Error.WriteLine(L("usage: select <list> \"<option>\" [--dry]   (for the lists: `dropdowns`)"));
			return 64;
		}

		var query = new List<(string, string)> { ("name", words[0]), ("choice", words[1]) };
		if (args.Contains("--dry")) query.Add(("dry", "1"));

		return Report(await Arriving("select", query.ToArray()));
	}

	/// <summary>
	/// Asks again for a short while when the control is not there yet. A screen takes a moment
	/// to build after the press that opened it, and two commands in a row should not have to
	/// know how long.
	/// </summary>
	private static async Task<string> Arriving(string route, (string, string)[] query)
	{
		var answer = "";
		// Not there yet, or the game is still sending what the last press queued: a page that is
		// opening holds the queue for a moment. Either clears by itself or is reported once the
		// wait is up; a name the game's own page list does not have never will, and asking again
		// for it only spends the whole wait (measured: nine requests and nine seconds to the same
		// answer).
		await Until(async () => !Unanswered(answer = await Fm.Get(route, query)), seconds: 8, every: 1);
		if (Environment.GetEnvironmentVariable("FM_TRACE") == "1")
			Console.Error.WriteLine($"    -> {answer.Split('\n')[0]}");
		return answer;
	}

	/// <summary>An answer the game has yet to give: the thing asked for is not drawn yet, or the press that would draw it is still queued.</summary>
	private static bool Unanswered(string answer) =>
		(answer.StartsWith("error: no ", StringComparison.Ordinal)
		 && !answer.StartsWith("error: no page named", StringComparison.Ordinal)
		 // The tab was opened for us and its bar is still being built: asked again, each press queues the tab
		 // once more and the queue ends held. The answer is taken, and the bar waited on (OpenPage).
		 && !answer.Contains("no subtab bar", StringComparison.Ordinal))
		|| answer.StartsWith("error: the event queue is blocked", StringComparison.Ordinal);

	/// <summary>The bridge answers in plain text and marks a refusal by how the line starts.</summary>
	private static int Report(string answer)
	{
		var failed = answer.StartsWith("error", StringComparison.Ordinal);
		(failed ? Console.Error : Console.Out).WriteLine(answer.TrimEnd());
		return failed ? 1 : 0;
	}

	/// <summary>A command's positional arguments: everything that is neither a flag nor a flag's value.</summary>
	private static IEnumerable<string> Words(string[] args)
	{
		for (var i = 1; i < args.Length; i++)
		{
			if (args[i] is "--for" or "--by" or "--analysts" or "--ability" or "--potential" or "--area" or "--priority" or "--max-value" or "--attr" or "--age" or "--grade" or "--contract" or "--employed" or "--reputation" or "--note" or "--name" or "--tab" or "--view" or "--filter" or "--wage" or "--term" or "--focus" or "--position" or "--role" or "--captain" or "--vice" or "--coach" or "--trait" or "--trait-remove" or "--control" or "--record" or "--stat" or "--top" or "--to" or "--list" or "--new" or "--jobs" or "--player" or "--date" or "--rules" or "--pick" or "--of") { i++; continue; }
			if (!args[i].StartsWith("--", StringComparison.Ordinal)) yield return args[i];
		}
	}

	private static string Option(string[] args, string flag)
	{
		var at = Array.IndexOf(args, flag);
		return at >= 0 && at + 1 < args.Length ? args[at + 1] : null;
	}

	/// <summary>
	/// A message holds the game up when it is a priority one still unanswered. Being answerable
	/// is not enough: some messages only offer something optional, like cancelling a camp.
	/// </summary>
	internal static bool Blocking(JsonElement message) =>
		Field(message, "NewsItemIsPriority") == "True" && Answerable(message);

	private static bool StillAnswerable(JsonElement inbox, string id) =>
		inbox.EnumerateArray().Any(m => Field(m, "UniqueId") == id && Answerable(m));

	internal static bool Answerable(JsonElement message) =>
		Field(message, "CanRespond") == "True" && Field(message, "NewsHasResponded") != "True";

	private static string Flags(JsonElement message)
	{
		var flags = new List<string>();
		if (Field(message, "NewsItemHasBeenRead") != "True") flags.Add(L("unread"));
		if (Blocking(message)) flags.Add(L("awaiting an answer"));
		else if (Answerable(message)) flags.Add(L("can be answered"));
		return string.Join(", ", flags);
	}

	/// <summary>
	/// A dialog's title, description or button label ("Title", "Description", "Label") as the window
	/// shows it; the English one put back into the game's words from an older plugin. Only for showing:
	/// matching goes by the English field.
	/// </summary>
	internal static string DialogWords(JsonElement element, string field) =>
		Field(element, "Shown" + field) is { Length: > 0 } shown ? shown : GameWords.Of(Field(element, field));

	/// <summary>
	/// A dialog's answer by its place rather than its words: the game puts the
	/// answer that goes ahead (Yes, Confirm, Ok) in the primary slot and the one that backs out (No,
	/// Cancel) in the secondary. The bridge takes the slot from the game's dialog manager; failing
	/// that, the button's element is named for it ("buttons-button-primary-default").
	/// </summary>
	internal static JsonElement? DialogAnswer(JsonElement dialog, bool agree)
	{
		var slot = agree ? "primary" : "secondary";
		var buttons = dialog.GetProperty("Buttons").EnumerateArray().ToList();
		foreach (var b in buttons) if (Field(b, "Slot") == slot) return b;
		foreach (var b in buttons) if (Field(b, "Name").Contains($"-{slot}-", StringComparison.Ordinal)) return b;
		return null;
	}

	/// <summary>
	/// Answers the dialog on screen until it is gone: a click while it is still being drawn is lost.
	/// True once no dialog is left.
	/// </summary>
	internal static async Task<bool> AnswerDialog(bool agree) =>
		await Until(async () =>
		{
			if (await Fm.GetJson("dialog") is not { ValueKind: JsonValueKind.Array } now || now.GetArrayLength() == 0) return true;
			// By the game's own handler; a simulated click on the answer often leaves the question up.
			if ((await Fm.Get("dialog", ("press", agree ? "primary" : "secondary"))).StartsWith("error", StringComparison.Ordinal)
			    && DialogAnswer(now[0], agree) is { } answer) await Fm.Get("click", ("ptr", Field(answer, "Pointer")));
			return false;
		}, seconds: 8, every: 0.5);

	/// <summary>A dialog that asks: one answer goes ahead, another backs out.</summary>
	internal static bool DialogAsks(JsonElement dialog) => DialogAnswer(dialog, true) != null && DialogAnswer(dialog, false) != null;
}
