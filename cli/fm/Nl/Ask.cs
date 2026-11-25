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
	public sealed record Result(string Answer, List<string> Read, List<string> Refused, List<string> Unread, double Seconds, int Tokens, string? Error);

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
		Func<string[], Task<int>> run, Action<string>? reading = null)
	{
		var started = Environment.TickCount64;
		var talk = Load(save, fresh);
		var tools = new JsonArray();
		foreach (var (name, description, schema) in Mcp.ReadTools())
			tools.Add(new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = schema } });

		var acts = string.Join("\n", Mcp.ActCommands().Select(a => $"- {a.Words}: {a.About}"));
		var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = $"{Prompt}\n{context}\n\nCommands that act, which you cannot run (name one when asked to do something):\n{acts}" } };
		foreach (var turn in talk.Turns)
		{
			messages.Add(new JsonObject { ["role"] = "user", ["content"] = turn.Q });
			messages.Add(new JsonObject { ["role"] = "assistant", ["content"] = turn.A });
		}
		messages.Add(new JsonObject { ["role"] = "user", ["content"] = question });

		var read = new List<string>();
		var refused = new List<string>();
		var seen = new List<string> { question, context };
		seen.AddRange(talk.Turns.SelectMany(t => new[] { t.Q, t.A }));
		var tokens = 0;
		var calls = 0;
		double Seconds() => (Environment.TickCount64 - started) / 1000.0;

		for (var round = 0; round < Rounds; round++)
		{
			// The last round is offered no tools: what was read so far is answered from.
			var last = round == Rounds - 1 || calls >= MaxCalls;
			var reply = await model.Next(messages, last ? [] : tools, 60);
			tokens += reply.Tokens;
			if (reply.Error != null) return new Result("", read, refused, [], Seconds(), tokens, reply.Error);
			if (reply.Calls.Count == 0 || last)
			{
				if (reply.Text.Length == 0) return new Result("", read, refused, [], Seconds(), tokens, "no answer");
				talk.Turns.Add(new Turn(question, reply.Text, read));
				Keep(talk);
				return new Result(reply.Text, read, refused, Unread(reply.Text, seen), Seconds(), tokens, null);
			}
			messages.Add(reply.Message!);
			foreach (var call in reply.Calls)
				messages.Add(await Read(call, ++calls > MaxCalls, run, read, refused, seen, reading));
		}
		return new Result("", read, refused, [], Seconds(), tokens, "no answer");
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
