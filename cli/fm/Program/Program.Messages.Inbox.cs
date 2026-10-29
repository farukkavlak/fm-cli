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
		return page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text is { Length: > 0 } body ? body : opening;
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

	/// <summary>A message by its number in the inbox listing, or by a piece of its subject.</summary>
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

	private static JsonElement? Pick(List<JsonElement> inbox, string wanted, bool quiet = false)
	{
		if (int.TryParse(wanted, out var number))
		{
			// The number as `inbox` showed it, found again by the message's id.
			string[][] shown = null;
			try
			{
				if (File.Exists(NumbersFile) && DateTime.UtcNow - File.GetLastWriteTimeUtc(NumbersFile) < TimeSpan.FromHours(12))
					shown = JsonSerializer.Deserialize<string[][]>(File.ReadAllText(NumbersFile));
			}
			catch { }
			if (shown != null && number >= 1 && number <= shown.Length)
			{
				var (id, title) = (shown[number - 1][0], shown[number - 1][1]);
				if (inbox.FirstOrDefault(m => Field(m, "UniqueId") == id) is { ValueKind: JsonValueKind.Object } same) return same;
				Console.Error.WriteLine(L($"Message {number} ('{title}') is no longer in the inbox. For the current numbers: inbox"));
				return null;
			}

			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine(L($"There is no message {number}; the inbox has {inbox.Count} messages."));
			return null;
		}

		var matches = inbox
			// The subject as the game shows it, or the English the plugin gives for it.
			.Where(m => Field(m, "NewsTitleString") is var subject && (subject.Contains(wanted, StringComparison.OrdinalIgnoreCase) || GameWords.Of(subject).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (matches.Count == 1) return matches[0];
		if (matches.Count == 0 && quiet) return null;

		Console.Error.WriteLine(matches.Count == 0
			? L($"No message has '{wanted}' in its subject.")
			: L($"'{wanted}' matches {matches.Count} messages; choose by number:"));
		foreach (var match in matches) Console.Error.WriteLine("  " + GameWords.Of(Field(match, "NewsTitleString")));
		return null;
	}
}
