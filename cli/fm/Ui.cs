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

	/// <summary>Text from the game, made safe to put inside markup.</summary>
	public static string Safe(string text) => Markup.Escape(text ?? "");

	/// <summary>Cuts a name to a width, marking the cut.</summary>
	public static string Fit(string text, int width) =>
		text.Length <= width ? text : text[..(width - 1)] + "…";
}
