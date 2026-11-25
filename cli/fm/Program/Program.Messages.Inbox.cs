using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<string> MessageBody(string title, string opening, string previous = "")
	{
		// The title can change a frame before the body does, so the body of the message opened
		// before must not be taken for this one's (seen on a fitness-test message that showed the
		// tactical advice above it). Anything but the previous body, or the message's own opening
		// paragraph, is this message's.
		var page = await OpenMessage(title,
			t => t.Path == "newsitem.NewsBodyString" && (t.Text != previous || t.Text == opening || previous.Length == 0),
			seconds: 3);
		// The plugin puts a body the game's table knows whole into English; a line-by-line lookup
		// misses it, since it splits at the commas inside (seen on the club vision message).
		return GameWords.Of(page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text is { Length: > 0 } body ? body : opening);
	}

	/// <summary>
	/// Every text of the open message, each with its data path: read inside the message's own
	/// named block, which holds its sender, title, body and answers (FM27 put the title further from
	/// the body than a fixed climb from it reached).
	/// </summary>
	internal static Task<JsonElement> MessageTexts() =>
		Fm.GetJson("texts", ("within", "MessagesToolBlockMessageContent"), ("paths", "1"));

	/// <summary>
	/// The open message as the game draws it, from its sender and title down through its attachment,
	/// each text with its data path, known for this message by its title, since the one opened
	/// before can still be on screen. Waits a while for what <paramref name="wanted"/> asks for;
	/// empty when the message is not there.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> OpenMessage(string title, Func<(string Text, string Path), bool> wanted, double seconds)
	{
		List<(string Text, string Path)> page = [];
		bool Ours() => page.Any(t => t.Path == "newsitem.NewsTitleString" && (t.Text == title || GameWords.Of(t.Text) == GameWords.Of(title)));
		try
		{
			await Until(async () =>
			{
				var found = await MessageTexts();
				if (found!.ValueKind != JsonValueKind.Array) return false;
				page = found.EnumerateArray().Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
				return Ours() && page.Any(wanted);
			}, seconds: seconds, every: 0.3);
		}
		catch (FmError) { return []; }
		return Ours() ? page : [];
	}

	/// <summary>
	/// Where the numbers last shown by `inbox` are kept. The inbox drops read messages and takes
	/// new ones between commands, so "reply 17" means the seventeenth as it was shown, not as
	/// it is now.
	/// </summary>
	private static string NumbersFile => CacheFile("inbox-numbers.json");

	private static void RememberNumbers(JsonElement inbox)
	{
		try
		{
			var shown = inbox.EnumerateArray().Select(m => new[] { Field(m, "UniqueId"), Field(m, "NewsTitleString") }).ToList();
			File.WriteAllText(NumbersFile, JsonSerializer.Serialize(shown));
		}
		catch { }
	}

	/// <summary>The message's id and subject the number stood for when `inbox` last listed it; null when not kept.</summary>
	private static (string Id, string Title)? Remembered(int number)
	{
		string[][]? shown = null;
		try
		{
			if (File.Exists(NumbersFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(NumbersFile) < TimeSpan.FromHours(12))
				shown = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(NumbersFile));
		}
		catch { }
		return shown != null && number >= 1 && number <= shown.Length ? (shown[number - 1][0], shown[number - 1][1]) : null;
	}

	/// <summary>
	/// Whether the message a number stood for is outside these rows: one `inbox --all` listed, which the
	/// list no longer draws, is looked for in the whole inbox.
	/// </summary>
	private static bool NumberedElsewhere(List<JsonElement> inbox, string wanted) =>
		int.TryParse(wanted, out var number) && Remembered(number) is { } kept && !inbox.Any(m => Field(m, "UniqueId") == kept.Id);

	/// <summary>
	/// The message a number or a piece of its subject names. <paramref name="many"/> holds the messages of that subject
	/// when several match and the game marks none of them alone as awaiting an answer; they are said only when not
	/// <paramref name="quiet"/>, so a caller that looks in more lists says them once (<see cref="SayMany"/>).
	/// </summary>
	private static JsonElement? Pick(List<JsonElement> inbox, string wanted, bool quiet, out List<JsonElement> many)
	{
		many = [];
		if (int.TryParse(wanted, out var number))
		{
			// The number as `inbox` showed it, found again by the message's id.
			if (Remembered(number) is { } kept)
			{
				var (id, title) = kept;
				if (inbox.FirstOrDefault(m => Field(m, "UniqueId") == id) is { ValueKind: JsonValueKind.Object } same) return same;
				Console.Error.WriteLine(L($"Message {number} ('{title}') is no longer in the inbox. For the current numbers: inbox"));
				return null;
			}

			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine(L($"There is no message {number}; the inbox has {inbox.Count} messages."));
			return null;
		}

		// A message named by the game's own id (a stop's answer): that one, or none.
		if (wanted.StartsWith("id:", StringComparison.Ordinal))
		{
			var id = wanted[3..];
			var byId = inbox.Where(m => Field(m, "UniqueId") == id).Take(1).ToList();
			if (byId.Count == 1) return byId[0];
			if (!quiet) Console.Error.WriteLine(L("That message is no longer in the inbox. For the current messages: inbox"));
			return null;
		}

		var matches = inbox
			// The subject as the game shows it, or the English the plugin gives for it.
			.Where(m => Field(m, "NewsTitleString") is var subject && (subject.Contains(wanted, StringComparison.OrdinalIgnoreCase) || GameWords.Of(subject).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (matches.Count == 1) return matches[0];
		// The same subject twice (a second pre-season camp, 14 Jul 2031): the one still awaiting an answer, by the
		// game's own marks, is the one meant.
		if (matches.Count > 1 && matches.Where(Awaits).ToList() is { Count: 1 } open)
			return open[0];
		many = matches;
		if (quiet) return null;

		if (matches.Count == 0) Console.Error.WriteLine(L($"No message has '{wanted}' in its subject."));
		else SayMany(inbox, matches, wanted);
		return null;
	}

	/// <summary>A message the game marks as awaiting an answer: it can be answered and has not been.</summary>
	private static bool Awaits(JsonElement message) =>
		Field(message, "CanRespond") == "True" && Field(message, "NewsHasResponded") != "True";

	/// <summary>
	/// Several messages of one subject, none of them alone awaiting an answer: each said with a number `reply` takes.
	/// The numbers are those of the list they were found in, kept as `inbox` keeps its own (each held by the
	/// message's id), so `reply <n>` finds the same message (10 journalist questions of one subject, 12 May 2029).
	/// </summary>
	private static void SayMany(List<JsonElement> list, List<JsonElement> many, string wanted)
	{
		RememberNumbers(JsonSerializer.SerializeToElement(list));
		var awaiting = many.Count(Awaits);
		Console.Error.WriteLine(awaiting == 0
			? L($"'{wanted}' matches {many.Count} messages, none of them awaiting an answer; choose by number:")
			: L($"'{wanted}' matches {many.Count} messages; choose by number:"));
		foreach (var message in many)
		{
			var at = list.FindIndex(m => Field(m, "UniqueId") == Field(message, "UniqueId")) + 1;
			Console.Error.WriteLine($"  {at,3}  {GameWords.Of(Field(message, "NewsTitleString"))}" + (Awaits(message) ? "  · " + L("can be answered") : ""));
		}
	}
}
