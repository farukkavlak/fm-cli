using Spectre.Console;

namespace FmCli;

/// <summary>
/// Drawing for a terminal. Whatever is drawn here is only drawn when someone is looking: a
/// pipe, a script or the MCP server gets the plain tables, which are what a program wants.
/// </summary>
internal static class Ui
{
	public static bool Rich => Format == null && !Console.IsOutputRedirected;

	/// <summary>One line that shows how a long wait goes, written over itself; only on a terminal. Null clears it.</summary>
	public static void Progress(string? text)
	{
		if (!Rich) return;
		Console.Write($"\r\u001b[2K{(text == null ? "" : $"\u001b[2m{text}\u001b[0m")}");
	}

	/// <summary>
	/// "json", "csv" or "md" when the command was asked for one: nothing is drawn and nothing
	/// is asked, and the tables come out in that form.
	/// </summary>
	public static string? Format { get; set; }

	public static bool Json => Format == "json";

	/// <summary>
	/// A console bound to whatever Console.Out is now, since the MCP server swaps it per call.
	/// </summary>
	public static IAnsiConsole Out
	{
		get
		{
			var console = AnsiConsole.Create(new AnsiConsoleSettings
			{
				Out = new AnsiConsoleOutput(Console.Out),
				Ansi = Rich ? AnsiSupport.Detect : AnsiSupport.No,
				ColorSystem = Rich ? ColorSystemSupport.Detect : ColorSystemSupport.NoColors,
				Interactive = Rich && !Console.IsInputRedirected ? InteractionSupport.Yes : InteractionSupport.No,
			});
			if (!Rich) console.Profile.Width = 120;
			return console;
		}
	}

	/// <summary>Someone is at the keyboard to answer a question.</summary>
	public static bool Interactive => Rich && !Console.IsInputRedirected;

	/// <summary>
	/// Asks for one of a list with the arrow keys. The last entry always lets the question go
	/// unanswered, which comes back as null; so does a terminal with no one to ask. The list wraps,
	/// so that entry is one key up from the first, not thirty down (a press question naming the squad).
	/// </summary>
	/// <summary>How many questions have been put at the terminal: an answer that took one is not kept to be shown again.</summary>
	public static int Asked { get; private set; }

	public static int? Ask(string title, IReadOnlyList<string> choices, int current = -1, string? cancel = null)
	{
		if (!Interactive || choices.Count == 0) return null;
		Asked++;
		cancel ??= L("Cancel");

		var prompt = new SelectionPrompt<int>()
			.Title(Safe(title))
			.PageSize(Math.Clamp(choices.Count + 1, 3, 20))
			.HighlightStyle(new Style(Color.Yellow, decoration: Decoration.Bold))
			.WrapAround()
			.MoreChoicesText(L("[dim](more below)[/]"))
			.UseConverter(i => cancel != null && i < 0
				? $"[dim]{Safe(L(cancel))}[/]"
				: Safe(choices[i]) + (i == current ? L(" [dim]← now[/]") : ""))
			.AddChoices(Enumerable.Range(0, choices.Count));
		if (cancel != null) prompt.AddChoices(-1);

		// Starting on the current choice saves the walk down to it.
		if (current >= 0) prompt.DefaultValue(current);

		var picked = Out.Prompt(prompt);
		return picked < 0 ? null : picked;
	}

	/// <summary>Asks for a line of text; empty or no one to ask comes back as null.</summary>
	public static string Text(string question)
	{
		if (!Interactive) return null;
		Asked++;
		var answer = Out.Prompt(new TextPrompt<string>(Safe(question)).AllowEmpty());
		return string.IsNullOrWhiteSpace(answer) ? null : answer.Trim();
	}

	/// <summary>One line of markup, for files that keep Spectre's names out of their way.</summary>
	public static void Line(string markup) => Out.MarkupLine(markup);

	/// <summary>Text from the game, made safe to put inside markup.</summary>
	public static string Safe(string text) => Markup.Escape(GameWords.Of(text) ?? "");

	/// <summary>
	/// One bar for a value on a scale from low to high, eighths of a line tall: a row of them
	/// is a small chart of how something has gone.
	/// </summary>
	public static char Bar(double value, double low, double high) =>
		"▁▂▃▄▅▆▇█"[(int)Math.Round(Math.Clamp((value - low) / (high - low), 0, 1) * 7)];

	/// <summary>Cuts a name to a width, marking the cut.</summary>
	public static string Fit(string text, int width) =>
		text.Length <= width ? text : text[..(width - 1)] + "…";

	/// <summary>
	/// A hint line ("to apply: advice 1 apply   ·   all seen: …") written so it fits the terminal:
	/// a line too wide breaks where its parts meet (the ·), the parts after the first indented;
	/// a part still too wide breaks between words. A pipe, a script or MCP gets the line whole.
	/// </summary>
	public static void Hint(string text, TextWriter? to = null)
	{
		to ??= Console.Out;
		// The MCP server's output is always a pipe, whichever writer it swapped in.
		var terminal = !Console.IsOutputRedirected && (to != Console.Error || !Console.IsErrorRedirected);
		if (!terminal || Format != null)
		{
			to.WriteLine(text);
			return;
		}
		int width;
		try { width = Console.WindowWidth >= 40 ? Console.WindowWidth : 80; }
		catch (IOException) { width = 80; }
		foreach (var line in text.Split('\n'))
			foreach (var row in HintRows(line, width - 1)) to.WriteLine(row);
	}

	internal static IEnumerable<string> HintRows(string line, int width)
	{
		if (line.Length <= width)
		{
			yield return line;
			yield break;
		}
		const string indent = "  ";
		var separator = line.Contains("   ·   ") ? "   ·   " : " · ";
		var parts = line.Split(separator).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
		var lead = line[..(line.Length - line.TrimStart().Length)];
		var row = "";
		var first = true;
		foreach (var part in parts)
		{
			var start = first ? lead : indent;
			var joined = row.Length == 0 ? start + part : $"{row} · {part}";
			if (joined.Length <= width)
			{
				row = joined;
				continue;
			}
			if (row.Length > 0)
			{
				yield return row;
				first = false;
				start = indent;
			}
			// A part on its own that is still too wide breaks between words.
			row = start;
			foreach (var word in part.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			{
				var next = row.Trim().Length == 0 ? row + word : $"{row} {word}";
				if (next.Length > width && row.Trim().Length > 0)
				{
					yield return row;
					first = false;
					row = indent + word;
				}
				else row = next;
			}
		}
		if (row.Length > 0) yield return row;
	}
}
