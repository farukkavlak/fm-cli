using System.Text.Json.Nodes;
using FmCli.Nl;

namespace FmCli;

/// <summary>
/// A sentence typed in place of a command ("önümüzdeki maç ne zaman", "rinaldiyi sat"): the
/// router turns it into the command it asks for, which then runs as if typed. When the model
/// is not sure it offers the likeliest commands with their share of the vote (three, or five
/// when it is far from sure) instead of guessing; what a call needs and the sentence leaves
/// open is asked; a command that spends, signs, sells, lets go or cannot be taken back is shown
/// and run only on a yes.
/// </summary>
internal static partial class Program
{
	private static Neighbours? near;
	private static Embedder? embedder;

	/// <summary>
	/// Commands that change something hard to take back, with the arguments that make them do
	/// it (empty: any call). Their bare forms only show.
	/// </summary>
	private static readonly Dictionary<string, string[]> Weighty = new()
	{
		["fm_offer"] = [], ["fm_contract"] = [], ["fm_release"] = [], ["fm_propose"] = [], ["fm_approach"] = [],
		["fm_staff_hire"] = [], ["fm_staff_fire"] = [], ["fm_resign"] = [], ["fm_stop"] = [], ["fm_load"] = [], ["fm_continue"] = [],
		["fm_save"] = [], ["fm_board_request"] = ["request"], ["fm_jobs"] = ["apply"], ["fm_saves"] = ["delete"],
		["fm_tactic_clear"] = [], ["fm_tactic_new"] = [], ["fm_setpieces_new"] = [], ["fm_register"] = ["send", "auto"],
		["fm_numbers"] = ["send", "auto"], ["fm_reply"] = ["answer"], ["fm_dialog"] = ["answer"], ["fm_vision"] = [],
		["fm_press"] = [], ["fm_select"] = [], ["fm_scout_focus"] = [], ["fm_match"] = [],
	};

	/// <summary>
	/// A line is a sentence, not a command, when it has more than one word, does not start with a
	/// command's own name (or a short alias such as `p`) and carries no --option. A one-word line
	/// stays a command, as does "squad --fitness"; "kadro ne" or "maç ne zaman" start with an
	/// alias but read as sentences.
	/// </summary>
	public static bool IsSentence(string[] words) =>
		words.Length > 1
		&& !CommandNames.Contains(words[0])
		&& !(words[0].Length <= 2 && CommandAliases.ContainsKey(words[0]))
		&& !words.Any(w => w.StartsWith("--", StringComparison.Ordinal));

	public static async Task<int> Understand(string sentence, Func<string[], Task<int>> execute)
	{
		if (Model() is not { } model)
		{
			Console.Error.WriteLine(L($"unknown command: {sentence.Split(' ')[0]} — for the commands: `help`"));
			Console.Error.WriteLine(L($"To ask in a sentence the language model is needed: {ModelDir}"));
			return 64;
		}

		var understood = Nl.Router.Understand(sentence, model);
		if (understood.Calls.Count == 0 && understood.Options.Count == 0)
		{
			Console.WriteLine(L("No command does that. For the commands: `help`"));
			return 1;
		}

		var calls = understood.Calls;
		var chosen = false;
		if (calls.Count == 0)
		{
			if (Choose(understood.Options) is not { } pick) return 0;
			calls = [pick];
			chosen = true;
		}

		var code = 0;
		foreach (var call in calls)
		{
			if (!AskFor(call)) return 0;
			if (Mcp.CommandFor(call.Tool, call.Args) is not { } words) continue;
			var shown = string.Join(' ', words.Select(w => w.Contains(' ') ? $"\"{w}\"" : w));
			// a pick from the list, with nothing asked after, was already a yes
			if (IsWeighty(call) && !(chosen && call.Ask.Count == 0))
			{
				if (!Confirm(L($"Run `{shown}`?"))) return 0;
			}
			else Console.WriteLine(Dim($"→ {shown}"));
			code = await execute(words);
		}
		return code;
	}

	private static string ModelDir =>
		Environment.GetEnvironmentVariable("FM_NL_MODEL") is { Length: > 0 } dir ? dir
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache", "fm-cli", "nl", "model");

	/// <summary>The model, loaded once and kept for the session; null when it is not installed.</summary>
	private static Neighbours? Model()
	{
		if (near != null) return near;
		var dir = ModelDir;
		if (!File.Exists(Path.Combine(dir, "model.onnx"))) return null;
		var examples = Neighbours.Embedded();
		if (!Neighbours.Indexed(dir, examples))
			Console.WriteLine(L("Getting the language model ready (only the first time, about half a minute)…"));
		embedder = new Embedder(dir);
		near = new Neighbours(embedder, examples, dir);
		return near;
	}

	private static bool IsWeighty(Nl.Router.Call call) =>
		Weighty.TryGetValue(call.Tool, out var when) && (when.Length == 0 || when.Any(call.Args.ContainsKey));

	/// <summary>The likely commands, each with its share of the vote; null when none is chosen.</summary>
	private static Nl.Router.Call? Choose(List<(Nl.Router.Call Call, float Weight)> options)
	{
		Console.WriteLine(options.Count == 1 ? L("Did you mean this?") : L("Did you mean one of these?"));
		var described = CommandDescriptions();
		for (var i = 0; i < options.Count; i++)
		{
			var (call, weight) = options[i];
			var words = Mcp.CommandFor(call.Tool, call.Args) ?? [call.Tool];
			var shown = string.Join(' ', words.Select(w => w.Contains(' ') ? $"\"{w}\"" : w));
			var about = described.GetValueOrDefault(words[0], "");
			Console.WriteLine($"  {i + 1}. {shown,-28} {weight * 100,3:0}%  {Dim(Short(about))}");
		}
		Console.Write(L($"Which one (1-{options.Count}, Enter: none)? "));
		var answer = Console.ReadLine()?.Trim();
		return int.TryParse(answer, out var n) && n >= 1 && n <= options.Count ? options[n - 1].Call : null;
	}

	private static string Dim(string text) => Ui.Rich ? $"\u001b[2m{text}\u001b[0m" : text;

	private static string Short(string text) => text.Length <= 60 ? text : text[..57] + "…";

	/// <summary>Asks for what the call needs and the sentence did not say; false when one is left empty.</summary>
	private static bool AskFor(Nl.Router.Call call)
	{
		foreach (var name in call.Ask)
		{
			var schema = Mcp.ArgumentSchema(call.Tool, name);
			var choices = schema?["enum"] is JsonArray e ? e.Select(c => c!.GetValue<string>()).ToList() : [];
			if (choices.Count > 0)
			{
				for (var i = 0; i < choices.Count; i++) Console.WriteLine($"  {i + 1}. {choices[i]}");
				Console.Write($"{name} (1-{choices.Count}): ");
				var typed = Console.ReadLine()?.Trim() ?? "";
				var value = int.TryParse(typed, out var n) && n >= 1 && n <= choices.Count ? choices[n - 1]
					: choices.FirstOrDefault(c => c.Equals(typed, StringComparison.OrdinalIgnoreCase));
				if (value == null) return false;
				call.Args[name] = value;
				continue;
			}
			var about = schema?["description"]?.GetValue<string>() is { Length: > 0 } d ? " " + Dim($"({Short(d)})") : "";
			Console.Write($"{name}{about}: ");
			var answer = Console.ReadLine()?.Trim() ?? "";
			if (answer.Length == 0) return false;
			call.Args[name] = schema?["type"]?.GetValue<string>() == "boolean" ? IsYes(answer) : answer;
		}
		return true;
	}

	private static bool Confirm(string question)
	{
		Console.Write($"{question} {L("[y/N]")} ");
		return IsYes((Console.ReadLine() ?? "").Trim());
	}

	private static bool IsYes(string answer) => Nl.Text.Fold(answer) is "y" or "yes" or "e" or "evet" or "true";
}
