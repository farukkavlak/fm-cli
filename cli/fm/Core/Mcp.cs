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
internal static partial class Mcp
{
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

			JsonNode? result = null;
			JsonNode? error = null;

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
						["instructions"] = "Plays Football Manager 26 through the running game. Start with fm_status; it says what the game is waiting for and which tool deals with it. Command output is in the language the game is set to (Turkish words in a Turkish game, English otherwise).",
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

		var text = InToolTerms(printed.ToString().TrimEnd(), tool.Name);
		return new JsonObject
		{
			["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text.Length > 0 ? text : "(no output)" }),
			["isError"] = code != 0,
		};
	}

	/// <summary>The command words a tool call stands for, as fm_* over MCP would run them; null for no such tool.</summary>
	public static string[]? CommandFor(string tool, JsonObject arguments) =>
		Tools.FirstOrDefault(t => t.Name == tool) is { } t ? [.. t.Words(JsonDocument.Parse(arguments.ToJsonString()).RootElement)] : null;

	/// <summary>
	/// Whether a command takes free text after its name ("player rinaldi", "search …"): some tool
	/// standing for it puts a text argument there as it is, not behind an --option. Worked out by
	/// running each tool's words with a marker in one text argument at a time. A command no tool
	/// stands for (help, lang, start…) counts as taking text.
	/// </summary>
	public static bool TakesText(string command)
	{
		const string marker = "§text§";
		var any = false;
		foreach (var tool in Tools)
		{
			var empty = JsonDocument.Parse("{}").RootElement;
			if (tool.Words(empty) is not [var name, ..] || name != command) continue;
			any = true;
			if (JsonNode.Parse(tool.Schema)?["properties"] is not JsonObject props) continue;
			foreach (var (arg, schema) in props)
			{
				if (schema?["type"]?.GetValue<string>() != "string" || schema["enum"] != null) continue;
				var words = tool.Words(JsonDocument.Parse(new JsonObject { [arg] = marker }.ToJsonString()).RootElement);
				var at = words.IndexOf(marker);
				if (at > 0 && !words[at - 1].StartsWith("--", StringComparison.Ordinal)) return true;
			}
		}
		return !any;
	}

	/// <summary>An argument's schema (type, description, choices) from the tool's input schema.</summary>
	public static JsonObject? ArgumentSchema(string tool, string argument) =>
		Tools.FirstOrDefault(t => t.Name == tool) is { } t ? JsonNode.Parse(t.Schema)?["properties"]?[argument] as JsonObject : null;

	private static string Str(JsonElement arguments, string name) =>
		arguments.TryGetProperty(name, out var value)
			? value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString()
			: "";

	private static string[] Opt(JsonElement arguments, string name) =>
		Str(arguments, name) is { Length: > 0 } value ? [value] : [];

	private static string[] Flag(JsonElement arguments, string name, string flag) =>
		arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True ? [flag] : [];

	/// <summary>A list argument as a repeated option: ["a", "b"] → --in a --in b. A lone string counts as one.</summary>
	private static string[] Each(JsonElement arguments, string name, string flag)
	{
		if (!arguments.TryGetProperty(name, out var value)) return [];
		var items = value.ValueKind == JsonValueKind.Array ? value.EnumerateArray().Select(v => v.GetString() ?? "") : [value.GetString() ?? ""];
		return items.Where(i => i.Length > 0).SelectMany(i => new[] { flag, i }).ToArray();
	}

	/// <summary>An argument as an option with its value: focus → --focus <value>.</summary>
	private static string[] Opt2(JsonElement a, string name, string flag) => Str(a, name) is { Length: > 0 } v ? [flag, v] : [];
}
