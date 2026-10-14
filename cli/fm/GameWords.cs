namespace FmCli;

/// <summary>
/// The game's own words for the English texts the plugin answers with. In a game set to another
/// language the plugin puts what it reads into English, so commands can find buttons and rows by
/// one set of words; what is shown to the user is then put back into the words the game showed
/// (Faruk, 14 Ekim 2026: a Turkish game is read in Turkish). Only texts the game has answered with
/// are known, so a word the CLI writes itself is never changed; in an English game nothing is.
/// </summary>
internal static class GameWords
{
	private static readonly Dictionary<string, string> words = new(StringComparer.Ordinal);

	/// <summary>Takes in the pairs an answer came with: base64 of "english\tshown" lines.</summary>
	public static void Learn(string? header)
	{
		if (string.IsNullOrEmpty(header)) return;
		string text;
		try { text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(header)); }
		catch (FormatException) { return; }
		foreach (var line in text.Split('\n'))
		{
			var tab = line.IndexOf('\t');
			if (tab > 0) words[line[..tab]] = line[(tab + 1)..];
		}
	}

	/// <summary>A text from the game in the game's own words, where it was put into English.</summary>
	public static string Of(string text)
	{
		if (string.IsNullOrEmpty(text) || words.Count == 0) return text;
		if (words.TryGetValue(text, out var shown)) return shown;
		var trimmed = text.Trim();
		if (trimmed.Length != text.Length && words.TryGetValue(trimmed, out shown)) return text.Replace(trimmed, shown);
		return InLine(text);
	}

	// What the CLI puts between the game's texts on a line: columns (a tab, or spaces to line them
	// up), lists ("York · Preparation", "Proposal, İzleniyor"), a label before a value ("Mentalite:
	// Balanced") and a note after one ("8-0 (Preparation)").
	private static readonly System.Text.RegularExpressions.Regex Between = new(@"(\t| {2,}| · |, |: | ?\(|\)|\r?\n)");

	/// <summary>
	/// A line the CLI wrote with the game's texts in it, each part looked up on its own. A column
	/// padded with spaces keeps its width, so the columns after it stay where they were.
	/// </summary>
	public static string InLine(string line)
	{
		if (string.IsNullOrEmpty(line) || words.Count == 0) return line;
		var parts = Between.Split(line);
		var changed = false;
		var carry = 0; // how much longer the last part became than the text it stands for
		for (var i = 0; i < parts.Length; i++)
		{
			if (i % 2 == 1)
			{
				// Spaces after a part that changed length take up the difference, leaving at least two.
				if (carry != 0 && parts[i].Length >= 2 && parts[i].Trim().Length == 0)
					parts[i] = new string(' ', Math.Max(2, parts[i].Length - carry));
				carry = 0;
				continue;
			}
			var part = parts[i];
			var core = part.Trim();
			// A single letter inside a line is a code ("D (M)"), not one of the game's words.
			if (core.Length < 2 || !words.TryGetValue(core, out var shown)) continue;
			parts[i] = part.Replace(core, shown);
			carry = shown.Length - core.Length;
			changed = true;
		}
		return changed ? string.Concat(parts) : line;
	}
}

/// <summary>
/// Puts what a command prints into the game's own words on its way out (<see cref="GameWords"/>): most
/// commands write the game's texts straight into their lines, not through a table.
/// </summary>
internal sealed class GameWordsLines(TextWriter inner) : TextWriter
{
	public override System.Text.Encoding Encoding => inner.Encoding;
	public override void Write(char value) => inner.Write(value);
	public override void Write(string? value) => inner.Write(GameWords.InLine(value ?? ""));
	public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));
	public override void WriteLine(string? value) => inner.WriteLine(GameWords.InLine(value ?? ""));
	public override void Flush() => inner.Flush();
}
