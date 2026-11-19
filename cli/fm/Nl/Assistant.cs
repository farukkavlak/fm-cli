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
	/// A line is a sentence, not a command, when it has more than one word and no --option, and
	/// does not start with a short alias such as `p`. One that starts with a command's own name is
	/// a sentence only when that command takes no free text and the rest is not numbers and its own
	/// words: "next 3", "dugout 2 uygula" and "player rinaldi" stay commands, "next opponent" or
	/// "table please" do not. A one-word
	/// line stays a command, as does "squad --fitness"; "kadro ne" or "maç ne zaman" start with
	/// an alias but read as sentences.
	/// </summary>
	public static bool IsSentence(string[] args) =>
		// fm "maç ne zaman" arrives as one argument
		string.Join(' ', args).Split(' ', StringSplitOptions.RemoveEmptyEntries) is var words
		&& words.Length > 1
		&& !words.Any(w => w.StartsWith("--", StringComparison.Ordinal))
		&& (CommandNames.Contains(words[0]) ? !Mcp.TakesText(words[0])
			&& !words.Skip(1).All(w => w.All(char.IsDigit) || CommandFlags.GetValueOrDefault(words[0], []).Contains(Nl.Text.Fold(w)))
			: !(words[0].Length <= 2 && CommandAliases.ContainsKey(words[0])));

	public static async Task<int> Understand(string sentence, Func<string[], Task<int>> execute)
	{
		if (Model() is not { } model)
		{
			Console.Error.WriteLine(L($"unknown command: {sentence.Split(' ')[0]} — for the commands: `help`"));
			Console.Error.WriteLine(L("To ask in a sentence the language model is needed: `fm doctor --install` downloads it."));
			return 64;
		}

		var understood = Nl.Router.Understand(sentence, model);
		if (understood.Calls.Count == 0 && understood.Options.Count == 0)
		{
			Console.WriteLine(L("No command does that. For the commands: `help`"));
			return 1;
		}

		// A line that starts with the command it means is that command, every word kept: the command says
		// what it does not understand, where a filled-in call would drop the word ("dugout 1 zzz" opening advice 1).
		var typed = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (understood.Calls is [var only] && only.Tool == $"fm_{typed[0]}") return await execute(typed);

		var calls = understood.Calls;
		var chosen = false;
		if (calls.Count == 0)
		{
			if (Choose(understood.Options) is not { } pick) return NothingRun;
			calls = [pick];
			chosen = true;
		}

		var code = 0;
		var done = new List<string>();
		for (var i = 0; i < calls.Count; i++)
		{
			var call = calls[i];
			if (!AskFor(call)) return Stopped(done, calls.Skip(i), NothingRun, calls.Count);
			if (Typed(call) is not { } words) continue;
			var shown = Shown(words);
			// a pick from the list, with nothing asked after, was already a yes
			if (IsWeighty(call) && !(chosen && call.Ask.Count == 0))
			{
				if (!Confirm(L($"Run `{shown}`?"))) return Stopped(done, calls.Skip(i), NothingRun, calls.Count);
			}
			else Console.WriteLine(Dim($"→ {shown}"));
			code = await execute(words);
			// a step that failed stops the rest: they were asked for after it, and may count on it
			if (code != 0) return Stopped(done, calls.Skip(i + 1), code, calls.Count);
			done.Add(shown);
		}
		return code;
	}

	/// <summary>The exit code of a line left undone: a person said no, a script got nothing it asked for.</summary>
	private static int NothingRun => Ui.Interactive ? 0 : 1;

	private static string Shown(IEnumerable<string> words) => string.Join(' ', words.Select(w => w.Contains(' ') ? $"\"{w}\"" : w));

	/// <summary>
	/// A chain of commands ended before its last: says which ran and which did not, so nothing
	/// is left to guess. A single command says nothing more than its own output.
	/// </summary>
	private static int Stopped(List<string> done, IEnumerable<Nl.Router.Call> left, int code, int asked) =>
		Halfway.Stop(done, left.Select(c => Typed(c) is { } w ? Shown(w) : c.Tool), code, always: asked > 1);

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

	/// <summary>
	/// The command as a person would type it: the tool's words without the --json some tools add
	/// for a model reading them, since here a person reads the output.
	/// </summary>
	private static string[]? Typed(Nl.Router.Call call) =>
		Mcp.CommandFor(call.Tool, call.Args) is { } words ? [.. words.Where(w => w != "--json")] : null;

	/// <summary>A call that cannot be taken back and matters (Mcp.Effect.Weighty): shown and run only on a yes.</summary>
	internal static bool IsWeighty(Nl.Router.Call call) => Mcp.EffectOf(call.Tool, call.Args) == Mcp.Effect.Weighty;

	/// <summary>The likely commands, each with its share of the vote; null when none is chosen.</summary>
	private static Nl.Router.Call? Choose(List<(Nl.Router.Call Call, float Weight)> options)
	{
		Console.WriteLine(options.Count == 1 ? L("Did you mean this?") : L("Did you mean one of these?"));
		var described = CommandDescriptions();
		for (var i = 0; i < options.Count; i++)
		{
			var (call, weight) = options[i];
			var words = Typed(call) ?? [call.Tool];
			var shown = string.Join(' ', words.Select(w => w.Contains(' ') ? $"\"{w}\"" : w));
			var about = described.GetValueOrDefault(words[0], "");
			Console.WriteLine($"  {i + 1}. {shown,-28} {weight * 100,3:0}%  {Dim(Short(about))}");
		}
		if (options.Count == 1) return Confirm(L("Run it?")) ? options[0].Call : null;
		Console.Write(L($"Which one (1-{options.Count}, Enter: none)? "));
		var answer = Console.ReadLine()?.Trim();
		return int.TryParse(answer, out var n) && n >= 1 && n <= options.Count ? options[n - 1].Call : null;
	}

	private static string Dim(string text) => Ui.Rich ? $"\u001b[2m{text}\u001b[0m" : text;

	private static string Short(string text) => text.Length <= 60 ? text : text[..57] + "…";

	/// <summary>
	/// The question for an argument a sentence left open, by "tool.argument" or, for the rest, by
	/// the argument's name. In English here, in the game's language on screen (labels-turkish.tsv).
	/// </summary>
	internal static readonly Dictionary<string, string> Questions = new()
	{
		["fm_match.mode"] = "How do you want to play the match?",
		["fm_approach.move"] = "What kind of move?",
		["fm_scout_focus.action"] = "What to do with the recruitment focus?",
		["fm_tactic_set.setting"] = "Which setting?",
		["fm_load.name"] = "Which save?",
		["fm_load.save"] = "Save the current game first?",
		["fm_stop.save"] = "Save before closing?",
		["fm_tactic_file.save"] = "Name to save the tactic as?",
		["fm_staff_fire.name"] = "Which member of staff?",
		["fm_staff_hire.name"] = "Who to approach for the staff?",
		["fm_compare.a"] = "First player?",
		["fm_compare.b"] = "Second player?",
		["fm_reply.answer"] = "Which answer (number or a piece of its text)?",
		["fm_dugout.number"] = "Which piece of advice (its number)?",
		["fm_captains.captain"] = "Who is the new captain?",
		["kind"] = "Which set piece?",
		["side"] = "Which side?",
		["player"] = "Which player?",
		["name"] = "Which player?",
		["person"] = "Who?",
		["page"] = "Which page?",
		["label"] = "Which control (its label)?",
		["where"] = "Which players (e.g. age<23 and position=ST)?",
		["message"] = "Which message (number or a piece of its subject)?",
		["text"] = "Search for what?",
		["list"] = "Which list?",
		["choice"] = "Which choice?",
		["position"] = "Which position (e.g. ST, DC)?",
		["length"] = "For how long (e.g. 1 month)?",
		["on"] = "Who comes on?",
		["off"] = "Who comes off?",
		["club"] = "Which club?",
		["month"] = "Which month?",
		["competition"] = "Which competition?",
		["slot"] = "Which tactic slot (1-3)?",
		["day"] = "Which day?",
		["offer"] = "Which offer?",
		["answer"] = "Which answer?",
		["number"] = "Which number?",
	};

	/// <summary>What a choice of an argument means, by "tool.argument=value" or "argument=value".</summary>
	internal static readonly Dictionary<string, string> AskChoices = new()
	{
		["fm_match.mode=instant"] = "Instant result (the staff play it)",
		["fm_match.mode=play"] = "Play it, with the team talks",
		["fm_match.mode=play_no_talk"] = "Play it, skipping the team talks",
		["fm_approach.move=interest"] = "Show interest",
		["fm_approach.move=enquire"] = "Ask his club what it would take",
		["fm_approach.move=trial"] = "Invite him on trial",
		["fm_scout_focus.action=new"] = "Set up a new focus",
		["fm_scout_focus.action=edit"] = "Change a focus",
		["fm_scout_focus.action=areas"] = "List the areas",
		["fm_scout_focus.action=remove"] = "Stop a focus",
		["fm_scout_focus.action=restart"] = "Restart a focus",
		["fm_scout_focus.action=staff"] = "List the scouts",
		["fm_tactic_set.setting=mentality"] = "Mentality",
		["fm_tactic_set.setting=style"] = "Style",
		["kind=corner"] = "Corner",
		["kind=freekick"] = "Free kick",
		["kind=wide"] = "Wide free kick",
		["kind=deep"] = "Deep free kick",
		["kind=throw"] = "Throw-in",
		["side=right"] = "Right side",
		["side=left"] = "Left side",
		["answer=view"] = "View it",
		["answer=accept"] = "Accept",
		["answer=stall"] = "Stall",
		["answer=reject"] = "Reject",
	};

	private static string Question(string tool, string arg) =>
		L(Questions.GetValueOrDefault($"{tool}.{arg}") ?? Questions.GetValueOrDefault(arg) ?? $"{arg}?");

	private static string ChoiceText(string tool, string arg, string value) =>
		L(AskChoices.GetValueOrDefault($"{tool}.{arg}={value}") ?? AskChoices.GetValueOrDefault($"{arg}={value}") ?? value);

	/// <summary>Asks for what the call needs and the sentence did not say; false when one is left empty.</summary>
	private static bool AskFor(Nl.Router.Call call)
	{
		foreach (var name in call.Ask)
		{
			var schema = Mcp.ArgumentSchema(call.Tool, name);
			var question = Question(call.Tool, name);
			if (schema?["type"]?.GetValue<string>() == "boolean")
			{
				call.Args[name] = Confirm(question);
				continue;
			}
			var choices = schema?["enum"] is JsonArray e ? e.Select(c => c!.GetValue<string>()).ToList() : [];
			if (choices.Count > 0)
			{
				Console.WriteLine(question);
				var shown = choices.Select(c => ChoiceText(call.Tool, name, c)).ToList();
				for (var i = 0; i < choices.Count; i++) Console.WriteLine($"  {i + 1}. {shown[i]}");
				Console.Write(L($"Which one (1-{choices.Count}, Enter: none)? "));
				var typed = Console.ReadLine()?.Trim() ?? "";
				var at = int.TryParse(typed, out var n) && n >= 1 && n <= choices.Count ? n - 1
					: choices.FindIndex(c => c.Equals(typed, StringComparison.OrdinalIgnoreCase)) is var i2 and >= 0 ? i2
					: shown.FindIndex(c => Nl.Text.Fold(c) == Nl.Text.Fold(typed));
				if (at < 0) return false;
				call.Args[name] = choices[at];
				continue;
			}
			Console.Write($"{question} ");
			var answer = Console.ReadLine()?.Trim() ?? "";
			if (answer.Length == 0) return false;
			call.Args[name] = answer;
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
