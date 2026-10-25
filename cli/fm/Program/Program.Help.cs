using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static string Field(JsonElement element, string name) =>
		JsonRead.Text(element, name);

	private static int Help()
	{
		Console.WriteLine(HelpText);
		return 0;
	}

	/// <summary>The lines of the help that are about one command: its own, and those indented under them.</summary>
	private static int HelpFor(string command)
	{
		var own = false;
		var any = false;
		foreach (var line in HelpText.Split('\n'))
		{
			var indent = line.Length - line.TrimStart().Length;
			if (indent <= 2) own = indent == 2 && (line.TrimStart().Split(' ', '\t')[0] == command || line.TrimStart().StartsWith("fm " + command + " ", StringComparison.Ordinal));
			if (!own || line.Trim().Length == 0) continue;
			Console.WriteLine(line);
			any = true;
		}
		if (!any) Console.WriteLine(L($"{command}: no help line; for all of them: help"));
		return 0;
	}

	/// <summary>Also where tab completion takes each command's description from; in the game's language.</summary>
	private static string HelpText => L(HelpEnglish);


	private static int Unknown(string command)
	{
		Console.Error.WriteLine(L($"unknown command: {command} — for the commands: `help`"));
		return 64; // EX_USAGE
	}
}
