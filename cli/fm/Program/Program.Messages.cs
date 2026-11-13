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
		// A card fills its parts a moment after the message opens. Taken once the game's own flags say nothing is still
		// being bound (each text's binder, each section's dependencies: plugin /attachment?state=1) and two readings
		// agree: the flags clear when its texts are bound, and a list row can still come a frame after (13 Nov, 4 cards).
		await Until(async () =>
		{
			var answer = await Fm.Get("attachment");
			if (answer.StartsWith("error", StringComparison.Ordinal)) return false;
			var state = await Fm.GetJson("attachment", ("state", "1"));
			var building = state.ValueKind == JsonValueKind.Object
				&& (JsonRead.RawText(state, "Waiting") != "0" || JsonRead.RawText(state, "NotReady") != "0" || JsonRead.RawText(state, "Unresolved") != "0");
			var now = answer.Trim();
			var settled = !building && now == previous;
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
		// A table's rows are records of one kind: the first row says whether they stand for competitions, so a table of
		// thirty players is asked once, not thirty times (each a pull of a path that is not there).
		var notCompetitions = new HashSet<string>();
		for (var i = 0; i < shown.Count; i++)
		{
			if (Field(shown[i].Block, "Kind") != "row" || !shown[i].Block.TryGetProperty("Nodes", out var nodes)) continue;
			var ofTable = Field(shown[i].Block, "Table");
			if (notCompetitions.Contains(ofTable)) continue;
			var found = false;
			foreach (var node in nodes.EnumerateArray().Select(n => n.GetString() ?? "").Where(n => n.Length > 0))
			{
				// One asking: a competition's name is bound as soon as its row is (Fm.Pull would ask again and again).
				if (Field(await Fm.GetJson("pull", ("path", $"{node}.comp"), ("props", "Name")), "Name").Length == 0) continue;
				found = true;
				var words = "";
				await Until(async () =>
				{
					words = Field(await Fm.Pull($"{node}.comp", "TeamsBoardExpectationsInCompString", shown: "TeamsBoardExpectationsInCompString"), "TeamsBoardExpectationsInCompString");
					return words.Length > 0;
				}, seconds: 3, every: 0.2);
				if (words.Length > 0) expected[i] = words;
				break;
			}
			if (!found) notCompetitions.Add(ofTable);
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
