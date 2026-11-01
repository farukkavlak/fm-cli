namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// What a message carries beside its body (the Code of Conduct it asks to confirm, say). An answer such as
	/// "Confirm" agrees to it, so it is shown with the message.
	/// </summary>
	private static Task WriteAttachment(HashSet<string> answers) => AttachmentRows(answers);

	/// <summary>
	/// An attachment as FM27 lays it out: what follows the message's body (newsitem.NewsBodyString) in
	/// the open message (MessagesToolBlockMessageContent). A table in it is printed row by row from
	/// the table's own rows (each row's texts as the game shows them); anything else as its lines.
	/// No heading or label is read.
	/// </summary>
	private static async Task AttachmentRows(HashSet<string> answers)
	{
		List<(string Text, string Path)> page = [];
		var previous = "";
		await Until(async () =>
		{
			page = (await Fm.GetJson("texts", ("within", "MessagesToolBlockMessageContent"), ("paths", "1"))).EnumerateArray()
				.Select(t => (Field(t, "Text"), Field(t, "Path"))).ToList();
			var now = string.Join("|", page.Select(t => t.Item1));
			var settled = now == previous;
			previous = now;
			return settled;
		}, seconds: 4, every: 0.3);
		var body = page.FindIndex(t => t.Path == "newsitem.NewsBodyString");
		if (body < 0) return;
		var after = page.Skip(body + 1).Where(t => t.Text.Trim().Length > 0 && !answers.Contains(t.Text)).ToList();
		if (after.Count == 0) return;

		var shown = after.Select(t => t.Text).ToHashSet();
		var rows = (await Fm.GetJson("rowtexts", ("has", "primarytext"), ("gen", Environment.TickCount64.ToString()))).EnumerateArray()
			.Select(r => r.GetProperty("Texts").EnumerateArray().Select(x => Field(x, "Text")).Where(x => x.Length > 0).ToList())
			.Where(r => r.Count > 0 && r.All(shown.Contains)).ToList();
		Console.WriteLine(L("\n  Attached:"));
		if (rows.Count > 0)
		{
			foreach (var row in rows) Console.WriteLine($"    {string.Join(" · ", row)}");
			return;
		}
		foreach (var line in after.Take(40)) Console.WriteLine($"    {line.Text.Replace("\n", "\n    ")}");
	}
}
