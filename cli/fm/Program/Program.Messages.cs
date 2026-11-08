using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What a message carries beside its body (the Code of Conduct it asks to confirm, say). An answer such as
	/// "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static Task WriteAttachment(HashSet<string> answers) => AttachmentRows(answers);

	/// <summary>
	/// An attachment as FM27 lays it out under the open message's #MessageAttachments (plugin /attachment): a
	/// table's headings, its rows, and every other group of texts, in the game's own words and in the order
	/// drawn. A table lays out only the rows in view: how many more it holds is said. Nothing is chosen by its
	/// place or its words; the order is only how it is shown.
	/// </summary>
	private static async Task AttachmentRows(HashSet<string> answers)
	{
		JsonElement blocks = default;
		var previous = "";
		// Taken once two readings agree: a card fills its parts a moment after the message opens.
		await Until(async () =>
		{
			var answer = await Fm.Get("attachment");
			if (answer.StartsWith("error", StringComparison.Ordinal)) return false;
			var now = answer.Trim();
			var settled = now == previous;
			previous = now;
			if (settled) blocks = JsonDocument.Parse(now).RootElement;
			return settled;
		}, seconds: 4, every: 0.3);
		if (blocks.ValueKind != JsonValueKind.Array || blocks.GetArrayLength() == 0) return;

		static List<string> TextsOf(JsonElement block) =>
			block.GetProperty("Texts").EnumerateArray().Select(t => Field(t, "Text")).Where(t => t.Length > 0).ToList();
		var shown = blocks.EnumerateArray().Select(b => (Block: b, Texts: TextsOf(b)))
			.Where(b => b.Texts.Count > 0 && !(b.Texts.Count == 1 && answers.Contains(b.Texts[0]))).ToList();
		if (shown.Count == 0) return;

		// A row that stands for a competition (its record holds `comp`) shows only the name and a progress icon;
		// what the board expects of us there is that competition's own record, read in the game's words.
		var expected = new Dictionary<int, string>();
		for (var i = 0; i < shown.Count; i++)
		{
			if (Field(shown[i].Block, "Kind") != "row" || !shown[i].Block.TryGetProperty("Nodes", out var nodes)) continue;
			foreach (var node in nodes.EnumerateArray().Select(n => n.GetString() ?? "").Where(n => n.Length > 0))
			{
				if (Field(await Fm.Pull($"{node}.comp", "Name"), "Name").Length == 0) continue;
				var words = "";
				await Until(async () =>
				{
					words = Field(await Fm.Pull($"{node}.comp", "TeamsBoardExpectationsInCompString", shown: "TeamsBoardExpectationsInCompString"), "TeamsBoardExpectationsInCompString");
					return words.Length > 0;
				}, seconds: 3, every: 0.2);
				if (words.Length > 0) expected[i] = words;
				break;
			}
		}

		Console.WriteLine(L("\n  Attached:"));
		var rowsOf = new Dictionary<string, (int Shown, int Count)>();
		string? table = null;
		void More()
		{
			if (table != null && rowsOf.TryGetValue(table, out var t) && t.Count > t.Shown)
				Console.WriteLine(L($"    … {t.Count - t.Shown} more rows (the table lays out only those in view)"));
			table = null;
		}
		for (var i = 0; i < shown.Count; i++)
		{
			var (block, texts) = shown[i];
			if (expected.TryGetValue(i, out var words)) texts = [.. texts, words];
			var kind = Field(block, "Kind");
			if (kind == "row")
			{
				var at = Field(block, "Table");
				if (at != table) More();
				table = at;
				var count = int.TryParse(Field(block, "Count"), out var c) ? c : 0;
				rowsOf[at] = (rowsOf.GetValueOrDefault(at).Shown + 1, count);
			}
			else More();
			var line = string.Join(" · ", texts.Select(t => t.Replace("\n", "\n    ")));
			Console.WriteLine(kind == "head" ? $"    [{line}]" : $"    {line}");
		}
		More();
	}
}
