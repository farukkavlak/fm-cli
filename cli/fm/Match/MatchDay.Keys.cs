namespace FmCli;

/// <summary>What the live panel's keys do beyond the panel itself: nothing typed has to wait for the match or a second terminal.</summary>
internal static partial class Program
{
	/// <summary>A command whose answer would only be drawn over: its first line goes on the panel's note instead.</summary>
	private static async Task Quietly(Task<int> command)
	{
		var (shown, said) = (Console.Out, Console.Error);
		var heard = new StringWriter();
		Console.SetOut(heard);
		Console.SetError(heard);
		try { await command; }
		finally
		{
			Console.SetOut(shown);
			Console.SetError(said);
		}
		if (heard.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() is { } line) MatchLive.Note(line.Trim());
	}

	/// <summary>Runs a command under the panel with play stopped if it was going, shows its answer until a key, and plays on.</summary>
	private static async Task LiveCommand(string[] words, bool stopPlay)
	{
		var paused = false;
		if (stopPlay && Field(await Fm.GetJson("state"), "Playing") == "True")
		{
			await Quietly(MatchPause("pause"));
			paused = Field(await Fm.GetJson("state"), "Playing") != "True";
		}
		Console.WriteLine();
		if (IsSentence(words)) await Understand(string.Join(' ', words), Run);
		else
		{
			Nl.Learned.Next(QuietCommand(words[0]));
			await Run(words);
		}
		Console.WriteLine(L("Any key: back to the match"));
		Console.ReadKey(true);
		if (paused) await Quietly(MatchPause("resume"));
		MatchLive.Redraw();
	}

	/// <summary>The session's prompt under the panel: play stops, any command runs there, play goes on.</summary>
	private static async Task LiveLine()
	{
		var paused = false;
		if (Field(await Fm.GetJson("state"), "Playing") == "True")
		{
			await Quietly(MatchPause("pause"));
			paused = Field(await Fm.GetJson("state"), "Playing") != "True";
		}
		Console.Write("\n\u001b[2mfm›\u001b[0m ");
		var words = Session.Split(Console.ReadLine() ?? "");
		if (words.Length > 0 && words[0] == "fm") words = words[1..];
		if (words.Length > 0 && words[0] is not ("match" or "quit" or "exit" or "q"))
		{
			await LiveCommand(words, stopPlay: false);
		}
		if (paused) await Quietly(MatchPause("resume"));
		MatchLive.Redraw();
	}
}
