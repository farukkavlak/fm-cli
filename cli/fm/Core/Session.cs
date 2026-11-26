namespace FmCli;

/// <summary>
/// The interactive prompt. Inside a session the commands are the same words without the `fm`
/// prefix, which is the whole point of having one.
/// </summary>
internal static class Session
{
	public static async Task<int> Run(Func<string[], Task<int>> execute)
	{
		Console.WriteLine(L("fm session — commands: squad, inbox, next, continue, status, help · to leave: quit"));

		// Where `next` stops is shown here as a thing to answer, with numbered answers: the opening status too,
		// so a stop is told once, numbered, and not first as a plain list.
		Program.SessionShowsStops = true;
		Program.HeldStop? shownStop = null;
		// Opening on where things stand saves typing the first command of every session.
		if (Ui.Rich)
		{
			Console.WriteLine();
			Program.ForgetStop();
			await execute(["status"]);
			if (Program.StopNow is { } opening)
			{
				Program.ShowStop(opening);
				shownStop = opening;
			}
		}
		await Program.NewsBaseline();
		await Program.NoteSave(loaded: false);
		// The commands a line ran, a sentence's included: told against the stop's answers.
		var ran = new List<string[]>();
		async Task<int> Run(string[] w)
		{
			ran.Add(w);
			return await execute(w);
		}

		// Ctrl-C ends the command, not the session: at the prompt it starts a new line, and the game is left
		// to go on with what it was doing (a holiday ends only by the game's own way).
		var running = new CancellationTokenSource();
		var busy = false;
		Console.CancelKeyPress += (_, press) =>
		{
			press.Cancel = true;
			if (busy) running.Cancel();
			else Console.Write($"\n{Prompt()}");
		};
		async Task<int> Interruptible(Func<Task<int>> command)
		{
			running = new CancellationTokenSource();
			Bridge.Interruption = running.Token;
			busy = true;
			try { return await command(); }
			catch (Interrupted)
			{
				Console.WriteLine(L("\nStopped. What the game was doing goes on: status"));
				return 130;
			}
			finally
			{
				busy = false;
				Bridge.Interruption = default;
			}
		}

		FirstTime();
		var editor = new LineEditor(Program.Completions);
		// An answer the last line's sentence said for the stop its answer led to ("fenerin teklifini kabul et":
		// the offer chosen, then accepting it), run as the next line; and what that sentence said to do.
		string[]? queued = null;
		IReadOnlySet<string>? carried = null;
		var carriedSure = true;
		while (true)
		{
			string[] words;
			var answering = false;
			if (queued != null) (words, queued, answering) = (queued, null, true);
			else
			{
				Program.ReadAhead();
				var line = await editor.ReadLine(Prompt(), PromptWidth());
				if (line == null) break; // ctrl-D

				words = Split(line);
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

				// "?" is help: alone, the list of commands; after a command, that command's.
				if (words is ["?"]) words = ["help"];
				else if (words.Length > 1 && words[^1] == "?") words = [.. words[..^1], "--help"];

				// A number alone: a row of the list shown last. Anything else forgets that list.
				if (words is [var alone] && int.TryParse(alone, out var number) && number > 0)
				{
					answering = Program.ListIsStop;
					if (Program.Picked(number) is not { } picked) continue;
					words = picked;
				}
				else
				{
					Program.ForgetList();
					// A sentence at a stop, or a word that is no command: one of the stop's answers first, the router after.
					if (Program.StopNow is { Answers.Count: > 0 } at && (Program.IsSentence(words) || words is [var one] && !Program.IsCommandWord(one))
					    && Program.StopAnswer(at, string.Join(' ', words)) is { } said)
					{
						// Asked about and not taken: nothing runs, the stop waits.
						if (said.Command.Length == 0) continue;
						(words, answering) = (said.Command, true);
						carried = said.Done.Count > 0 ? said.Done : null;
						carriedSure = said.Sure;
					}
				}
			}

			// At a stop with somewhere to go, going on from it goes there.
			var standing = Program.StopNow;
			if (standing != null && Program.GoalNow is { } goal && words is ["next"] or ["next", "--skip"])
				words = [.. words, "--until", goal];

			Program.ForgetNames();
			ran.Clear();
			if (!Program.IsSentence(words)) Nl.Learned.Next(Program.QuietCommand(words[0]));
			var code = await Interruptible(() => Program.IsSentence(words) ? Program.Understand(string.Join(' ', words), Run) : Run(words));
			// A save loaded or the game closed is another starting point, not news.
			if (Program.QuietCommand(words[0]) is "load" or "continue" or "start" or "stop")
			{
				Program.ForgetStop();
				Program.GoalNow = null;
				shownStop = null;
				await Program.NewsBaseline();
				await Program.NoteSave(loaded: Program.QuietCommand(words[0]) is not "stop");
				continue;
			}
			if (!Console.IsOutputRedirected) await Program.SayNews(again: Program.QuietCommand(words[0]) is "reply" or "talk" or "dialog" or "offers" or "contract" or "meeting" or "press");

			// The stop answered: forgotten, and the way on taken again to where it was going (match day is
			// where it was going, so answering that one ends it).
			var wentOn = ran.Any(w => Program.QuietCommand(w[0]) == "next");
			if (standing != null && ReferenceEquals(Program.StopNow, standing) && code == 0
			    && (answering || ran.Any(w => Program.Answers(standing, w))))
			{
				Program.ForgetStop();
				shownStop = null;
				if (standing.Kind == "match") Program.GoalNow = null;
				else if (Program.GoalNow is { } on && !wentOn)
				{
					string[] onward = ["next", "--until", on];
					Console.WriteLine(Program.Dim($"→ {string.Join(' ', onward)}"));
					ran.Clear();
					await Interruptible(() => Run(onward));
					if (!Console.IsOutputRedirected) await Program.SayNews();
				}
				// Nowhere to go: what still waits is read, nothing pressed (the other offers come back as the stop).
				else if (Program.GoalNow == null && !wentOn)
				{
					var free = false;
					await Interruptible(async () => { free = await Program.LookAgain(); return 0; });
					if (free) Console.WriteLine(Program.Dim(L("The game can go on: next")));
				}
			}

			// A stop come to is shown once, with its answers; one still standing after a question keeps its
			// answers for a number alone, unless the question showed a list of its own.
			if (Program.StopNow is { } stop)
			{
				if (!ReferenceEquals(stop, shownStop))
				{
					Program.ShowStop(stop);
					shownStop = stop;
				}
				else if (!Program.HasList) Program.PickStop(stop);
				// What the sentence said to do, for the answers its answer led to: the one that does it, if one does.
				if (carried != null && !ReferenceEquals(stop, standing) && Program.DoneAnswer(stop, carried, carriedSure) is { } next) queued = next;
			}
			else shownStop = null;
			carried = null;

			// A save, or an autosave on the way: the prompt's reminder counts from it.
			await Program.NoteSave(loaded: false);
		}

		return 0;
	}

	private static string WelcomedFile =>
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "welcomed");

	/// <summary>The first session on a terminal says how the line works; once only.</summary>
	private static void FirstTime()
	{
		if (!LineEditor.Usable || File.Exists(WelcomedFile)) return;
		Console.WriteLine();
		Console.WriteLine(L("First time here, so in short:"));
		Console.WriteLine(L("  Commands go without fm: squad, inbox, next."));
		Console.WriteLine(L("  Tab completes commands, options and our players' names; twice lists them."));
		Console.WriteLine(L("  Up and down bring back what was typed; ctrl-R searches it."));
		Console.WriteLine(L("  ? lists the commands; squad ? explains one."));
		Console.WriteLine(L("  quit leaves the session; the game stays open."));
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(WelcomedFile)!);
			File.WriteAllText(WelcomedFile, "");
		}
		catch (IOException) { }
		catch (UnauthorizedAccessException) { }
	}

	/// <summary>
	/// Splits a line into words, keeping a quoted phrase whole: a button's label is often two
	/// words, and there is no shell here to do the quoting.
	/// </summary>
	internal static string[] Split(string line)
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

	/// <summary>The prompt: where things stand, then "fm›"; dim, so the typed line stands out.</summary>
	private static string Prompt() =>
		Console.IsOutputRedirected ? "" : $"\u001b[2m{PromptText()}\u001b[0m ";

	private static string PromptText() => Program.PromptStatus() is { Length: > 0 } status ? $"{status} fm›" : "fm›";

	// What the prompt takes on screen: its text and a space.
	private static int PromptWidth() => Console.IsOutputRedirected ? 0 : PromptText().Length + 1;
}
