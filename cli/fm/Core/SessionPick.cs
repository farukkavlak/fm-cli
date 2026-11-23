namespace FmCli;

/// <summary>
/// In the session, a number alone picks a row of the list shown last (`inbox`, `players`,
/// `search`): each row is kept as the command it stands for, held by the game's own ids (a
/// message's number as `inbox` keeps it, a player's UniqueId), never by its place on a page
/// read again. Any other command forgets the list.
/// </summary>
internal static partial class Program
{
	private sealed record PickList(string From, List<(string Label, string[] Command)> Rows);

	private static PickList? lastList;

	/// <summary>The rows a list command just showed, numbered from 1 in the order shown.</summary>
	private static void ShowedList(string from, List<(string Label, string[] Command)> rows) =>
		lastList = rows.Count > 0 ? new PickList(from, rows) : null;

	internal static void ForgetList() => lastList = null;

	/// <summary>The command a number alone stands for, having said which list it came from; null having said why not.</summary>
	internal static string[]? Picked(int number)
	{
		if (lastList is not { } list)
		{
			Console.Error.WriteLine(L($"No list to pick {number} from: inbox, players or search first."));
			return null;
		}
		if (number < 1 || number > list.Rows.Count)
		{
			Console.Error.WriteLine(L($"The {list.From} list has {list.Rows.Count} rows."));
			return null;
		}
		var (label, command) = list.Rows[number - 1];
		if (command.Length == 0)
		{
			Console.Error.WriteLine(L($"{label}: nothing to open from here."));
			return null;
		}
		if (Ui.Rich) Ui.Line($"[dim]{Ui.Safe(L($"{list.From} {number}: {label}"))}[/]");
		return command;
	}
}
