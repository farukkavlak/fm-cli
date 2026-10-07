using System.Text.Json;

namespace FmCli;

/// <summary>Talks to the plugin running inside the game.</summary>
internal sealed class Bridge
{
	private const string Base = "http://127.0.0.1:7777/";

	private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(120) };

	// FM_TRACE=1 prints every call and how long it took, to see where a command's time goes.
	private static readonly bool Trace = Environment.GetEnvironmentVariable("FM_TRACE") == "1";
	private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

	public async Task<string> Get(string route, params (string Key, string Value)[] query)
	{
		var url = Base + route;
		if (query.Length > 0)
			url += "?" + string.Join("&", query.Select(q =>
				$"{q.Key}={Uri.EscapeDataString(q.Value)}"));

		try
		{
			var started = Clock.ElapsedMilliseconds;
			var body = await _http.GetStringAsync(url);
			if (Trace) Console.Error.WriteLine($"[{started / 1000.0,6:0.00}s +{Clock.ElapsedMilliseconds - started,5}ms] {route} {Uri.UnescapeDataString(url[(Base.Length + route.Length)..])}");
			return body;
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

	/// <summary>
	/// Properties read straight from the game's data, with no screen opened for them: of the
	/// reference at a path, or of every entry of the list there. A property is bound by the
	/// first read and arrives on a later one, so this asks again while anything is missing.
	/// </summary>
	public async Task<JsonElement> Pull(string path, string props, bool list = false)
	{
		var wanted = props.Split(',').Length;
		JsonElement data = default;
		for (var attempt = 0; attempt < 6; attempt++)
		{
			data = await GetJson("pull", ("path", path), ("props", props), ("list", list ? "1" : "0"));
			var records = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList() : [data];
			var complete = records.All(r => r.EnumerateObject().Count(p => p.Name != "Index") >= wanted);
			if (complete && (attempt > 0 || records.Count > 0)) return data;
			await Task.Delay(TimeSpan.FromMilliseconds(250));
		}
		return data;
	}

	/// <summary>A list of plain numbers at a path, bound first if need be: recent form as points per match.</summary>
	public async Task<List<int>> Numbers(string path)
	{
		await Get("pathbind", ("path", path));
		for (var attempt = 0; attempt < 4; attempt++)
		{
			var listed = await Get("pathlist", ("path", path));
			if (!listed.StartsWith("error", StringComparison.Ordinal))
				return listed.Split('\n')
					.Select(line => line.IndexOf(") ", StringComparison.Ordinal) is var at and >= 0 && int.TryParse(line[(at + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var n) ? (int?)n : null)
					.Where(n => n != null)
					.Select(n => n!.Value)
					.ToList();
			await Task.Delay(250);
		}
		return [];
	}

	/// <summary>The manager's own team: its name and short name, read without a screen.</summary>
	public async Task<(string Name, string Short)> OwnTeam()
	{
		var team = await Pull("Human.Team", "Name,ShortName");
		return (Text(team, "Name"), Text(team, "ShortName"));

		static string Text(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
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
