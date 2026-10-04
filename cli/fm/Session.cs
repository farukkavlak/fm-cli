namespace FmCli;

/// <summary>
/// The interactive prompt. Inside a session the commands are the same words without the `fm`
/// prefix, which is the whole point of having one.
/// </summary>
internal static class Session
{
	public static async Task<int> Run(Func<string[], Task<int>> execute)
	{
		Console.WriteLine("fm oturumu — komutlar: squad, inbox, next, status, help · çıkış: quit");

		while (true)
		{
			Console.Write(Prompt());

			var line = Console.ReadLine();
			if (line == null) break; // ctrl-D

			var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (words.Length == 0) continue;

			if (words[0] is "quit" or "exit" or "q") break;

			// `fm squad` typed inside the session is still what someone means.
			if (words[0] == "fm") words = words.Skip(1).ToArray();
			if (words.Length == 0) continue;

			await execute(words);
		}

		return 0;
	}

	private static string Prompt() => Console.IsOutputRedirected ? "" : "\u001b[2mfm›\u001b[0m ";
}
