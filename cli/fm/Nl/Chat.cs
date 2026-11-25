using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace FmCli.Nl;

/// <summary>
/// A chat model with tools, as the assistant (`ask`) uses one. One interface for any provider: the
/// messages and tools are kept in the OpenAI form, and a provider of another form turns them into its own.
/// </summary>
internal interface IChatModel
{
	string Name { get; }

	/// <summary>The model's next message for <paramref name="messages"/>, offered <paramref name="tools"/>; <paramref name="seconds"/> bounds the wait.</summary>
	Task<ChatReply> Next(JsonArray messages, JsonArray tools, double seconds);
}

/// <summary>A tool the model asked for: its call id, name and arguments.</summary>
internal sealed record ToolCall(string Id, string Name, JsonObject Args);

/// <summary>The model's message: text, tool calls, or why there is none (<see cref="Error"/>). <see cref="Message"/> goes back into the talk as it came.</summary>
internal sealed record ChatReply(string Text, List<ToolCall> Calls, JsonObject? Message, string? Error, int Tokens);

internal static class ChatModels
{
	/// <summary>The model set up with `ai set`; null when none is.</summary>
	public static IChatModel? Current() => Cloud.Current() is { } s ? new OpenAiChat(s) : null;
}

/// <summary>Any provider with OpenAI-style chat completions and tools (OpenCode, OpenRouter, OpenAI, a local server).</summary>
internal sealed class OpenAiChat(Cloud.Settings settings) : IChatModel
{
	private static readonly string Session = Guid.NewGuid().ToString();

	public string Name => settings.Model;

	public async Task<ChatReply> Next(JsonArray messages, JsonArray tools, double seconds)
	{
		var body = new JsonObject
		{
			["model"] = settings.Model,
			["temperature"] = 0,
			["max_tokens"] = 1200,
			["messages"] = messages.DeepClone(),
		};
		if (tools.Count > 0) body["tools"] = tools.DeepClone();
		try
		{
			using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(seconds) };
			using var request = new HttpRequestMessage(HttpMethod.Post, settings.Url.TrimEnd('/') + "/v1/chat/completions")
			{
				Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
			};
			if (settings.Key.Length > 0) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Key);
			foreach (var (name, value) in settings.Headers ?? []) request.Headers.TryAddWithoutValidation(name, value.Replace("{session}", Session));
			using var response = await http.SendAsync(request);
			var raw = await response.Content.ReadAsStringAsync();
			if (!response.IsSuccessStatusCode) return Failed($"HTTP {(int)response.StatusCode}");
			var reply = JsonNode.Parse(raw);
			if (reply?["choices"]?[0]?["message"] is not JsonObject message) return Failed("no message");
			var calls = new List<ToolCall>();
			foreach (var c in message["tool_calls"] as JsonArray ?? [])
			{
				var id = c?["id"]?.GetValue<string>() ?? "";
				var name = c?["function"]?["name"]?.GetValue<string>() ?? "";
				JsonObject args;
				try { args = JsonNode.Parse(c?["function"]?["arguments"]?.GetValue<string>() is { Length: > 0 } a ? a : "{}") as JsonObject ?? []; }
				catch (System.Text.Json.JsonException) { args = []; }
				calls.Add(new ToolCall(id, name, args));
			}
			var text = message["content"] is JsonValue v && v.TryGetValue<string>(out var t) ? t : "";
			var tokens = reply["usage"]?["total_tokens"]?.GetValue<int>() ?? 0;
			return new ChatReply(text.Trim(), calls, (JsonObject)message.DeepClone(), null, tokens);
		}
		catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
		{
			return Failed(e is TaskCanceledException ? "timed out" : e.GetType().Name);
		}
	}

	private static ChatReply Failed(string why) => new("", [], null, why, 0);
}
