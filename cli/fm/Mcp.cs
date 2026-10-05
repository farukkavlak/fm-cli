using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FmCli;

/// <summary>
/// The commands as tools for a model, over the Model Context Protocol's stdio transport:
/// one JSON-RPC message per line in, one per line out.
///
/// A tool is a command and nothing more. Its arguments are turned into the words a user would
/// have typed, the command runs as it would in a terminal, and what it printed is the result.
/// So anything that works in the CLI works here, and there is one place where behaviour lives.
/// </summary>
internal static class Mcp
{
	private sealed record Tool(string Name, string Description, string Schema, Func<JsonElement, List<string>> Words);

	private const string None = """{"type":"object","properties":{}}""";

	private static readonly Tool[] Tools =
	[
		new("fm_status", "The in-game date and time, and what the game is waiting for (free to continue, a message to answer, a press conference, match day, or the score if a match is on). Call this first.",
			None, _ => ["status"]),
		new("fm_next", "Press Continue and wait for the game to settle; reports the new date and the messages that arrived. Stops when something needs doing first and says what. `times` repeats it.",
			"""{"type":"object","properties":{"times":{"type":"integer","minimum":1,"maximum":30,"description":"How many times to continue (default 1); stops early when blocked."}}}""",
			a => ["next", .. Opt(a, "times")]),
		new("fm_inbox", "List the inbox with message numbers; flags which messages are unread, need an answer (these block Continue), or can optionally be answered.",
			None, _ => ["inbox"]),
		new("fm_reply", "Open a message and list its answers, or give one. Without `answer` it only lists. Some answers open a tour (use fm_tour) or a conversation (use fm_talk) instead of closing the message.",
			"""{"type":"object","required":["message"],"properties":{"message":{"type":"string","description":"Message number from fm_inbox, or a piece of its subject."},"answer":{"type":"string","description":"Answer number from the listing, or a piece of its text."},"dry":{"type":"boolean"}}}""",
			a => ["reply", Str(a, "message"), .. Opt(a, "answer"), .. Flag(a, "dry", "--dry")]),
		new("fm_talk", "The open conversation (squad meeting, press conference, team talk): what was said, how the listeners react, and the numbered lines that can be said. With `say`, says one (number or a piece of its text) and returns the reply and the next options. say=\"leave\" walks out.",
			"""{"type":"object","properties":{"say":{"type":"string","description":"Option number, a piece of its text, or \"leave\"."}}}""",
			a => ["talk", .. Opt(a, "say")]),
		new("fm_tour", "Walk the open introduction tour to its end, printing what the guide says at each step; `skip` skips it if the tour allows.",
			"""{"type":"object","properties":{"skip":{"type":"boolean"}}}""",
			a => ["tour", .. Flag(a, "skip", "--skip")]),
		new("fm_dialog", "Show the open confirmation dialog, or answer it with one of its buttons.",
			"""{"type":"object","properties":{"answer":{"type":"string","description":"Button number or text, e.g. \"Yes\"."}}}""",
			a => ["dialog", .. Opt(a, "answer")]),
		new("fm_squad", "The first-team squad. Default view: position, age, status, wage, value. view=fitness: condition and match sharpness; view=morale: morale, hierarchy, social group. Any other `view` is one of the squad table's own views by name (Contract, Injuries, Playing Time, Transfer, Development…; view=list prints them) and prints whatever columns it carries.",
			"""{"type":"object","properties":{"view":{"type":"string"}}}""",
			a => ["squad", .. Str(a, "view") switch { "" or "general" => Array.Empty<string>(), "fitness" => ["--fitness"], "morale" => ["--morale"], "list" => ["--views"], var other => ["--view", other] }]),
		new("fm_player", "A player's page: age, nation, positions, value, wage, contract end, condition, personality, traits and all attributes. Only players of your own squad.",
			"""{"type":"object","required":["name"],"properties":{"name":{"type":"string","description":"The player's name or a unique piece of it."}}}""",
			a => ["player", Str(a, "name")]),
		new("fm_players", "The player database (every player the game knows), as far as the page has rows on screen — about twenty. `filter` applies one of the game's quick filters (Transfer Listed, Loan listed, Contract Expiring, Unhappy, Goalkeepers…) and it stays applied; `view` chooses which columns the rows carry (Overview, General Info, Contract, Transfer…). list=filters or list=views prints the choices.",
			"""{"type":"object","properties":{"filter":{"type":"string"},"view":{"type":"string"},"list":{"type":"string","enum":["filters","views"]},"clear":{"type":"boolean","description":"Remove the filter in force first."}}}""",
			a => ["players", .. Str(a, "list") switch { "filters" => new[] { "--filters" }, "views" => ["--views"], _ => [] }, .. Flag(a, "clear", "--clear"),
				.. Str(a, "filter") is { Length: > 0 } filter ? new[] { "--filter", filter } : [],
				.. Str(a, "view") is { Length: > 0 } view ? new[] { "--view", view } : []]),
		new("fm_table", "The league table.", None, _ => ["table"]),
		new("fm_fixtures", "The team's last played and upcoming fixtures: date, time, venue, opponent, competition, result.", None, _ => ["fixtures"]),
		new("fm_tactic", "The team's tactic: style, mentality, and for each of the eleven slots the position and role with and without the ball and the player picked.",
			None, _ => ["tactic"]),
		new("fm_tactic_set", "Show or change the tactic's mentality or style. Without `value` it lists the choices and marks the current one.",
			"""{"type":"object","required":["setting"],"properties":{"setting":{"type":"string","enum":["mentality","style"]},"value":{"type":"string","description":"Choice number, name, or a unique piece of the name."},"dry":{"type":"boolean"}}}""",
			a => ["tactic", Str(a, "setting"), .. Opt(a, "value"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_role", "Show or change the role of one slot of the tactic. `position` is the slot as fm_tactic prints it (GK, DR, DCR, DML, ST…). Without `role` it lists the roles on offer and marks the current one. `without_ball` picks the out-of-possession role instead.",
			"""{"type":"object","required":["position"],"properties":{"position":{"type":"string"},"role":{"type":"string","description":"Role number, name, or a unique piece of the name."},"without_ball":{"type":"boolean"},"dry":{"type":"boolean"}}}""",
			a => ["tactic", "role", Str(a, "position"), .. Opt(a, "role"), .. Flag(a, "without_ball", "--oop"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_player", "Put a substitute into a slot of the starting eleven; whoever was there goes to the bench. `position` is the slot as fm_tactic prints it; `player` is a name from fm_tactic's bench line.",
			"""{"type":"object","required":["position","player"],"properties":{"position":{"type":"string"},"player":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["tactic", "player", Str(a, "position"), Str(a, "player"), .. Flag(a, "dry", "--dry")]),
		new("fm_tactic_pick", "Let the game pick the starting eleven (Quick Pick).", None, _ => ["tactic", "pick"]),
		new("fm_tactic_new", "Create the team's tactic through the game's wizard (only while the team has none): a style, then a formation in possession, then one out of possession. Leave a step out to get its choices listed; the wizard stays where it is.",
			"""{"type":"object","properties":{"style":{"type":"string"},"in_possession":{"type":"string"},"out_of_possession":{"type":"string"}}}""",
			a => ["tactic", "new", .. Opt(a, "style"), .. Opt(a, "in_possession"), .. Opt(a, "out_of_possession")]),
		new("fm_setpieces_new", "Set up set piece routines through the game's six-question wizard (only while none are set up). `answers` gives an option number per question in order; a question left out keeps the game's own choice.",
			"""{"type":"object","properties":{"answers":{"type":"array","items":{"type":"integer","minimum":1}}}}""",
			a => ["setpieces", "new", .. a.TryGetProperty("answers", out var list) && list.ValueKind == JsonValueKind.Array ? list.EnumerateArray().Select(n => n.ToString()) : []]),
		new("fm_match", "Play match day once fm_status says it is match day. mode=instant leaves the match to the staff and returns the result in seconds. mode=play plays it out and reports goals, half time and the result — this takes about six minutes. mode=talk is play, saying the first line on offer at each team talk. If the game stops for a decision (an injury), it says so and returns.",
			"""{"type":"object","properties":{"mode":{"type":"string","enum":["instant","play","talk"],"description":"Default instant."}}}""",
			a => ["match", .. Str(a, "mode") switch { "play" => Array.Empty<string>(), "talk" => ["--talk"], _ => ["--instant"] }]),
		new("fm_sub", "During a match: without arguments, who is on the pitch (mood, condition) and who is on the bench; with both, take `off` off for `on`.",
			"""{"type":"object","properties":{"off":{"type":"string"},"on":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["sub", .. Opt(a, "off"), .. Opt(a, "on"), .. Flag(a, "dry", "--dry")]),
		new("fm_save", "Save the game over its current file. This cannot be undone; `confirm` must be true.",
			"""{"type":"object","required":["confirm"],"properties":{"confirm":{"type":"boolean"}}}""",
			a => ["save", .. Flag(a, "confirm", "--yes")]),
		new("fm_pages", "The game's pages by tab, for fm_open. `filter` narrows the list.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["pages", .. Opt(a, "filter")]),
		new("fm_open", "Open one of the game's pages by name (see fm_pages); `tab` tells apart pages that share a name.",
			"""{"type":"object","required":["page"],"properties":{"page":{"type":"string"},"tab":{"type":"string"}}}""",
			a => ["open", Str(a, "page"), .. Str(a, "tab") is { Length: > 0 } tab ? new[] { "--tab", tab } : []]),
		new("fm_buttons", "The labelled controls on the current screen and the event each would send. For exploring a screen no dedicated tool covers.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["buttons", .. Opt(a, "filter")]),
		new("fm_press", "Press a control on the current screen by its label (see fm_buttons); `name` tells apart controls that share a label.",
			"""{"type":"object","required":["label"],"properties":{"label":{"type":"string"},"name":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["press", Str(a, "label"), .. Str(a, "name") is { Length: > 0 } name ? new[] { "--name", name } : [], .. Flag(a, "dry", "--dry")]),
		new("fm_dropdowns", "The dropdowns and radio groups on the current screen with their choices.",
			"""{"type":"object","properties":{"filter":{"type":"string"}}}""",
			a => ["dropdowns", .. Opt(a, "filter")]),
		new("fm_select", "Choose from a dropdown or radio group on the current screen (see fm_dropdowns).",
			"""{"type":"object","required":["list","choice"],"properties":{"list":{"type":"string"},"choice":{"type":"string"},"dry":{"type":"boolean"}}}""",
			a => ["select", Str(a, "list"), Str(a, "choice"), .. Flag(a, "dry", "--dry")]),
	];

	public static async Task<int> Serve(Func<string[], Task<int>> run)
	{
		// Standard output is the protocol's channel, so nothing but messages may reach it.
		var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
		var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

		while (await input.ReadLineAsync() is { } line)
		{
			if (string.IsNullOrWhiteSpace(line)) continue;

			JsonElement message;
			try { message = JsonDocument.Parse(line).RootElement; }
			catch (JsonException)
			{
				await output.WriteLineAsync("""{"jsonrpc":"2.0","id":null,"error":{"code":-32700,"message":"Parse error"}}""");
				continue;
			}

			// A message without an id is a notification and is never answered.
			if (!message.TryGetProperty("id", out var id)) continue;

			var method = message.TryGetProperty("method", out var m) ? m.GetString() : null;
			var parameters = message.TryGetProperty("params", out var p) ? p : default;

			JsonNode result = null;
			JsonNode error = null;

			switch (method)
			{
				case "initialize":
					var version = parameters.ValueKind == JsonValueKind.Object && parameters.TryGetProperty("protocolVersion", out var asked)
						? asked.GetString()
						: "2024-11-05";
					result = new JsonObject
					{
						["protocolVersion"] = version,
						["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
						["serverInfo"] = new JsonObject { ["name"] = "fm-cli", ["version"] = "0.1.0" },
						["instructions"] = "Plays Football Manager 26 through the running game. Start with fm_status; it says what the game is waiting for and which tool deals with it. Command output is in Turkish.",
					};
					break;

				case "ping":
					result = new JsonObject();
					break;

				case "tools/list":
					var listed = new JsonArray();
					foreach (var tool in Tools)
						listed.Add(new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = JsonNode.Parse(tool.Schema) });
					result = new JsonObject { ["tools"] = listed };
					break;

				case "tools/call":
					result = await Call(parameters, run);
					if (result == null) error = new JsonObject { ["code"] = -32602, ["message"] = "Unknown tool" };
					break;

				default:
					error = new JsonObject { ["code"] = -32601, ["message"] = $"Method not found: {method}" };
					break;
			}

			var reply = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()) };
			if (error != null) reply["error"] = error;
			else reply["result"] = result;

			await output.WriteLineAsync(reply.ToJsonString());
		}

		return 0;
	}

	/// <summary>Runs the command a tool stands for and hands back what it printed.</summary>
	private static async Task<JsonNode> Call(JsonElement parameters, Func<string[], Task<int>> run)
	{
		if (parameters.ValueKind != JsonValueKind.Object) return null;

		var name = parameters.TryGetProperty("name", out var n) ? n.GetString() : null;
		var tool = Tools.FirstOrDefault(t => t.Name == name);
		if (tool == null) return null;

		var arguments = parameters.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
			? a
			: JsonDocument.Parse("{}").RootElement;

		var printed = new StringWriter();
		var (stdout, stderr) = (Console.Out, Console.Error);
		int code;
		try
		{
			Console.SetOut(printed);
			Console.SetError(printed);
			code = await run(tool.Words(arguments).ToArray());
		}
		catch (Exception ex)
		{
			printed.WriteLine(ex.Message);
			code = 1;
		}
		finally
		{
			Console.SetOut(stdout);
			Console.SetError(stderr);
		}

		var text = printed.ToString().TrimEnd();
		return new JsonObject
		{
			["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : "(çıktı yok)" }),
			["isError"] = code != 0,
		};
	}

	private static string Str(JsonElement arguments, string name) =>
		arguments.TryGetProperty(name, out var value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
			: "";

	private static string[] Opt(JsonElement arguments, string name) =>
		Str(arguments, name) is { Length: > 0 } value ? [value] : [];

	private static string[] Flag(JsonElement arguments, string name, string flag) =>
		arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True ? [flag] : [];
}
