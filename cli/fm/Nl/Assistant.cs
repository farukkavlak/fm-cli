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
	/// an alias but read as sentences. An alias of a command no tool runs (`sor`, `planla`, `hafıza`)
	/// stays that command: it reads its words itself.
	/// </summary>
	public static bool IsSentence(string[] args) =>
		// fm "maç ne zaman" arrives as one argument
		string.Join(' ', args).Split(' ', StringSplitOptions.RemoveEmptyEntries) is var words
		&& words.Length > 1
		&& !words.Any(w => w.StartsWith("--", StringComparison.Ordinal))
		&& (CommandNames.Contains(words[0]) ? !Mcp.TakesText(words[0])
			&& !words.Skip(1).All(w => w.All(char.IsDigit) || CommandFlags.GetValueOrDefault(words[0], []).Contains(Nl.Text.Fold(w)))
			: !(words[0].Length <= 2 && CommandAliases.ContainsKey(words[0]))
				// "sor kaç puanımız var", "hafıza sil 2": an alias of a command of the CLI's own (no tool), which reads its words itself
				&& !(CommandAliases.TryGetValue(Nl.Text.Fold(words[0]), out var own) && Mcp.OwnCommand(own)));

	public static async Task<int> Understand(string sentence, Func<string[], Task<int>> execute)
	{
		if (Model() is not { } model)
		{
			Console.Error.WriteLine(L($"unknown command: {sentence.Split(' ')[0]} — for the commands: `help`"));
			Console.Error.WriteLine(L("To ask in a sentence the language model is needed: `fm doctor --install` downloads it."));
			return 64;
		}

		var seen = NameFit.StandIn(sentence, await SaveNames());
		var understood = Nl.Router.Understand(sentence, model, seen);
		// smart mode: the cloud model the user set up, asked only when the router is not sure
		if (understood.Calls.Count == 0 && Cloud.Current() is { } cloud)
		{
			Console.WriteLine(Dim(L($"Not sure; asking {cloud.Model}…")));
			understood = await Nl.Router.AskCloud(sentence, model, seen, cloud, understood) ?? understood;
		}
		if (understood.Calls.Count == 0 && understood.Options.Count == 0)
		{
			Console.WriteLine(L("No command does that. For the commands: `help`"));
			return 1;
		}

		// A line that starts with the command it means is that command, every word kept: the command says
		// what it does not understand, where a filled-in call would drop the word ("dugout 1 zzz" opening advice 1).
		var typed = sentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (understood.Calls is [var only] && only.Tool == $"fm_{typed[0]}")
		{
			Learned.Next(typed[0]);
			return await execute(typed);
		}

		var calls = understood.Calls;
		var chosen = false;
		var byEnter = false;
		if (calls.Count == 0)
		{
			(var pick, var own, byEnter) = Choose(understood.Options);
			if (own != null) return await RunInstead(own, understood, seen ?? sentence, sentence, execute);
			if (pick == null) return NothingRun;
			calls = [pick];
			chosen = true;
		}

		var code = 0;
		var done = new List<string>();
		for (var i = 0; i < calls.Count; i++)
		{
			var call = calls[i];
			if (!AskFor(call) || !await NamePlayers(call)) return Stopped(done, calls.Skip(i), NothingRun, calls.Count);
			if (Typed(call) is not { } words) continue;
			var shown = Shown(words);
			var yes = chosen;
			// a pick from the list, with nothing asked after, was already a yes; one the cloud model and
			// the router agreed on runs by itself only when it changes nothing in the game
			var agreed = understood.FromCloud && !chosen && Mcp.EffectOf(call.Tool, call.Args) != Mcp.Effect.Read;
			// a bare Enter took the first without naming it: what cannot be taken back is still asked
			if (IsWeighty(call) && (byEnter || !(chosen && call.Ask.Count == 0)) || agreed)
			{
				if (!Confirm(L($"Run `{shown}`?"))) return Stopped(done, calls.Skip(i), NothingRun, calls.Count);
				yes = true;
			}
			else Console.WriteLine(Dim($"→ {shown}"));
			Learned.Next(words[0]);
			code = await execute(words);
			// what the user chose for a sentence of one ask is learned (the tool only), unless it failed
			if (code == 0 && yes && calls.Count == 1)
				Learned.Chose(new Learned.Sentence(seen ?? sentence, sentence, call.Tool, shown), Rivals(understood, call));
			// a step that failed stops the rest: they were asked for after it, and may count on it
			if (code != 0) return Stopped(done, calls.Skip(i + 1), code, calls.Count);
			done.Add(shown);
		}
		return code;
	}

	/// <summary>
	/// None of the offered commands was meant and the user typed the one that was: it runs, and when it
	/// works the sentence is learned for its tool, like a pick from the list (open since T206).
	/// </summary>
	private static async Task<int> RunInstead(string[] typed, Nl.Router.Understood understood, string text, string sentence, Func<string[], Task<int>> execute)
	{
		var command = Shown(typed);
		Learned.Next(typed[0]);
		var code = await execute(typed);
		if (code == 0 && Mcp.ToolForTyped(typed) is { } tool)
		{
			var rivals = understood.Options.Select(o => Typed(o.Call) is [var name, ..] ? name : null).OfType<string>().Where(n => n != typed[0]);
			Learned.Chose(new Learned.Sentence(text, sentence, tool, command), rivals);
		}
		return code;
	}

	/// <summary>The commands of the other tools the sentence was near: a next command among them puts a choice right.</summary>
	private static IEnumerable<string> Rivals(Nl.Router.Understood understood, Nl.Router.Call chosen) =>
		understood.Options.Select(o => o.Call.Tool).Concat(understood.Tools).Where(t => t != chosen.Tool).Distinct()
			.Select(t => Mcp.CommandFor(t, new JsonObject()) is [var name, ..] ? name : null).OfType<string>()
			.Where(name => name != (Typed(chosen) is [var mine, ..] ? mine : null));

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
		var dir = ModelDir;
		if (near != null) return Taught(near, dir);
		if (!File.Exists(Path.Combine(dir, "model.onnx"))) return null;
		var examples = Neighbours.Embedded();
		if (!Neighbours.Indexed(dir, examples))
			Console.WriteLine(L("Getting the language model ready (only the first time, about half a minute)…"));
		embedder = new Embedder(dir);
		near = new Neighbours(embedder, examples, dir);
		learnedFrom = null;
		return Taught(near, dir);
	}

	private static string? learnedFrom;

	/// <summary>The user's own sentences put in the vote, again whenever the file has changed (a session goes on learning).</summary>
	private static Neighbours Taught(Neighbours model, string dir)
	{
		var stamp = File.Exists(Learned.File) ? $"{File.GetLastWriteTimeUtc(Learned.File).Ticks}" : "";
		if (stamp == learnedFrom) return model;
		model.Learn(Learned.Examples(Learned.Load()), dir);
		learnedFrom = stamp;
		return model;
	}

	/// <summary>
	/// The command as a person would type it: the tool's words without the --json some tools add
	/// for a model reading them, since here a person reads the output.
	/// </summary>
	private static string[]? Typed(Nl.Router.Call call) =>
		Mcp.CommandFor(call.Tool, call.Args) is { } words ? [.. words.Where(w => w != "--json")] : null;

	/// <summary>A call that cannot be taken back and matters (Mcp.Effect.Weighty): shown and run only on a yes.</summary>
	internal static bool IsWeighty(Nl.Router.Call call) => Mcp.EffectOf(call.Tool, call.Args) == Mcp.Effect.Weighty;

	/// <summary>
	/// The likely commands, each with its share of the vote and a short line of what that call does
	/// with its arguments. Enter takes the first (ByEnter: a weighty one is still asked); 0 says none is meant and asks for the command that
	/// is (Typed), which runs and is learned for the sentence. Null when nothing is chosen.
	/// </summary>
	private static (Nl.Router.Call? Pick, string[]? Typed, bool ByEnter) Choose(List<(Nl.Router.Call Call, float Weight)> options)
	{
		Console.WriteLine(options.Count == 1 ? L("Did you mean this?") : L("Did you mean one of these?"));
		var described = CommandDescriptions();
		var rows = options.Select(o =>
		{
			var words = Typed(o.Call) ?? [o.Call.Tool];
			// an argument still to be asked stands empty in the words
			var shown = string.Join(' ', words.Select(w => w.Length == 0 ? "…" : w.Contains(' ') ? $"\"{w}\"" : w));
			var share = o.Weight == Nl.Router.FromCloudWeight ? L("ai").PadLeft(4) : $"{o.Weight * 100,3:0}%";
			var about = CallLines.Of(o.Call.Tool, o.Call.Args, (arg, value) => ChoiceText(o.Call.Tool, arg, value))
				?? Short(described.GetValueOrDefault(words[0], ""));
			return (shown, share, about);
		}).ToList();
		var width = Math.Min(rows.Max(r => r.shown.Length), 40);
		for (var i = 0; i < rows.Count; i++)
			Console.WriteLine($"  {i + 1}. {rows[i].shown.PadRight(width)}  {rows[i].share}  {Dim(rows[i].about)}");
		if (options.Count == 1) return (Confirm(L("Run it?")) ? options[0].Call : null, null, false);
		Console.Write(L($"Which one (1-{options.Count}, Enter: 1, 0: none)? "));
		var answer = Console.ReadLine()?.Trim();
		// no input at all (a script's closed stdin) picks nothing; an empty line is Enter
		if (answer == null) return (null, null, false);
		if (answer.Length == 0) return (options[0].Call, null, true);
		if (int.TryParse(answer, out var n) && n >= 1 && n <= options.Count) return (options[n - 1].Call, null, false);
		if (answer != "0") return (null, null, false);
		Console.Write(L("Which command did you mean (Enter: nothing)? "));
		var typed = (Console.ReadLine() ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if (typed.Length == 0) return (null, null, false);
		if (!CommandNames.Contains(typed[0]))
		{
			Console.WriteLine(L($"unknown command: {typed[0]} — for the commands: `help`"));
			return (null, null, false);
		}
		return (null, typed, false);
	}

	internal static string Dim(string text) => Ui.Rich ? $"\u001b[2m{text}\u001b[0m" : text;

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
		["fm_holiday.until=week"] = "For a week",
		["fm_holiday.until=month"] = "For a month",
		["fm_holiday.until=match"] = "Until the next match",
		["fm_setpieces.delivery=in"] = "Inswinging",
		["fm_setpieces.delivery=out"] = "Outswinging",
		["fm_vision.action=suggest"] = "Suggest",
		["fm_vision.action=undo"] = "Undo",
		["fm_vision.action=reset"] = "Reset",
		["fm_vision.action=confirm"] = "Confirm",
		["fm_vision.action=end"] = "End the meeting",
		["fm_vision.action=plan"] = "The plan",
		["fm_pause.do=pause"] = "Pause",
		["fm_pause.do=resume"] = "Play on",
		["fm_pause.do=skip"] = "Skip the replay",
		["fm_scout_teams.action=top"] = "Move to the top",
		["fm_scout_teams.action=cancel"] = "Cancel",
		["fm_transferroom.action=offer"] = "Offer a player",
		["fm_transferroom.action=activity"] = "The players offered",
		["fm_transferroom.action=wanted"] = "Our requirements",
		["fm_transferroom.action=responses"] = "The replies",
		["fm_transferroom.action=new"] = "A new requirement",
		["fm_transferroom.action=delete"] = "Delete a requirement",
		["fm_transferroom.action=agents"] = "The agents",
		["fm_transferroom.action=discard"] = "Discard",
		["fm_schedule_week.do=copy"] = "Copy the week",
		["fm_schedule_week.do=paste"] = "Paste a week",
		["fm_schedule_week.do=clear"] = "Clear the week",
		["action=move"] = "Move",
		["action=add"] = "Add",
		["action=remove"] = "Remove",
		["action=suggest"] = "Suggest",
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
