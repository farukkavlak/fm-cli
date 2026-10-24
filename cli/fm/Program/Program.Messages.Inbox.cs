using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static async Task<string> MessageBody(string title, string opening)
	{
		var page = await OpenMessage(title, t => t.Path == "newsitem.NewsBodyString", seconds: 3);
		return page.FirstOrDefault(t => t.Path == "newsitem.NewsBodyString").Text is { Length: > 0 } body ? body : opening;
	}

	/// <summary>
	/// The open message as the game draws it, from its sender and title down through its attachment,
	/// each text with its data path: found by the body's element name ("MessageBodyText") and known
	/// for this message by its title, since the one opened before can still be on screen. Waits a
	/// while for what <paramref name="wanted"/> asks for; empty when the message is not there.
	/// </summary>
	private static async Task<List<(string Text, string Path)>> OpenMessage(string title, Func<(string Text, string Path), bool> wanted, double seconds, int up = 5)
	{
		List<(string Text, string Path)> page = [];
		bool Ours() => page.Any(t => t.Path == "newsitem.NewsTitleString" && (t.Text == title || GameWords.Of(t.Text) == GameWords.Of(title)));
		try
		{
			await Until(async () =>
			{
				var found = await Fm.GetJson("texts", ("anchorname", "MessageBodyText"), ("up", up.ToString()), ("paths", "1"), ("most", "1"));
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
	/// it is now (PLAN, known bugs: once a reply opened the message above the one meant).
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

	private static JsonElement? Pick(List<JsonElement> inbox, string wanted)
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
				Console.Error.WriteLine(L($"{number} numaralı mesaj ('{title}') artık gelen kutusunda yok. Güncel numaralar için: inbox"));
				return null;
			}

			if (number >= 1 && number <= inbox.Count) return inbox[number - 1];

			Console.Error.WriteLine(L($"{number} numaralı mesaj yok; gelen kutusunda {inbox.Count} mesaj var."));
			return null;
		}

		var matches = inbox
			// The subject as the game shows it, or the English the plugin gives for it.
			.Where(m => Field(m, "NewsTitleString") is var subject && (subject.Contains(wanted, StringComparison.OrdinalIgnoreCase) || GameWords.Of(subject).Contains(wanted, StringComparison.OrdinalIgnoreCase)))
			.ToList();
		if (matches.Count == 1) return matches[0];

		Console.Error.WriteLine(matches.Count == 0
			? L($"Konusunda '{wanted}' geçen mesaj yok.")
			: L($"'{wanted}' {matches.Count} mesaja uyuyor; numarasıyla seç:"));
		foreach (var match in matches) Console.Error.WriteLine("  " + GameWords.Of(Field(match, "NewsTitleString")));
		return null;
	}
}
