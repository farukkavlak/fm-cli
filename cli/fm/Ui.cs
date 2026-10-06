using Spectre.Console;

namespace FmCli;

/// <summary>
/// Drawing for a terminal. Whatever is drawn here is only drawn when someone is looking: a
/// pipe, a script or the MCP server gets the plain tables, which are what a program wants.
/// </summary>
internal static class Ui
{
	public static bool Rich => !Console.IsOutputRedirected;

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
	/// unanswered, which comes back as null; so does a terminal with no one to ask.
	/// </summary>
	public static int? Ask(string title, IReadOnlyList<string> choices, int current = -1, string cancel = "Vazgeç")
	{
		if (!Interactive || choices.Count == 0) return null;

		var prompt = new SelectionPrompt<int>()
			.Title(Safe(title))
			.PageSize(Math.Clamp(choices.Count + 1, 3, 20))
			.HighlightStyle(new Style(Color.Yellow, decoration: Decoration.Bold))
			.MoreChoicesText("[dim](aşağıda daha fazlası)[/]")
			.UseConverter(i => i < 0 ? $"[dim]{Safe(cancel)}[/]" : Safe(choices[i]) + (i == current ? " [dim]← şu an[/]" : ""))
			.AddChoices(Enumerable.Range(0, choices.Count))
			.AddChoices(-1);

		// Starting on the current choice saves the walk down to it.
		if (current >= 0) prompt.DefaultValue(current);

		var picked = Out.Prompt(prompt);
		return picked < 0 ? null : picked;
	}

	/// <summary>Text from the game, made safe to put inside markup.</summary>
	public static string Safe(string text) => Markup.Escape(text ?? "");

	/// <summary>Cuts a name to a width, marking the cut.</summary>
	public static string Fit(string text, int width) =>
		text.Length <= width ? text : text[..(width - 1)] + "…";
}
