namespace FmCli;

/// <summary>
/// In the session, a number alone picks a row of the list shown last: the number in its #
/// column, the row's place for `inbox`, `players` and `search`, the shirt number for `squad`.
/// Each row is kept as the command it stands for, held by the game's own ids (a message's number
/// as `inbox` keeps it, a player's UniqueId), never by its place on a page read again. Any other
/// command forgets the list.
/// </summary>
internal static partial class Program
{
	private sealed record PickList(string From, Dictionary<int, (string Label, string[] Command)> Rows);

	private static PickList? lastList;

	/// <summary>The rows a list command just showed, numbered from 1 in the order shown.</summary>
	private static void ShowedList(string from, List<(string Label, string[] Command)> rows) =>
		ShowedList(from, rows.Select((r, i) => (i + 1, r)).ToDictionary(x => x.Item1, x => x.r));

	/// <summary>The rows a list command just showed, each under the number it showed for it.</summary>
	private static void ShowedList(string from, Dictionary<int, (string Label, string[] Command)> rows) =>
		lastList = rows.Count > 0 ? new PickList(from, rows) : null;

	internal static void ForgetList() => lastList = null;

	/// <summary>The command a number alone stands for, having said which list it came from; null having said why not.</summary>
	internal static string[]? Picked(int number)
	{
		if (lastList is not { } list)
		{
			Console.Error.WriteLine(L($"No list to pick {number} from: inbox, squad, players or search first."));
			return null;
		}
		if (!list.Rows.TryGetValue(number, out var row))
		{
			Console.Error.WriteLine(L($"No {number} in the {list.From} list."));
			return null;
		}
		var (label, command) = row;
		if (command.Length == 0)
		{
			Console.Error.WriteLine(L($"{label}: nothing to open from here."));
			return null;
		}
		if (Ui.Rich) Ui.Line($"[dim]{Ui.Safe(L($"{list.From} {number}: {label}"))}[/]");
		return command;
	}
}
