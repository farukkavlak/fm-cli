using System.Text.Json;
using System.Text.Json.Nodes;

namespace FmCli;

/// <summary>The tools the assistant (`ask`) may call: reads only, each checked again when it is called.</summary>
internal static partial class Mcp
{
	/// <summary>
	/// Reads that are the screen by hand, the setup or files rather than the game (pages, buttons, a raw data
	/// path, the doctor, the saves): not offered to the assistant.
	/// </summary>
	private static readonly HashSet<string> NotForTheAssistant =
		["fm_pages", "fm_open", "fm_buttons", "fm_dropdowns", "fm_query", "fm_doctor", "fm_saves", "fm_settings", "fm_diff", "fm_dialog", "fm_tour"];

	/// <summary>
	/// Every tool a call of which can only read, as a model is shown it: the tools marked Read, and those
	/// whose effect comes only from some arguments, with those arguments left out of the schema.
	/// </summary>
	public static List<(string Name, string Description, JsonObject Schema)> ReadTools()
	{
		var tools = new List<(string, string, JsonObject)>();
		foreach (var t in Tools)
		{
			if (NotForTheAssistant.Contains(t.Name)) continue;
			if (t.Impact.Effect != Effect.Read && t.Impact.When.Length == 0) continue;
			var schema = (JsonObject)JsonNode.Parse(t.Schema)!;
			if (t.Impact.Effect != Effect.Read && schema["properties"] is JsonObject props)
			{
				foreach (var name in t.Impact.When.Select(w => w.Split('=', 2)[0]).Distinct()) props.Remove(name);
				// "dry" only matters beside an act
				props.Remove("dry");
				if (schema["required"] is JsonArray required)
					foreach (var r in required.Select(r => r!.GetValue<string>()).Where(r => !props.ContainsKey(r)).ToList())
						required.Remove(required.First(x => x!.GetValue<string>() == r));
			}
			tools.Add((t.Name, t.Description, schema));
		}
		return tools;
	}

	/// <summary>
	/// The commands that change or move the game, as words a user types and the first sentence of what each
	/// does: the assistant names one when asked to do something it cannot.
	/// </summary>
	public static List<(string Words, string About)> ActCommands()
	{
		var acts = new List<(string, string)>();
		foreach (var t in Tools)
		{
			if (t.Impact.Effect == Effect.Read || NotForTheAssistant.Contains(t.Name)) continue;
			var words = string.Join(" ", t.Words(JsonDocument.Parse("{}").RootElement).TakeWhile(w => w.Length > 0 && !w.StartsWith('-')));
			var about = t.Description.Split(". ", 2)[0].TrimEnd('.');
			if (words.Length > 0) acts.Add((words, about));
		}
		return acts;
	}

	/// <summary>
	/// Tools a plan (`plan`) may not hold as a step: besides <see cref="NotForTheAssistant"/>, what moves the
	/// date on or opens a talk (a plan sets things up; time and talks stay the user's), the save, the
	/// session and the job, and the screen by hand.
	/// </summary>
	private static readonly HashSet<string> NotInPlans =
		["fm_next", "fm_continue", "fm_match", "fm_holiday", "fm_meeting", "fm_talk", "fm_draw", "fm_load", "fm_stop", "fm_save",
		 "fm_resign", "fm_jobs", "fm_select", "fm_type", "fm_press"];

	/// <summary>The tools a plan's step may call, as a model is shown them: those that can change or move the game, with their whole schema.</summary>
	public static List<(string Name, string Description, JsonObject Schema)> PlanTools() =>
		[.. Tools.Where(t => t.Impact.Effect != Effect.Read && !NotForTheAssistant.Contains(t.Name) && !NotInPlans.Contains(t.Name))
			.Select(t => (t.Name, t.Description, (JsonObject)JsonNode.Parse(t.Schema)!))];

	/// <summary>Why a plan's step cannot be one; null when it can: a tool of <see cref="PlanTools"/>, arguments of its schema (never `dry`), and a call that acts.</summary>
	public static string? NotAStep(string tool, JsonObject arguments)
	{
		if (PlanTools().FirstOrDefault(t => t.Name == tool).Schema is not { } schema) return $"{tool} cannot be a step of a plan";
		var props = schema["properties"] as JsonObject ?? [];
		if (arguments.FirstOrDefault(a => a.Key == "dry" || !props.ContainsKey(a.Key)) is { Key: { } bad }) return $"{tool} has no argument {bad} here";
		foreach (var r in (schema["required"] as JsonArray ?? []).Select(r => r!.GetValue<string>()))
			if (arguments[r] is null || arguments[r]!.ToString().Length == 0) return $"{tool} needs {r}";
		if (EffectOf(tool, arguments) == Effect.Read) return $"{tool} with these arguments only reads: read it now, it is not a step";
		return null;
	}

	/// <summary>Whether a call is one the assistant may read with: a tool of <see cref="ReadTools"/>, arguments it was shown, a call that only reads.</summary>
	public static bool Readable(string tool, JsonObject arguments) =>
		ReadTools().FirstOrDefault(t => t.Name == tool).Schema?["properties"] is JsonObject shown
		&& arguments.All(a => shown.ContainsKey(a.Key)) && EffectOf(tool, arguments) == Effect.Read;

	/// <summary>
	/// Runs a call for the assistant and hands back what it printed. Refused, with nothing run, unless the
	/// tool is one of <see cref="ReadTools"/>, every argument is one it was shown, and the call only reads
	/// (<see cref="EffectOf"/>).
	/// </summary>
	public static async Task<(string Text, bool Ran)> RunRead(string tool, JsonObject arguments, Func<string[], Task<int>> run)
	{
		// Only the arguments it was shown: one left out of the schema (fm_offers' answer) is not taken, whatever its value.
		if (!Readable(tool, arguments))
			return ($"Refused: {tool} with these arguments can change the game. Only reads are allowed here; nothing was run.", false);
		var (text, _) = await Captured(tool, arguments, run);
		return (text, true);
	}

	/// <summary>
	/// Runs a plan's step, checked again (<see cref="NotAStep"/>): what it printed and whether it failed.
	/// The caller has already had the user's yes for it.
	/// </summary>
	public static async Task<(string Text, bool Failed)> RunStep(string tool, JsonObject arguments, Func<string[], Task<int>> run) =>
		NotAStep(tool, arguments) is { } why ? ($"Not run: {why}.", true) : await Captured(tool, arguments, run);

	private static async Task<(string Text, bool Failed)> Captured(string tool, JsonObject arguments, Func<string[], Task<int>> run)
	{
		var call = JsonDocument.Parse(new JsonObject { ["name"] = tool, ["arguments"] = arguments.DeepClone() }.ToJsonString()).RootElement;
		var captured = Ui.Captured;
		Ui.Captured = true;
		try
		{
			var result = await Call(call, run);
			return (result?["content"]?[0]?["text"]?.GetValue<string>() ?? "(no output)", result?["isError"]?.GetValue<bool>() ?? true);
		}
		finally { Ui.Captured = captured; }
	}
}
