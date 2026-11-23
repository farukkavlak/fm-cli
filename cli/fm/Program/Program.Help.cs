using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	private static string Field(JsonElement element, string name) =>
		JsonRead.Text(element, name);

	/// <summary>`help`: the groups; `help <group>`: one group; `help <command>`: one command; `help --all`: everything.</summary>
	private static int Help(string[] args)
	{
		if (args.Length < 2) return Ui.Rich ? HelpShort() : HelpAll();
		if (args[1] is "--all" or "-a") return HelpAll();
		if (args[1].StartsWith('-')) return HelpShort();
		if (HelpGroup(args[1]) is { } group) return HelpGroupLines(group);
		return HelpFor(args[1]);
	}

	private static int HelpAll()
	{
		Console.WriteLine(HelpText);
		return 0;
	}

	/// <summary>The lines of the help that are about one command.</summary>
	private static int HelpFor(string command)
	{
		var lines = HelpLinesOf(command);
		foreach (var line in lines) Console.WriteLine(line);
		if (lines.Count == 0) Console.WriteLine(L($"{command}: no help line; for all of them: help"));
		return 0;
	}

	/// <summary>A command's own help lines and those indented under them.</summary>
	private static List<string> HelpLinesOf(string command)
	{
		var own = false;
		var lines = new List<string>();
		foreach (var line in HelpText.Split('\n'))
		{
			var indent = line.Length - line.TrimStart().Length;
			if (indent <= 2) own = indent == 2 && (line.TrimStart().Split(' ', '\t')[0] == command || line.TrimStart().StartsWith("fm " + command + " ", StringComparison.Ordinal));
			if (own && line.Trim().Length > 0) lines.Add(line);
		}
		return lines;
	}

	/// <summary>Also where tab completion takes each command's description from; in the game's language.</summary>
	private static string HelpText => string.Join('\n', HelpEnglish.Split('\n').Select(HelpLine));

	/// <summary>One help line in the game's language, its indent kept: the indent says which command it belongs to.</summary>
	private static string HelpLine(string line)
	{
		var text = line.TrimStart();
		return text.Length == 0 ? line : line[..(line.Length - text.Length)] + L(text);
	}


	private static int Unknown(string command)
	{
		Console.Error.WriteLine(L($"unknown command: {command} — for the commands: `help`"));
		return 64; // EX_USAGE
	}
}
