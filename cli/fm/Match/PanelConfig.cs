using System.Text.Json;
using System.Text.Json.Serialization;

namespace FmCli;

/// <summary>
/// The live match panel's own settings, kept apart from the game's preferences:
/// view (full / key moments / score only), the pitch, how many events, colours, and the radar and its style.
/// </summary>
internal sealed record PanelConfig(string View, bool Pitch, int Events, bool Colour, bool Radar, string RadarStyle = "auto")
{
	public static PanelConfig Default { get; } = new("full", true, 8, true, true, "auto");

	public static string File => Path.Combine(
		Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fm-cli", "panel.json");

	/// <summary>The saved settings, or the defaults when the file is missing or unreadable.</summary>
	public static PanelConfig Load()
	{
		try
		{
			var json = System.IO.File.ReadAllText(File);
			var saved = JsonSerializer.Deserialize<PanelConfig>(json);
			if (saved == null) return Default;
			// A panel.json from before the radar has no Radar key at all; the default is on,
			// and a missing bool would otherwise read as off.
			using var document = JsonDocument.Parse(json);
			var hasRadar = document.RootElement.TryGetProperty("Radar", out _);
			return saved with
			{
				View = ParseView(saved.View) ?? Default.View,
				Events = Math.Clamp(saved.Events, 0, 12),
				Radar = hasRadar ? saved.Radar : Default.Radar,
				RadarStyle = Program.ParseRadarStyle(saved.RadarStyle ?? "") ?? Default.RadarStyle,
			};
		}
		catch { return Default; }
	}

	public static void Save(PanelConfig config)
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(File)!);
			System.IO.File.WriteAllText(File, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
		}
		catch { }
	}

	/// <summary>"full", "key" or "score", in the words the command accepts.</summary>
	public static string? ParseView(string text) => text.Trim().ToLowerInvariant() switch
	{
		"full" or "all" or "tam" => "full",
		"key" or "keymoments" or "key-moments" or "onemli" => "key",
		"score" or "scoreonly" or "score-only" => "score",
		_ => null,
	};

	/// <summary>The view's place in the `v` cycle.</summary>
	[JsonIgnore]
	public int ViewIndex => View switch { "key" => 1, "score" => 2, _ => 0 };

}
