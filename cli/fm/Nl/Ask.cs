using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FmCli.Nl;

/// <summary>
/// The assistant, read-only (T208): a question answered from what the game says. The model is offered
/// the read tools only; each call it asks for is checked again and run as the command would be, and
/// its answer is held to what was read: a name in it that no read, question or earlier answer holds
/// is said to be unread. The talk is kept per save for a while, so "him" in the next question is
/// the player the last answer was about.
/// </summary>
internal static class Ask
{
	public sealed record Turn(string Q, string A, List<string> Read);

	private sealed record Talk(string Save, DateTime At, List<Turn> Turns);

	/// <summary>The answer and what it stands on: the commands read, calls refused, names no read holds.</summary>
	public sealed record Result(string Answer, List<string> Read, List<string> Refused, List<string> Unread, double Seconds, int Tokens, string? Error)
	{
		/// <summary>The notes the model offered to keep or forget, and what became of each.</summary>
		public List<Offered> Offers { get; init; } = [];

		/// <summary>The notes the answer stands on, as it cites them (#N).</summary>
		public List<Memory.Note> Cited { get; init; } = [];
	}

	/// <summary>A note the model offers: one to keep (in place of <see cref="Replaces"/> when given), or <see cref="Forget"/> one.</summary>
	public sealed record Offer(string Text, string Kind, int? Replaces, int? Forget);

	/// <summary>What became of an offer: <see cref="Kept"/> on a yes, <see cref="Asked"/> false when the user could not be asked.</summary>
	public sealed record Offered(Offer Offer, bool Asked, bool Yes, Memory.Note? Kept);

	/// <summary>A talk left this long is over: the next question starts a new one.</summary>
	private static readonly TimeSpan Idle = TimeSpan.FromHours(2);

	private const int KeptTurns = 8;
	private const int Rounds = 8;
	private const int MaxCalls = 14;
	private const int MaxRead = 8000;

	private static string File =>
		Environment.GetEnvironmentVariable("FM_ASK_FILE") is { Length: > 0 } other ? other
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fm-cli", "ask.json");

	private const string Prompt = """
		You help someone play Football Manager from a terminal tool. You can only read the game, with the tools given; you change nothing.
		Answer only from what the tools returned in this talk. Never from your own knowledge of football, players or clubs: if the tools do not say it, say you could not read it.
		Read before you answer, and read again rather than guess (a player's details: fm_player with his name).
		A question about someone named in the talk before ("him", "onu", "that player") means that one.
		A player is ours only when a tool shows it (fm_squad lists ours; fm_player gives his club). Never assume it.
		Keep names of players, clubs, competitions and nations exactly as the tool writes them, even when you answer in another language.
		Your earlier answers in this talk are not facts and may be wrong: take every figure from a tool result, and read again when you need one. Never correct a tool result from an earlier answer.
		The user plays only through this tool: never send them to the game's own screens; name a command.
		Answer in the language of the question, short and plain: a few lines, names and numbers as the tools show them. No headings.
		Keep every number with its unit and period exactly as the tool shows it: a wage per year stays per year, a value range stays a range, a date stays written as the tool writes it (30/6/2031). Do not convert or work out new figures unless asked.
		When the user tells you something to keep for later (a wish, a rule for the club, a plan, something about themselves), call keep_note with it: a short sentence in the user's language, in their own voice as they would write it ("I want…", "never sell…"). The user is asked before it is kept. When it changes or takes back an earlier note, give replaces with that note's number, or call forget_note. Never keep what a tool read (it is read again next time), a question, or an idea of your own. Asking you a question is not asking you to keep anything.
		When asked to do something (sell, sign, change the tactic, answer a message), say you only read, what you read that bears on it, and the command that would do it, from the list of commands that act below, in backticks. Otherwise do not say that you only read.
		""";

	/// <summary>The talk kept for <paramref name="save"/>, or a new one; <paramref name="fresh"/> starts one anyway.</summary>
	private static Talk Load(string save, bool fresh)
	{
		if (fresh) return new Talk(save, DateTime.UtcNow, []);
		try
		{
			var kept = JsonSerializer.Deserialize<Talk>(System.IO.File.ReadAllText(File));
			if (kept != null && kept.Save == save && DateTime.UtcNow - kept.At < Idle) return kept;
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
		return new Talk(save, DateTime.UtcNow, []);
	}

	private static void Keep(Talk talk)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File)!);
			System.IO.File.WriteAllText(File, JsonSerializer.Serialize(talk with { At = DateTime.UtcNow, Turns = [.. talk.Turns.TakeLast(KeptTurns)] }));
			if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	/// <summary>Forgets the talk.</summary>
	public static void Forget()
	{
		try { System.IO.File.Delete(File); }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
	}

	/// <summary>
	/// The answer to <paramref name="question"/>. <paramref name="save"/> names the save the talk belongs to,
	/// <paramref name="context"/> is what is known without reading (our club); <paramref name="run"/> runs a
	/// command's words as the CLI would; <paramref name="reading"/> is told each read as it starts.
	/// </summary>
	public static async Task<Result> Answer(IChatModel model, string question, string save, string context, bool fresh,
		Func<string[], Task<int>> run, Action<string>? reading = null, Memory.Book? book = null, Func<Offer, Offered>? offer = null)
	{
		var started = Environment.TickCount64;
		var talk = Load(save, fresh);
		book ??= new Memory.Book();
		var tools = new JsonArray();
		foreach (var (name, description, schema) in Mcp.ReadTools())
			tools.Add(Function(name, description, schema));
		tools.Add(Function("keep_note", "Offer to keep something the user told you for later in this save: a wish, a rule for the club, a plan, something about themselves. The user is asked first.",
			JsonNode.Parse("""{"type":"object","properties":{"text":{"type":"string","description":"the note, short, in the user's language"},"kind":{"type":"string","enum":KINDS},"replaces":{"type":"integer","description":"the number of the note this one changes"}},"required":["text","kind"]}"""
				.Replace("KINDS", JsonSerializer.Serialize(Memory.Kinds)))!));
		tools.Add(Function("forget_note", "Offer to forget a kept note the user takes back. The user is asked first.",
			JsonNode.Parse("""{"type":"object","properties":{"number":{"type":"integer"}},"required":["number"]}""")!));

		var acts = string.Join("\n", Mcp.ActCommands().Select(a => $"- {a.Words}: {a.About}"));
		var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = $"{Prompt}\n{context}{Memory.ForModel(book)}\n\nCommands that act, which you cannot run (name one when asked to do something):\n{acts}" } };
		foreach (var turn in talk.Turns)
		{
			messages.Add(new JsonObject { ["role"] = "user", ["content"] = turn.Q });
			messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn.A });
		}
		messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });

		var read = new List<string>();
		var refused = new List<string>();
		var offers = new List<Offered>();
		var seen = new List<string> { question, context };
		seen.AddRange(talk.Turns.SelectMany(t => new[] { t.Q, t.A }));
		seen.AddRange(book.Notes.Select(n => n.Text));
		var tokens = 0;
		var calls = 0;
		double Seconds() => (Environment.TickCount64 - started) / 1000.0;

		for (var round = 0; round < Rounds; round++)
		{
			// The last round is offered no tools: what was read so far is answered from.
			var last = round == Rounds - 1 || calls >= MaxCalls;
			var reply = await model.Next(messages, last ? [] : tools, 60);
			tokens += reply.Tokens;
			if (reply.Error != null) return new Result("", read, refused, [], Seconds(), tokens, reply.Error) { Offers = offers };
			if (reply.Calls.Count == 0 || last)
			{
				if (reply.Text.Length == 0) return new Result("", read, refused, [], Seconds(), tokens, "no answer") { Offers = offers };
				talk.Turns.Add(new Turn(question, reply.Text, read));
				Keep(talk);
				return new Result(reply.Text, read, refused, Unread(reply.Text, seen), Seconds(), tokens, null)
				{ Offers = offers, Cited = [.. Memory.Cited(reply.Text, book).Where(n => !offers.Any(o => o.Kept?.N == n.N))] };
			}
			messages.Add(reply.Message!);
			foreach (var call in reply.Calls)
				messages.Add(call.Name is "keep_note" or "forget_note"
					? Noted(call, book, offer, offers)
					: await Read(call, ++calls > MaxCalls, run, read, refused, seen, reading));
		}
		return new Result("", read, refused, [], Seconds(), tokens, "no answer") { Offers = offers };
	}

	private static JsonObject Function(string name, string description, JsonNode schema) =>
		new() { ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = schema } };

	/// <summary>
	/// A note the model offers to keep or forget, checked (<see cref="Memory.NotANote"/>) and put to the user by
	/// <paramref name="offer"/>; the tool message that tells the model what became of it. Nothing is kept without a yes.
	/// </summary>
	internal static JsonObject Noted(ToolCall call, Memory.Book book, Func<Offer, Offered>? offer, List<Offered> offers)
	{
		int? Int(string key) => call.Args[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i
			: call.Args[key] is JsonValue s && s.TryGetValue<string>(out var t) && int.TryParse(t.TrimStart('#'), out var j) ? j : null;
		var wanted = call.Name == "forget_note"
			? new Offer("", "", null, Int("number"))
			: new Offer(call.Args["text"] is JsonValue tv && tv.TryGetValue<string>(out var x) ? x.Trim() : "",
				call.Args["kind"] is JsonValue kv && kv.TryGetValue<string>(out var k) ? k : "wish", Int("replaces"), null);
		var fault = wanted.Forget is { } f
			? (book.Notes.Any(n => n.N == f) ? null : $"there is no note #{f}")
			: call.Name == "forget_note" ? "number is missing"
			: Memory.NotANote(wanted.Text, book, wanted.Replaces);
		string result;
		if (fault != null) result = $"Not offered: {fault}.";
		else if (offer == null) result = "Not kept: notes cannot be kept here.";
		else
		{
			var done = offer(wanted);
			offers.Add(done);
			result = !done.Asked ? "Not kept: the user could not be asked here. Tell them the command that keeps it: memory add <note>, or memory delete <number>."
				: !done.Yes ? "The user said no: nothing was kept or forgotten."
				: wanted.Forget is { } gone ? $"Forgotten: note #{gone}."
				: $"Kept as note #{done.Kept?.N}.";
		}
		return new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = result };
	}

	/// <summary>
	/// A read the model asked for, run when it only reads (<see cref="Mcp.RunRead"/>), as the tool message
	/// that answers it; <paramref name="enough"/> refuses it, nothing run. What ran goes to <paramref name="read"/>
	/// and its text to <paramref name="seen"/>; a call that could act goes to <paramref name="refused"/>.
	/// </summary>
	internal static async Task<JsonObject> Read(ToolCall call, bool enough, Func<string[], Task<int>> run,
		List<string> read, List<string> refused, List<string> seen, Action<string>? reading)
	{
		var words = Mcp.CommandFor(call.Name, call.Args) is { } w ? string.Join(" ", w) : call.Name;
		reading?.Invoke(words);
		var (text, ran) = enough
			? ("Not run: enough has been read; answer from it.", false)
			: await Mcp.RunRead(call.Name, call.Args, run);
		if (ran)
		{
			read.Add(words);
			seen.Add(text);
		}
		else if (!enough) refused.Add(words);
		if (text.Length > MaxRead) text = text[..MaxRead] + "\n…(cut: ask for less, e.g. one player)";
		return new JsonObject { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = text };
	}

	/// <summary>
	/// The names in an answer that nothing read holds: a word starting with a capital inside a sentence
	/// (a sentence's own first word is left alone, and so is what stands in `backticks`), looked for in the
	/// reads, the question and the talk before, letters folded. A name with a suffix ("Rinaldi'nin") is
	/// looked for without it.
	/// </summary>
	internal static List<string> Unread(string answer, IEnumerable<string> seen)
	{
		var corpus = Text.Fold(string.Join("\n", seen));
		var plain = Regex.Replace(answer, "`[^`]*`", " ");
		plain = plain.Replace("**", "").Replace("__", "");
		var unread = new List<string>();
		foreach (Match m in Regex.Matches(plain, @"[\p{L}][\p{L}\p{M}'’\-]*"))
		{
			var word = m.Value;
			if (!char.IsUpper(word[0])) continue;
			var before = plain[..m.Index].TrimEnd(' ', '\t', '*', '_', '"', '(', '[');
			if (before.Length == 0 || before[^1] is '.' or '!' or '?' or ':' or '\n' or '-' or '•' || Regex.IsMatch(before, @"(^|\n)\s*(\d+[.)]|[-*•])$")) continue;
			var stem = word.Split('\'', '’')[0].Trim('-');
			if (stem.Length < 2) continue;
			var folded = Text.Fold(stem);
			if (folded.Length == 0 || corpus.Contains(folded, StringComparison.Ordinal)) continue;
			if (!unread.Contains(stem)) unread.Add(stem);
		}
		return unread;
	}
}
