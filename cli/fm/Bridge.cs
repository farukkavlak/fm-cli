using System.Text.Json;

namespace FmCli;

/// <summary>Talks to the plugin running inside the game.</summary>
internal sealed class Bridge
{
	private const string Base = "http://127.0.0.1:7777/";

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

	public async Task<string> Get(string route, params (string Key, string Value)[] query)
	{
		var url = Base + route;
		if (query.Length > 0)
			url += "?" + string.Join("&", query.Select(q =>
				$"{q.Key}={Uri.EscapeDataString(q.Value)}"));

		try
		{
			return await _http.GetStringAsync(url);
		}
		catch (HttpRequestException)
		{
			throw new FmUnavailable();
		}
		catch (TaskCanceledException)
		{
			// A stalled request is not a missing game: the bridge runs on the game's main
			// thread, so a busy frame looks exactly like a timeout.
			throw new FmBusy();
		}
	}

	public async Task<JsonElement> GetJson(string route, params (string, string)[] query)
	{
		var body = await Get(route, query);
		try
		{
			return JsonDocument.Parse(body).RootElement.Clone();
		}
		catch (JsonException)
		{
			// The bridge answers errors as plain text, so an unparseable body is a message.
			throw new FmError(body.Trim());
		}
	}

	/// <summary>True once a save is loaded, rather than merely the game being up.</summary>
	public async Task<bool> InGame()
	{
		var health = await Get("health");
		if (!health.Contains("ok")) return false;

		var digits = new string(health.Where(char.IsDigit).ToArray());
		if (int.TryParse(digits, out var nodes) && nodes > 1000) return true;

		// A match runs in a scene of its own with only a few hundred nodes bound, far fewer
		// than the main menu has; what the menu lacks is a game date.
		return (await Get("state")).Contains("\"Date\"");
	}
}

internal sealed class FmUnavailable : Exception;

internal sealed class FmBusy : Exception;

internal sealed class FmError(string text) : Exception(text);
