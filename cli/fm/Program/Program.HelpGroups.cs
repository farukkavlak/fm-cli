namespace FmCli;

/// <summary>
/// `help` in groups: alone, one line a group and the commands used most; with a group's word,
/// that group's commands with their options; `help --all` the whole text. Every command is in
/// one group (a test holds that).
/// </summary>
internal static partial class Program
{
	/// <summary>The groups in the order shown: the word typed, the Turkish word, what it is about, its commands.</summary>
	internal static readonly (string Name, string Turkish, string About, string[] Commands)[] HelpGroups =
	[
		("start", "baslat", "Start, save and move on",
			["status", "todo", "start", "stop", "continue", "next", "go", "holiday", "inbox", "reply", "talk", "dialog", "news", "doctor", "diff",
			 "saves", "save", "load", "world", "tour", "help"]),
		("squad", "kadro", "The squad and its players",
			["squad", "player", "compare", "health", "depth", "happiness", "dynamics", "meeting", "captains", "numbers", "register",
			 "progress", "query", "internationals", "mentoring"]),
		("match", "mac", "Tactics, match day, competitions",
			["match", "tactic", "setpieces", "penalties", "takers", "opponent", "opposition", "shout", "tweak", "sub", "dugout",
			 "pause", "resume", "skip", "report", "fixtures", "calendar", "table", "stages", "league", "friendly", "draw"]),
		("transfers", "transfer", "Transfers, scouting and contracts",
			["targets", "players", "search", "shortlist", "scout", "offer", "offers", "propose", "release", "contract", "contracts",
			 "interest", "enquire", "trial", "transferroom", "planner", "budget"]),
		("training", "antrenman", "Training",
			["training", "schedule", "prep", "intensity", "coaches"]),
		("club", "kulup", "The club, staff and board",
			["club", "staff", "advice", "board", "vision", "finances", "supporters", "facilities", "youth", "responsibilities",
			 "manager", "jobs", "resign", "read"]),
		("settings", "ayarlar", "Settings, language and output",
			["settings", "lang", "learned", "ai", "export", "completion"]),
		("tools", "araclar", "The game's screen, by hand",
			["pages", "open", "buttons", "press", "dropdowns", "select", "type", "actions", "crawl"]),
	];

	private static readonly string[] UsedMost = ["status", "next", "go", "squad", "inbox", "match"];

	private static (string Name, string Turkish, string About, string[] Commands)? HelpGroup(string word)
	{
		var typed = Nl.Text.Fold(word);
		foreach (var group in HelpGroups)
			if (typed == group.Name || typed == group.Turkish) return group;
		return null;
	}

	/// <summary>One line a group, then the commands used most and where to look further.</summary>
	private static int HelpShort()
	{
		Console.WriteLine(L("fm — run Football Manager from the terminal"));
		Console.WriteLine();
		foreach (var group in HelpGroups)
		{
			// As many whole command names as fit, then how many more.
			var line = $"  {group.Name,-11}{L(group.About)}: ";
			var shown = 0;
			foreach (var command in group.Commands)
			{
				var more = group.Commands.Length - shown - 1;
				var next = line + (shown > 0 ? ", " : "") + command;
				if (next.Length + (more > 0 ? 5 : 0) > 79) break;
				line = next;
				shown++;
			}
			if (shown < group.Commands.Length) line += $" +{group.Commands.Length - shown}";
			Console.WriteLine(line);
		}
		Console.WriteLine();
		Console.WriteLine(L($"Used most: {string.Join(" · ", UsedMost)}"));
		Ui.Hint(L("A group: help <group>   ·   a command: help <command> (or squad ?)   ·   everything: help --all"));
		return 0;
	}

	/// <summary>A group's commands, each with its help lines, in the group's order.</summary>
	private static int HelpGroupLines((string Name, string Turkish, string About, string[] Commands) group)
	{
		Console.WriteLine(L(group.About));
		var shown = new HashSet<string>();
		foreach (var command in group.Commands)
			foreach (var line in HelpLinesOf(command))
				if (shown.Add(line)) Console.WriteLine(line);
		return 0;
	}
}
