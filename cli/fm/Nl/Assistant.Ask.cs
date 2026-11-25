using System.Text.Json;
using FmCli.Nl;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// `ask <question>`: the assistant, read-only (T208). Answers from what it reads in the game with the
	/// model set up by `ai set`, says what it read, and names what it said that no read holds.
	/// `--new` starts a new talk; the talk is otherwise kept for the save a while.
	/// </summary>
	private static async Task<int> AskCommand(string[] args, bool json)
	{
		var fresh = args.Contains("--new");
		// --new is a flag: the words after it are the question, not its value
		var question = string.Join(" ", Words([.. args.Where(a => a != "--new")])).Trim();
		if (question.Length == 0)
		{
			if (fresh)
			{
				Ask.Forget();
				Console.WriteLine(L("The talk is forgotten: the next question starts a new one."));
				return 0;
			}
			Console.Error.WriteLine(L("usage: ask <question>   (ask --new <question>: a new talk)"));
			return 64;
		}
		if (ChatModels.Current() is not { } model)
		{
			Console.Error.WriteLine(L("The assistant needs a model: ai set <url> <model> --key <key>  (any OpenAI-style provider)"));
			return 1;
		}
		if (await NotNow() is { } refused) return refused;

		string club = "", save = "";
		try
		{
			var human = await Fm.Pull("Human.Club", "Name,UniqueId");
			club = Field(human, "Name");
			save = $"{Field(human, "UniqueId")}|{Field(await Fm.Pull("app.LastSaveSummary", "GameSummaryGameName"), "GameSummaryGameName")}";
		}
		catch (Exception e) when (e is FmError or FmBusy) { }
		var context = club.Length > 0 ? $"The user manages {club}; \"we\" and \"our\" mean this club." : "The user manages no club at the moment.";

		var result = await Ask.Answer(model, question, save, context, fresh, Run,
			reading: words => Ui.Progress(L($"reading: {words}…")));
		Ui.Progress(null);

		if (json)
		{
			Console.WriteLine(JsonOut.Of(new { result.Answer, result.Read, result.Refused, result.Unread, result.Seconds, result.Error }));
			return result.Error == null ? 0 : 1;
		}
		if (result.Error != null)
		{
			Console.Error.WriteLine(L($"{model.Name} gave no answer ({result.Error}). Check it: ai test"));
			return 1;
		}
		// The model writes Markdown: bold is drawn as bold at a terminal and left plain elsewhere.
		Console.WriteLine(System.Text.RegularExpressions.Regex.Replace(result.Answer, @"\*\*(.+?)\*\*", m => Ui.Rich ? $"\u001b[1m{m.Groups[1].Value}\u001b[0m" : m.Groups[1].Value));
		Console.WriteLine();
		Console.WriteLine(Dim(result.Read.Count > 0
			? L($"Read: {string.Join(" · ", result.Read.Distinct())}  ({result.Seconds:0.0} s)")
			: L($"Nothing was read from the game for this answer ({result.Seconds:0.0} s).")));
		if (result.Refused.Count > 0)
			Console.WriteLine(Dim(L($"Not run (it could change the game): {string.Join(" · ", result.Refused.Distinct())}")));
		if (result.Unread.Count > 0)
			Console.WriteLine(L($"⚠ Not in anything read: {string.Join(", ", result.Unread)}. Check before you rely on it."));
		return 0;
	}
}
