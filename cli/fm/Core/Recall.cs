namespace FmCli;

/// <summary>
/// A read asked again while the game's clock has not moved is answered from the last time
/// (in a session and in the MCP server, which live on between commands). The answer is kept by
/// the game's date and hour (`state`), the save (our team's id), the language, the terminal's
/// width and the command with its words. Only reads are kept; any command not known to be
/// harmless forgets them all, since it may have changed the game (sell, train, tactic, reply…).
/// `--fresh` reads again. A read that asked something (a name with several players) is not kept.
/// </summary>
internal static partial class Program
{
	/// <summary>Reads whatever their words: every form of them only looks.</summary>
	private static readonly HashSet<string> KeptReads =
	[
		"squad", "player", "compare", "table", "fixtures", "league", "calendar", "health", "depth", "happiness", "dynamics",
		"finances", "club", "supporters", "facilities", "manager", "internationals", "progress", "opponent", "report", "world", "news",
	];

	/// <summary>Reads when given alone; with words they can change something (`board request`, `tactic role`, `staff fire`).</summary>
	private static readonly HashSet<string> KeptBare =
		["board", "staff", "youth", "tactic", "captains", "coaches", "intensity", "penalties", "takers", "contracts", "doctor", "stages"];

	/// <summary>Neither kept nor a change: the kept reads stay after them.</summary>
	private static readonly HashSet<string> Harmless = ["status", "help", "saves", "pages", "buttons", "dropdowns"];

	private sealed record Kept(string Out, string Err, int Code);

	private static readonly Dictionary<string, Kept> recalled = [];

	internal static void ForgetRecalled() => recalled.Clear();

	private static bool Keepable(string command, string[] args)
	{
		if (KeptReads.Contains(command)) return true;
		// Only formats and --fresh beside it.
		return KeptBare.Contains(command) && args.Skip(1).All(a => a is "--json" or "--csv" or "--md" or "--fresh");
	}

	private static async Task<int> Recalled(string command, string[] args, bool json)
	{
		var fresh = args.Contains("--fresh");
		if (fresh) args = [.. args.Where(a => a != "--fresh")];

		if (!Keepable(command, args))
		{
			if (!Harmless.Contains(command) && !(command == "inbox" && !args.Contains("--read-all"))) recalled.Clear();
			return await Dispatch(command, args, json);
		}

		var key = await RecallKey(args);
		if (key != null && !fresh && recalled.TryGetValue(key, out var kept))
		{
			Console.Out.Write(kept.Out);
			Console.Error.Write(kept.Err);
			if (Ui.Rich) Ui.Line($"[dim]{Ui.Safe(L("(as read a moment ago; the clock has not moved. Read again: --fresh)"))}[/]");
			return kept.Code;
		}

		var (output, errors) = (Console.Out, Console.Error);
		var (seen, said) = (new Copying(output), new Copying(errors));
		var asked = Ui.Asked;
		int code;
		Console.SetOut(seen);
		Console.SetError(said);
		try { code = await Dispatch(command, args, json); }
		finally
		{
			Console.SetOut(output);
			Console.SetError(errors);
		}
		if (key != null && code == 0 && Ui.Asked == asked) recalled[key] = new Kept(seen.Text, said.Text, code);
		return code;
	}

	/// <summary>Where the game stands and what was asked; null when the game cannot say.</summary>
	private static async Task<string?> RecallKey(string[] args)
	{
		try
		{
			var state = await Fm.GetJson("state");
			var date = Field(state, "Date");
			if (date.Length == 0 || Field(state, "DuringMatch") == "True") return null;
			var team = Field(await Fm.Pull("Human.Team", "UniqueId"), "UniqueId");
			int width;
			try { width = Console.WindowWidth; } catch (IOException) { width = 0; }
			return string.Join("\u001f", [date, Field(state, "Clock"), team, Labels.Language, Ui.Rich ? width.ToString() : "-", Ui.Format ?? "", .. args]);
		}
		catch (Exception e) when (e is not OperationCanceledException)
		{
			return null;
		}
	}

	/// <summary>Writes on and keeps a copy of what was written.</summary>
	private sealed class Copying(TextWriter inner) : TextWriter
	{
		private readonly System.Text.StringBuilder copy = new();
		public string Text => copy.ToString();
		public override System.Text.Encoding Encoding => inner.Encoding;
		public override void Write(char value) { copy.Append(value); inner.Write(value); }
		public override void Write(string? value) { copy.Append(value); inner.Write(value); }
		public override void Write(char[] buffer, int index, int count) { copy.Append(buffer, index, count); inner.Write(buffer, index, count); }
		public override void WriteLine(string? value) { copy.Append(value).Append(CoreNewLine); inner.WriteLine(value); }
		public override void Flush() => inner.Flush();
	}
}
