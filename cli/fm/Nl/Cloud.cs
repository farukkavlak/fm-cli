using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// The cloud model of smart mode (T207): asked only when the router is not sure, and only when
/// the user set one up (`fm ai`). Any provider with OpenAI-style chat completions: a URL, a
/// key and a model. The model proposes one tool and its arguments; our code checks them.
/// </summary>
internal static class Cloud
{
	/// <summary>
	/// A provider: its URL, key and model, and the headers some providers want besides (OpenCode:
	/// x-opencode-session). "{session}" in a header's value is one id for this run.
	/// </summary>
	public sealed record Settings(string Url, string Key, string Model, Dictionary<string, string>? Headers = null);

	private static readonly string Session = Guid.NewGuid().ToString();

	public sealed record Proposal(string? Tool, JsonObject Args, double Seconds);

	private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	public static string File =>
		Environment.GetEnvironmentVariable("FM_AI_FILE") is { Length: > 0 } other ? other
		: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "ai.json");

	/// <summary>The provider set up, FM_AI_URL / FM_AI_KEY / FM_AI_MODEL first; null when none is.</summary>
	public static Settings? Current()
	{
		Settings? saved = null;
		try { saved = JsonSerializer.Deserialize<Settings>(System.IO.File.ReadAllText(File), Options); }
		catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { }
		string Pick(string env, string? kept) => Environment.GetEnvironmentVariable(env) is { Length: > 0 } v ? v : kept ?? "";
		// FM_AI_HEADERS: "Name: value; Other: value"
		var headers = Environment.GetEnvironmentVariable("FM_AI_HEADERS") is { Length: > 0 } h
			? h.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(p => p.Split(':', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim())
			: saved?.Headers;
		var s = new Settings(Pick("FM_AI_URL", saved?.Url), Pick("FM_AI_KEY", saved?.Key), Pick("FM_AI_MODEL", saved?.Model), headers);
		return s.Url.Length > 0 && s.Model.Length > 0 ? s : null;
	}

	public static void Save(Settings? settings)
	{
		if (settings == null)
		{
			System.IO.File.Delete(File);
			return;
		}
		Directory.CreateDirectory(Path.GetDirectoryName(File)!);
		System.IO.File.WriteAllText(File, JsonSerializer.Serialize(settings, Options));
		// the key is the user's: only they read the file
		if (!OperatingSystem.IsWindows()) System.IO.File.SetUnixFileMode(File, UnixFileMode.UserRead | UnixFileMode.UserWrite);
	}

	private const string Prompt = """
		You pick the one tool that does what a Football Manager player typed (Turkish or English, often with typos and no Turkish letters).
		Answer with JSON only: {"tool": "<name>", "args": {...}} or {"tool": null} when no tool does it.
		Put in args only what the sentence says, in its own words (a player's name as typed, a number as typed). Leave out anything it does not say.
		Never set confirm, send, sign or yes.
		""";

	/// <summary>
	/// The tool the model picks among <paramref name="tools"/> (name, description, schema) for the
	/// sentence, and the arguments it read; a null tool when it picks none, fails or answers
	/// with a tool not offered. <paramref name="seconds"/> bounds the wait.
	/// </summary>
	public static async Task<Proposal> Propose(Settings s, string sentence, IEnumerable<(string Name, string Description, string Schema)> tools, double seconds = 20)
	{
		var started = Environment.TickCount64;
		var offered = tools.ToList();
		var cards = string.Join("\n", offered.Select(t => $"- {t.Name}: {t.Description}\n  args: {t.Schema}"));
		var body = new JsonObject
		{
			["model"] = s.Model,
			["temperature"] = 0,
			["max_tokens"] = 300,
			["messages"] = new JsonArray(
				new JsonObject { ["role"] = "system", ["content"] = Prompt },
				new JsonObject { ["role"] = "user", ["content"] = $"Tools:\n{cards}\n\nTyped: {sentence}" }),
		};
		Proposal None() => new(null, [], (Environment.TickCount64 - started) / 1000.0);
		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
			using var request = new HttpRequestMessage(HttpMethod.Post, s.Url.TrimEnd('/') + "/v1/chat/completions")
			{
				Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
			};
			if (s.Key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", s.Key);
			foreach (var (name, value) in s.Headers ?? []) request.Headers.TryAddWithoutValidation(name, value.Replace("{session}", Session));
			using var response = await http.SendAsync(request);
			if (!response.IsSuccessStatusCode) return None();
			var answer = JsonNode.Parse(await response.Content.ReadAsStringAsync())?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
			return Read(answer, offered.Select(t => t.Name).ToHashSet()) is { } p ? p with { Seconds = (Environment.TickCount64 - started) / 1000.0 } : None();
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException) { return None(); }
	}

	/// <summary>The proposal in a model's answer: JSON somewhere in it, with a tool that was offered.</summary>
	internal static Proposal? Read(string answer, HashSet<string> offered)
	{
		var start = answer.IndexOf('{');
		var end = answer.LastIndexOf('}');
		if (start < 0 || end <= start) return null;
		try
		{
			if (JsonNode.Parse(answer[start..(end + 1)]) is not JsonObject o) return null;
			var tool = o["tool"] is JsonValue v && v.TryGetValue<string>(out var name) ? name : null;
			if (tool != null && !offered.Contains(tool)) return null;
			var args = o["args"] as JsonObject ?? [];
			return new Proposal(tool, (JsonObject)args.DeepClone(), 0);
		}
		catch (JsonException) { return null; }
	}

	/// <summary>Arguments a model may never give: the yes is the user's.</summary>
	private static readonly HashSet<string> Never = ["confirm", "send", "sign", "yes"];

	/// <summary>
	/// The model's arguments put to the call our reader filled: what the reader read stays; a value
	/// the reader left out is taken when the sentence says it (its words or number are in the
	/// sentence), or when it is a choice (a fixed list, a yes/no) and the call with it still only
	/// reads. A name or a number the sentence does not give is never taken: it stays asked when the
	/// call needs it.
	/// </summary>
	public static void Merge(Router.Call call, JsonObject proposed, string sentence)
	{
		var said = new Text(sentence).Words;
		foreach (var (name, value) in proposed)
		{
			if (Never.Contains(name) || call.Args.ContainsKey(name) || value is null) continue;
			if (Mcp.ArgumentSchema(call.Tool, name) is not { } schema || !Fits(value, schema)) continue;
			var with = (JsonObject)call.Args.DeepClone();
			with[name] = value.DeepClone();
			var choice = schema["enum"] != null || schema["type"]?.GetValue<string>() == "boolean";
			if (!Said(value, said) && !(choice && Mcp.EffectOf(call.Tool, with) == Mcp.Effect.Read)) continue;
			call.Args[name] = value.DeepClone();
			call.Ask.Remove(name);
		}
	}

	/// <summary>Whether a value is in the sentence: each of its words fits a word typed (a typo or an ending allowed).</summary>
	internal static bool Said(JsonNode value, string[] said)
	{
		if (value is not JsonValue v) return false;
		var text = v.TryGetValue<string>(out var s) ? s : v.ToJsonString();
		var words = new Text(text).Words;
		return words.Length > 0 && words.All(w => said.Any(t => t == w || NameFit.Fit(t, w) > 0 || (w.Any(char.IsDigit) && t.Contains(w, StringComparison.Ordinal))));
	}

	private static bool Fits(JsonNode value, JsonObject schema)
	{
		var type = schema["type"]?.GetValue<string>();
		if (schema["enum"] is JsonArray e) return value is JsonValue ev && ev.TryGetValue<string>(out var s) && e.Any(x => x?.GetValue<string>() == s);
		return type switch
		{
			"boolean" => value is JsonValue b && b.TryGetValue<bool>(out _),
			"integer" or "number" => value is JsonValue n && (n.TryGetValue<double>(out _) || (n.TryGetValue<string>(out var t) && double.TryParse(t, out _))),
			"string" => value is JsonValue str && str.TryGetValue<string>(out _),
			_ => false,
		};
	}
}
