using System.Text.Json;

namespace FmCli;

/// <summary>The command was stopped by Ctrl-C in a session; the game goes on with what it was doing.</summary>
internal sealed class Interrupted : Exception;

/// <summary>Talks to the plugin running inside the game.</summary>
internal sealed class Bridge
{
	// The plugin writes the port it took: the usual one is skipped when a dead game still holds it.
	private static string Base => $"http://127.0.0.1:{PluginPort()}/";

	private static int PluginPort()
	{
		try
		{
			var file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "port");
			if (File.Exists(file) && int.TryParse(File.ReadAllText(file).Trim(), out var port) && port > 0) return port;
		}
		catch { }
		return 7777;
	}

	// The words the game showed come back in a header (GameWords), which on a long page outgrows the usual limit.
	/// <summary>Ctrl-C in a session: every request to the game stops, which ends whatever command is waiting on it.</summary>
	public static CancellationToken Interruption { get; set; }

	private readonly HttpClient _http = new(new HttpClientHandler { MaxResponseHeadersLength = 4096 }) { Timeout = TimeSpan.FromSeconds(120) };

	// FM_TRACE=1 prints every call and how long it took, to see where a command's time goes.
	private static readonly bool Trace = Environment.GetEnvironmentVariable("FM_TRACE") == "1";
	private static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

	/// <summary>
	/// Whether a bridge answers at all, asked briefly: a dying game can hold the port and take
	/// the connection without ever answering (11 Ekim 2026), which the usual long wait would sit on.
	/// </summary>
	public async Task<bool> Answers()
	{
		using var quick = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
		try { return (await quick.GetStringAsync(Base + "health")).Length > 0; }
		catch { return false; }
	}

	public async Task<string> Get(string route, params (string Key, string Value)[] query)
	{
		var url = Base + route;
		if (query.Length > 0)
			url += "?" + string.Join("&", query.Select(q =>
				$"{q.Key}={Uri.EscapeDataString(q.Value)}"));

		try
		{
			var started = Clock.ElapsedMilliseconds;
			using var response = await _http.GetAsync(url, Interruption);
			response.EnsureSuccessStatusCode();
			if (response.Headers.TryGetValues("X-Shown", out var shown)) GameWords.Learn(shown.FirstOrDefault());
			if (response.Headers.TryGetValues("X-Language", out var language)) Labels.Heard(language.FirstOrDefault());
			var body = await response.Content.ReadAsStringAsync();
			if (Trace) Console.Error.WriteLine(L($"[{started / 1000.0,6:0.00}s +{Clock.ElapsedMilliseconds - started,5}ms] {route} {Uri.UnescapeDataString(url[(Base.Length + route.Length)..])}"));
			// The plugin gives up on a request the main thread never ran; read as an answer it said "no
			// save is loaded" of a game that had frozen with one (27 Ekim 2026).
			if (body.StartsWith("timeout: the main thread", StringComparison.Ordinal)) throw new FmBusy();
			return body;
		}
		catch (HttpRequestException)
		{
			throw new FmUnavailable();
		}
		catch (TaskCanceledException) when (Interruption.IsCancellationRequested)
		{
			throw new Interrupted();
		}
		catch (TaskCanceledException)
		{
			// A stalled request is not a missing game: the bridge runs on the game's main
			// thread, so a busy frame looks exactly like a timeout.
			throw new FmBusy();
		}
	}

	// Whether the running plugin takes batches; an older one answers "unknown route" once and is
	// then asked one request at a time.
	private bool? batches;

	/// <summary>
	/// Several requests run by the game in one frame, answers in the same order; null when the
	/// plugin is too old for it. One at a time each waits for a frame of its own (~35 ms), so a
	/// reading of a hundred parts held the live panel for seconds.
	/// </summary>
	public async Task<List<string>?> Batch(IReadOnlyList<(string Route, (string Key, string Value)[] Query)> requests)
	{
		if (batches == false || requests.Count == 0) return requests.Count == 0 ? [] : null;
		var lines = string.Join("\n", requests.Select(r => r.Route + (r.Query.Length > 0
			? "?" + string.Join("&", r.Query.Select(q => $"{q.Key}={Uri.EscapeDataString(q.Value)}"))
			: "")));
		try
		{
			var started = Clock.ElapsedMilliseconds;
			using var response = await _http.PostAsync(Base + "batch", new StringContent(lines, System.Text.Encoding.UTF8, "text/plain"), Interruption);
			if (response.StatusCode == System.Net.HttpStatusCode.NotFound) { batches = false; return null; }
			response.EnsureSuccessStatusCode();
			if (response.Headers.TryGetValues("X-Shown", out var shown)) GameWords.Learn(shown.FirstOrDefault());
			if (response.Headers.TryGetValues("X-Language", out var language)) Labels.Heard(language.FirstOrDefault());
			var body = await response.Content.ReadAsStringAsync();
			if (Trace) Console.Error.WriteLine($"[{started / 1000.0,6:0.00}s +{Clock.ElapsedMilliseconds - started,5}ms] batch ×{requests.Count}");
			if (!body.TrimStart().StartsWith('[')) { batches = false; return null; }
			batches = true;
			return JsonSerializer.Deserialize<List<string>>(body);
		}
		catch (HttpRequestException) { throw new FmUnavailable(); }
		catch (TaskCanceledException) when (Interruption.IsCancellationRequested) { throw new Interrupted(); }
		catch (TaskCanceledException) { throw new FmBusy(); }
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
	private static int pulls;

	/// <param name="shown">Properties to have in the game's own words rather than English.</param>
	public async Task<JsonElement> Pull(string path, string props, bool list = false, string shown = "")
	{
		var wanted = props.Split(',').Length;
		// A list's entries are put at rows of their own for this reading.
		var generation = list ? Interlocked.Increment(ref pulls).ToString() + DateTime.UtcNow.Ticks.ToString()[^6..] : "";
		JsonElement data = default;
		for (var attempt = 0; attempt < 6; attempt++)
		{
			// A list bound by this very call holds nothing until the next frame.
			try { data = await GetJson("pull", ("path", path), ("props", props), ("list", list ? "1" : "0"), ("gen", generation), ("shown", shown)); }
			catch (FmError e) when (attempt < 5 && e.Message.Contains("is not a list", StringComparison.Ordinal))
			{
				await Task.Delay(TimeSpan.FromMilliseconds(250));
				continue;
			}
			var records = data.ValueKind == JsonValueKind.Array ? data.EnumerateArray().ToList() : [data];
			var complete = records.All(r => r.EnumerateObject().Count(p => p.Name is not ("Index" or "Row")) >= wanted);
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

	/// <summary>A list of plain strings at a path, bound first if need be: the board's praise, one sentence each.</summary>
	public async Task<List<string>> Strings(string path)
	{
		await Get("pathbind", ("path", path));
		for (var attempt = 0; attempt < 4; attempt++)
		{
			var listed = await Get("pathlist", ("path", path));
			if (!listed.StartsWith("error", StringComparison.Ordinal))
				return listed.Split('\n')
					.Select(line => line.IndexOf("(String) ", StringComparison.Ordinal) is var at and >= 0 ? line[(at + 9)..] : null)
					// Each entry ends with its type in brackets.
					.Select(text => text?[..(text.LastIndexOf("  [", StringComparison.Ordinal) is var end and >= 0 ? end : text.Length)].Trim())
					.Where(text => !string.IsNullOrEmpty(text))
					.Select(text => text!)
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

		static string Text(JsonElement e, string name) => JsonRead.Text(e, name);
	}

	/// <summary>True once a save is loaded, rather than merely the game being up.</summary>
	public async Task<bool> InGame()
	{
		var health = await Get("health");
		if (!health.Contains("ok")) return false;

		// The health line carries the game and plugin versions too; only the node count is read.
		var line = health.Split('\n').FirstOrDefault(l => l.StartsWith("descendants:", StringComparison.Ordinal));
		var digits = new string((line ?? "").Where(char.IsDigit).ToArray());
		if (int.TryParse(digits, out var nodes) && nodes > 1000) return true;

		// A match runs in a scene of its own with only a few hundred nodes bound, far fewer
		// than the main menu has; what the menu lacks is a game date. FM27's match-day screens
		// (team sheet, squad numbers…) bind no date either, but the in-game Continue's flow
		// ("State") is bound only in a save, whatever screen is up.
		var state = await Get("state");
		return state.Contains("\"Date\"") || state.Contains("\"State\":", StringComparison.Ordinal);
	}
}

internal sealed class FmUnavailable : Exception;

internal sealed class FmBusy : Exception;

internal sealed class FmError(string text) : Exception(text);
