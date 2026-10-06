namespace FmCli;

/// <summary>
/// The interactive prompt. Inside a session the commands are the same words without the `fm`
/// prefix, which is the whole point of having one.
/// </summary>
internal static class Session
{
	public static async Task<int> Run(Func<string[], Task<int>> execute)
	{
		Console.WriteLine("fm oturumu — komutlar: squad, inbox, next, continue, status, help · çıkış: quit");

		// Opening on where things stand saves typing the first command of every session.
		if (Ui.Rich)
		{
			Console.WriteLine();
			await execute(["status"]);
		}

		while (true)
		{
			Console.Write(Prompt());

			var line = Console.ReadLine();
			if (line == null) break; // ctrl-D

			var words = Split(line);
			if (words.Length == 0) continue;

			if (words[0] is "quit" or "exit" or "q") break;

			if (words[0] is "clear" or "cls")
			{
				Console.Write("\u001b[2J\u001b[H");
				continue;
			}

			// `fm squad` typed inside the session is still what someone means.
			if (words[0] == "fm") words = words.Skip(1).ToArray();
			if (words.Length == 0) continue;

			await execute(words);
		}

		return 0;
	}

	/// <summary>
	/// Splits a line into words, keeping a quoted phrase whole: a button's label is often two
	/// words, and there is no shell here to do the quoting.
	/// </summary>
	private static string[] Split(string line)
	{
		var words = new List<string>();
		var word = new System.Text.StringBuilder();
		var quoted = false;
		var open = false;

		foreach (var c in line)
		{
			if (c == '"') { quoted = !quoted; open = true; continue; }

			if (c == ' ' && !quoted)
			{
				if (word.Length > 0 || open) words.Add(word.ToString());
				word.Clear();
				open = false;
				continue;
			}

			word.Append(c);
		}

		if (word.Length > 0 || open) words.Add(word.ToString());
		return words.ToArray();
	}

	private static string Prompt() => Console.IsOutputRedirected ? "" : "\u001b[2mfm›\u001b[0m ";
}
