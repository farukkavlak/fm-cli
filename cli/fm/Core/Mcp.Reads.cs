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
	/// Runs a call for the assistant and hands back what it printed. Refused, with nothing run, unless the
	/// tool is one of <see cref="ReadTools"/>, every argument is one it was shown, and the call only reads
	/// (<see cref="EffectOf"/>).
	/// </summary>
	public static async Task<(string Text, bool Ran)> RunRead(string tool, JsonObject arguments, Func<string[], Task<int>> run)
	{
		// Only the arguments it was shown: one left out of the schema (fm_offers' answer) is not taken, whatever its value.
		var shown = ReadTools().FirstOrDefault(t => t.Name == tool).Schema?["properties"] as JsonObject;
		if (shown == null || arguments.Any(a => !shown.ContainsKey(a.Key)) || EffectOf(tool, arguments) != Effect.Read)
			return ($"Refused: {tool} with these arguments can change the game. Only reads are allowed here; nothing was run.", false);
		var call = JsonDocument.Parse(new JsonObject { ["name"] = tool, ["arguments"] = arguments.DeepClone() }.ToJsonString()).RootElement;
		var captured = Ui.Captured;
		Ui.Captured = true;
		try
		{
			var result = await Call(call, run);
			var text = result?["content"]?[0]?["text"]?.GetValue<string>() ?? "(no output)";
			return (text, true);
		}
		finally { Ui.Captured = captured; }
	}
}
