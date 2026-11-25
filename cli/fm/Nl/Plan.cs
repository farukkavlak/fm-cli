using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FmCli.Nl;

/// <summary>
/// Plans (T209): something asked for in a sentence, done in steps. The model reads the game with the read
/// tools and proposes the steps, each one call of a tool that acts, with a read that checks it. Our code
/// checks every step (<see cref="Mcp.NotAStep"/>), the user sees them all and says yes, and each step runs
/// in turn: its check is read before and after, and the step counts as done only when the read after shows
/// what it was to show. A step that did not hold stops the rest.
/// </summary>
internal static class Plan
{
	public sealed record Step(string Tool, JsonObject Args, string Why, string CheckTool, JsonObject CheckArgs, string Expect)
	{
		public string Command => Words(Tool, Args);
		public string Check => Words(CheckTool, CheckArgs);
		public Mcp.Effect Effect => Mcp.EffectOf(Tool, Args);
	}

	/// <summary>The proposed steps, or none and why (<see cref="Note"/>); what was read for them and what was refused.</summary>
	public sealed record Draft(List<Step> Steps, string Note, List<string> Read, List<string> Refused, double Seconds, int Tokens, string? Error);

	public enum Outcome
	{
		/// <summary>Ran, the check changed and shows what it was to show.</summary>
		Done,
		/// <summary>Ran, the check did not change, and it already showed what it was to show.</summary>
		AlreadySo,
		/// <summary>Ran, but the check does not show it: the game took it and did not do it, or did something else.</summary>
		NotDone,
		/// <summary>The command failed.</summary>
		Failed,
		/// <summary>Ran and the check changed, but the model gave no verdict on it.</summary>
		Unchecked,
	}

	public sealed record Ran(Step Step, Outcome Outcome, string Seen);

	private const int MaxSteps = 12;
	private const int Rounds = 10;
	private const int MaxReads = 16;
	private const int MaxProposals = 3;
	private const int MaxCheck = 12000;

	private const string Prompt = """
		You plan changes for someone playing Football Manager from a terminal tool. You run nothing: the user sees your plan and says yes, then the tool runs it step by step and checks each step.
		First read the game with the read tools: what holds now, the names and numbers the steps need. Then call propose_plan once.
		Each step is one call of a tool from the list of tools that act below, with arguments as its schema gives them.
		Use only what the tools returned in this talk, never your own knowledge of football: names, numbers and options exactly as a tool wrote them. Never make up a fee, a wage or a name: one the user did not give and no read showed is left out of the plan, and the note says what is missing.
		For each step give a check: one read call (a read tool and its arguments) whose result shows whether the step held, and in expect, what it should show after the step.
		When what is asked goes against one of the user's notes, plan what was asked all the same, and say in the note which note it goes against, as (#N): the user decides.
		No step for what already holds. As few steps as do it, at most 12; keep why and expect to a few words. Time, matches, talks and saves stay the user's: never plan them.
		If it cannot be done with these tools, or the user has to decide something first (which player, how much), call no propose_plan: say so in a few lines, with what you read that bears on it.
		Write why, expect and the note in the language the request is written in, even when the game is in another; short and plain. Keep names and the game's own words as the tools write them.
		To the user a command is named as it is typed in the terminal (`next`, `intensity`), never as a tool (fm_next).
		""";

	private const string CheckPrompt = """
		You check one step of a plan carried out in Football Manager. You are given the step, what it was to show after it, and a read of the game before and after the step.
		Answer with JSON only: {"holds": true or false, "seen": "one short line: what the read after shows that bears on it"}.
		holds is true only when the read after shows what was expected. A read that does not show it, or shows something else, is false.
		Write seen in the language the following request is written in, even when the reads are in another (names and the game's own words as the reads write them). The request:
		""";

	/// <summary>
	/// The plan for <paramref name="goal"/>. <paramref name="context"/> is what is known without reading (our
	/// club); <paramref name="run"/> runs a command's words as the CLI would; <paramref name="reading"/> is told each read.
	/// </summary>
	public static async Task<Draft> Make(IChatModel model, string goal, string context, Func<string[], Task<int>> run, Action<string>? reading = null)
	{
		var started = Environment.TickCount64;
		double Seconds() => (Environment.TickCount64 - started) / 1000.0;

		var tools = new JsonArray();
		foreach (var (name, description, schema) in Mcp.ReadTools())
			tools.Add(Function(name, description, schema));
		var acts = Mcp.PlanTools();
		tools.Add(Function("propose_plan", "Propose the plan: the steps that do what was asked, each with a read that checks it. The user sees it before anything runs.",
			ProposeSchema(acts.Select(a => a.Name))));

		var listed = string.Join("\n", acts.Select(a => $"- {a.Name}: {a.Description}\n  arguments: {a.Schema["properties"]?.ToJsonString() ?? "{}"}"));
		var messages = new JsonArray
		{
			new JsonObject { ["role"] = "system", ["content"] = $"{Prompt}\n{context}\n\nTools that act (for the steps only; you cannot call them):\n{listed}" },
			new JsonObject { ["role"] = "user", ["content"] = goal },
		};

		var read = new List<string>();
		var refused = new List<string>();
		var seen = new List<string>();
		var tokens = 0;
		var reads = 0;
		var proposals = 0;
		for (var round = 0; round < Rounds; round++)
		{
			var reply = await model.Next(messages, tools, 120, 6000);
			tokens += reply.Tokens;
			if (reply.Error != null) return new Draft([], "", read, refused, Seconds(), tokens, reply.Error);
			if (reply.Calls.Count == 0)
				return reply.Text.Length > 0
					? new Draft([], reply.Text, read, refused, Seconds(), tokens, null)
					: new Draft([], "", read, refused, Seconds(), tokens, "no answer");
			messages.Add(reply.Message!);
			Draft? made = null;
			foreach (var call in reply.Calls)
			{
				if (call.Name != "propose_plan")
				{
					messages.Add(await Ask.Read(call, ++reads > MaxReads, run, read, refused, seen, reading));
					continue;
				}
				proposals++;
				var (steps, note, faults) = Proposed(call.Args);
				if (reply.Cut) faults.Insert(0, "The plan was cut off before its end: too long. Fewer steps, shorter why and expect.");
				if (faults.Count == 0 && steps.Count > 0)
				{
					made = new Draft(steps, note, read, refused, Seconds(), tokens, null);
					messages.Add(Answer(call, "Proposed; the user is shown it."));
					continue;
				}
				var why = steps.Count == 0 && faults.Count == 0 ? "A plan needs at least one step." : string.Join("\n", faults);
				messages.Add(Answer(call, proposals >= MaxProposals
					? $"Not accepted:\n{why}\nNo more proposals: say in a few lines why it cannot be planned."
					: $"Not accepted, nothing was shown to the user:\n{why}\nFix these and call propose_plan again, or say why it cannot be planned."));
			}
			if (made != null) return made;
		}
		return new Draft([], "", read, refused, Seconds(), tokens, "no plan");
	}

	/// <summary>The steps of a propose_plan call, its note, and what is wrong with them (one line each); a plan with faults is not shown.</summary>
	internal static (List<Step> Steps, string Note, List<string> Faults) Proposed(JsonObject args)
	{
		var steps = new List<Step>();
		var faults = new List<string>();
		var note = Str(args["note"]);
		var given = Node(args["steps"]) as JsonArray ?? [];
		if (Environment.GetEnvironmentVariable("FM_PLAN_LOG") is { Length: > 0 } log) File.AppendAllText(log, args.ToJsonString() + "\n");
		if (given.Count > MaxSteps) faults.Add($"{given.Count} steps: at most {MaxSteps}.");
		for (var i = 0; i < given.Count && i < MaxSteps; i++)
		{
			var s = Node(given[i]) as JsonObject ?? [];
			var tool = Str(s["tool"]);
			var arguments = Node(s["arguments"]) as JsonObject ?? [];
			var check = Node(s["check"]) as JsonObject ?? [];
			var checkTool = Str(check["tool"]);
			var checkArgs = Node(check["arguments"]) as JsonObject ?? [];
			if (Mcp.NotAStep(tool, arguments) is { } notStep) faults.Add($"step {i + 1}: {notStep}.");
			else if (!Mcp.Readable(checkTool, checkArgs)) faults.Add($"step {i + 1}: its check {checkTool} is not a read with those arguments.");
			else if (Str(s["expect"]).Length == 0) faults.Add($"step {i + 1}: expect is empty.");
			else steps.Add(new Step(tool, (JsonObject)arguments.DeepClone(), Str(s["why"]), checkTool, (JsonObject)checkArgs.DeepClone(), Str(s["expect"])));
		}
		return (steps, note, faults);
	}

	/// <summary>
	/// Runs the steps in turn, each only on <paramref name="allowed"/> (asked for the weighty ones), and checks
	/// each: its check read before and after, and the model's verdict on them. Stops at the first step not
	/// allowed, failed or not shown to hold; <paramref name="told"/> hears each outcome as it comes.
	/// </summary>
	public static async Task<(List<Ran> Ran, List<Step> Left)> Run(IChatModel model, string goal, List<Step> steps,
		Func<Step, bool> allowed, Func<string[], Task<int>> run, Action<string>? progress = null, Action<Ran>? told = null)
	{
		var ran = new List<Ran>();
		for (var i = 0; i < steps.Count; i++)
		{
			var step = steps[i];
			if (!allowed(step)) return (ran, steps.Skip(i).ToList());
			progress?.Invoke(step.Check);
			var (before, _) = await Mcp.RunRead(step.CheckTool, step.CheckArgs, run);
			progress?.Invoke(step.Command);
			var (text, failed) = await Mcp.RunStep(step.Tool, step.Args, run);
			Ran result;
			if (failed) result = new Ran(step, Outcome.Failed, FirstLines(text));
			else
			{
				progress?.Invoke(step.Check);
				var (after, _) = await Mcp.RunRead(step.CheckTool, step.CheckArgs, run);
				var changed = after != before;
				var (holds, seenText) = await Verdict(model, goal, step, text, before, after);
				var outcome = holds switch
				{
					true => changed ? Outcome.Done : Outcome.AlreadySo,
					false => Outcome.NotDone,
					null => changed ? Outcome.Unchecked : Outcome.NotDone,
				};
				result = new Ran(step, outcome, seenText);
			}
			ran.Add(result);
			told?.Invoke(result);
			if (result.Outcome is not (Outcome.Done or Outcome.AlreadySo)) return (ran, steps.Skip(i + 1).ToList());
		}
		return (ran, []);
	}

	/// <summary>The model's verdict on a step from its check read before and after: holds or not, and what it saw; null holds when it gave none.</summary>
	private static async Task<(bool? Holds, string Seen)> Verdict(IChatModel model, string goal, Step step, string output, string before, string after)
	{
		var messages = new JsonArray
		{
			new JsonObject { ["role"] = "system", ["content"] = $"{CheckPrompt} {goal}" },
			new JsonObject { ["role"] = "user", ["content"] =
				$"Step: {step.Command}\nWhy: {step.Why}\nIt was to show after it: {step.Expect}\n\nThe step printed:\n{Cut(output)}\n\n{Changed(before, after)}\n\nRead before ({step.Check}):\n{Cut(before)}\n\nRead after ({step.Check}):\n{Cut(after)}" },
		};
		var reply = await model.Next(messages, [], 45);
		if (Environment.GetEnvironmentVariable("FM_PLAN_LOG") is { Length: > 0 } log) File.AppendAllText(log, $"verdict: {reply.Error} {reply.Text}\n");
		if (reply.Error != null) return (null, "");
		var json = Regex.Match(reply.Text, @"\{[\s\S]*\}");
		if (!json.Success) return (null, "");
		try
		{
			var verdict = JsonNode.Parse(json.Value);
			bool? holds = verdict?["holds"]?.ToString().ToLowerInvariant() switch { "true" => true, "false" => false, _ => null };
			return (holds, Str(verdict?["seen"]));
		}
		catch (Exception e) when (e is System.Text.Json.JsonException or InvalidOperationException or FormatException) { return (null, ""); }
	}

	private static JsonObject ProposeSchema(IEnumerable<string> acts) => new()
	{
		["type"] = "object",
		["required"] = new JsonArray("steps"),
		["properties"] = new JsonObject
		{
			["steps"] = new JsonObject
			{
				["type"] = "array",
				["maxItems"] = MaxSteps,
				["items"] = new JsonObject
				{
					["type"] = "object",
					["required"] = new JsonArray("tool", "arguments", "why", "check", "expect"),
					["properties"] = new JsonObject
					{
						["tool"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. acts.Select(a => (JsonNode)a)]) },
						["arguments"] = new JsonObject { ["type"] = "object", ["description"] = "The tool's arguments, as its schema in the list gives them." },
						["why"] = new JsonObject { ["type"] = "string", ["description"] = "Why this step, in a few words." },
						["check"] = new JsonObject
						{
							["type"] = "object",
							["required"] = new JsonArray("tool", "arguments"),
							["description"] = "A read tool call whose result shows whether the step held.",
							["properties"] = new JsonObject
							{
								["tool"] = new JsonObject { ["type"] = "string" },
								["arguments"] = new JsonObject { ["type"] = "object" },
							},
						},
						["expect"] = new JsonObject { ["type"] = "string", ["description"] = "What the check should show after the step." },
					},
				},
			},
			["note"] = new JsonObject { ["type"] = "string", ["description"] = "What the user should know: what was left out and why. Empty if nothing." },
		},
	};

	private static JsonObject Function(string name, string description, JsonObject schema) =>
		new() { ["type"] = "function", ["function"] = new JsonObject { ["name"] = name, ["description"] = description, ["parameters"] = schema } };

	private static JsonObject Answer(ToolCall call, string text) => new() { ["role"] = "tool", ["tool_call_id"] = call.Id, ["content"] = text };

	private static string Words(string tool, JsonObject args) =>
		Mcp.CommandFor(tool, args) is { } w ? string.Join(' ', w.Where(x => x.Length > 0).Select(x => x.Contains(' ') ? $"\"{x}\"" : x)) : tool;

	/// <summary>A node as it was meant: some models send a list or an object written out as a string.</summary>
	private static JsonNode? Node(JsonNode? node)
	{
		if (node is not JsonValue v || !v.TryGetValue<string>(out var text) || text.TrimStart() is not ['[' or '{', ..]) return node;
		try { return JsonNode.Parse(text); }
		catch (System.Text.Json.JsonException) { return node; }
	}

	private static string Str(JsonNode? node) => node is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : "";

	/// <summary>
	/// The lines of a read that the step changed, before and after: a long read (every player's intensity)
	/// is cut for the model, and the row the step was about may be past the cut.
	/// </summary>
	internal static string Changed(string before, string after)
	{
		var was = before.Split('\n').Select(l => l.TrimEnd()).ToList();
		var now = after.Split('\n').Select(l => l.TrimEnd()).ToList();
		var gone = was.Where(l => l.Length > 0 && !now.Contains(l)).Take(40).ToList();
		var came = now.Where(l => l.Length > 0 && !was.Contains(l)).Take(40).ToList();
		if (gone.Count == 0 && came.Count == 0) return "The read is the same before and after the step.";
		return $"Lines of the read the step changed:\nbefore:\n{string.Join("\n", gone)}\nafter:\n{string.Join("\n", came)}";
	}

	private static string Cut(string text) => text.Length > MaxCheck ? text[..MaxCheck] + "\n…(cut)" : text;

	private static string FirstLines(string text) => string.Join(" ", text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Take(3)).Trim();
}
