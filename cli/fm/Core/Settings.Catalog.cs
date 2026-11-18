namespace FmCli;

internal static partial class Program
{
	private enum SettingKind { Dropdown, Radio, Slider, Checkbox }

	/// <summary>
	/// One preference: what it is called here and the game's own key for it (<see cref="Pref"/>):
	/// the name a game preference's block binds beside its control (auto_save_interval), or the
	/// display settings' field it is bound to (settings.FrameRate). The same in every language;
	/// the control is found by it, never by the heading above it.
	/// </summary>
	/// <param name="Steps">A slider's steps as the page labels them, read off it one step at a time.</param>
	/// <param name="StepShown">Where the game keeps the label of the step in force, which checks <paramref name="Steps"/>.</param>
	private sealed record Setting(
		string Name, string Group, string Pref, SettingKind Kind,
		string[]? Steps = null, string? StepShown = null, string? Hint = null);

	private static readonly Setting[] Settings =
	[
		new(L("match display"), L("Match day"), "settings.MatchHighlightMode", SettingKind.Radio,
			Hint: L("fewer highlights make the match shorter")),
		new(L("dynamic mode"), L("Match day"), "settings.MatchDynamicHighlightMode", SettingKind.Checkbox,
			Hint: L("the game chooses the highlights")),
		new(L("highlight speed"), L("Match day"), "settings.LiveMatchSpeedDuringHighlightsIndex", SettingKind.Slider,
			Steps: ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "1.75x", "2x", "2.5x", "3x", "4x", "10x"],
			StepShown: "settings.LiveMatchSpeedDuringHighlights", Hint: L("while a move is played")),
		new(L("speed between highlights"), L("Match day"), "settings.MatchSpeedBetweenHighlightsIndex", SettingKind.Slider,
			Steps: ["4x", "8x", "10x", "20x", "30x", "40x"],
			StepShown: "settings.MatchSpeedBetweenHighlights", Hint: L("between moves")),
		new(L("sound"), L("Sound"), "settings.AudioMasterEnabled", SettingKind.Checkbox,
			Hint: L("all of the game's sound")),
		new(L("auto save"), L("Saving"), "use_auto_saves", SettingKind.Checkbox),
		new(L("save frequency"), L("Saving"), "auto_save_interval", SettingKind.Dropdown),
		new(L("save type"), L("Saving"), "auto_save_mode", SettingKind.Dropdown),
		new(L("language"), L("Region and units"), "current_language", SettingKind.Dropdown,
			Hint: L("the game's and fm's texts in this language")),
		new(L("currency"), L("Region and units"), "selected_currency", SettingKind.Dropdown),
		new(L("wages"), L("Region and units"), "wage_string_type", SettingKind.Dropdown),
		new(L("date format"), L("Region and units"), "date_format", SettingKind.Dropdown),
		new(L("temperature"), L("Region and units"), "current_temperature", SettingKind.Dropdown),
		new(L("height"), L("Region and units"), "height_scale", SettingKind.Dropdown),
		new(L("short distance"), L("Region and units"), "short_distance_scale", SettingKind.Dropdown),
		new(L("long distance"), L("Region and units"), "long_distance_scale", SettingKind.Dropdown),
		new(L("continue flow"), L("Processing"), "continue_flow_preset", SettingKind.Dropdown,
			Hint: L("how often Continue stops to show you things")),
		new(L("match scheduling"), L("Processing"), "match_scheduling_mode", SettingKind.Dropdown,
			Hint: L("whether matches are moved for broadcasting")),
		new(L("unread messages"), L("Processing"), "continue_button_cycle_messages", SettingKind.Dropdown,
			Hint: L("whether Continue waits until every message is read")),
	];

	/// <summary>A setting as found on its page: the control, its choices and which one is in force.</summary>
	/// <param name="Choices">As the game shows them, in its language.</param>
	/// <param name="Source">The same choices in English, for matching what is typed.</param>
	/// <param name="Label">What the list shows for the row in force, where its rows are bare numbers.</param>
	/// <param name="Ids">The game's own word for each choice where it has one ("turkish"), the same in every language.</param>
	private sealed record SettingNow(Setting Setting, string Pointer, List<string> Choices, List<string> Source, int Current, bool Enabled, List<string>? Ids = null, string? Label = null);
}
