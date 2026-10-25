namespace FmCli;

internal static partial class Program
{
	private enum SettingKind { Dropdown, Radio, Slider, Checkbox }

	/// <summary>
	/// One preference: what it is called here, which tab holds it and the heading above it.
	/// Two lists on the saves block share one heading row, so those are told apart by a choice
	/// only they offer. A list is found by the block the template puts it in (<see cref="Block"/>)
	/// rather than by its heading, which is the game's own words and changes with the language.
	/// </summary>
	private sealed record Setting(
		string Name, string Group, string Tab, string Heading, SettingKind Kind,
		Dictionary<string, string>? Turkish = null, string[]? Steps = null, string? Offers = null, string? Hint = null,
		string? Block = null);

	private static readonly Dictionary<string, string> HighlightTurkish = new()
	{
		["Full Match"] = L("Full Match"), ["Comprehensive"] = L("Comprehensive"), ["Extended"] = L("Extended"), ["Key"] = L("Key"),
		["Only Commentary"] = L("Only Commentary"),
	};

	private static readonly Dictionary<string, string> IntervalTurkish = new()
	{
		["Day"] = L("Day"), ["Week"] = L("Week"), ["Fortnight"] = L("Fortnight"), ["Month"] = L("every month"),
		["Three Months"] = L("Three Months"), ["Year"] = L("Year"), ["Five Years"] = L("Five Years"),
		["After Matches"] = L("After Matches"), ["30 Minutes (Real Time)"] = L("30 Minutes (Real Time)"),
		["60 Minutes (Real Time)"] = L("60 Minutes (Real Time)"),
	};

	private static readonly Dictionary<string, string> SaveTypeTurkish = new()
	{
		["Single File"] = L("Single File"), ["Incremental Auto Save"] = L("Incremental Auto Save"),
		["3 File Rolling Auto Save"] = L("3 File Rolling Auto Save"), ["5 File Rolling Auto Save"] = L("5 File Rolling Auto Save"),
		["10 File Rolling Auto Save"] = L("10 File Rolling Auto Save"),
	};

	private static readonly Dictionary<string, string> UnitTurkish = new()
	{
		["Weekly"] = L("Weekly"), ["Monthly"] = L("Monthly"), ["Yearly"] = L("Yearly"),
		["Celsius"] = L("Celsius"), ["Fahrenheit"] = L("Fahrenheit"), ["Kelvin"] = "Kelvin",
		["Centimetres"] = L("Centimetres"), ["Metres"] = L("Metres"), ["Feet"] = "Fit",
		["Yards"] = L("Yards"), ["Miles"] = "Mil", ["Kilometres"] = L("Kilometres"),
	};

	/// <summary>
	/// The container each tab's button sits in: the template's own name, which no translation
	/// touches, so the tab is opened by it rather than by the words on it — a Turkish game
	/// labels the tab "Matchday" where the English one says "Match Day".
	/// </summary>
	private static readonly Dictionary<string, string> TabButtons = new()
	{
		["Match Day"] = "MatchDayTabButton",
		["Your World"] = "YourWorldTabButton",
		["Processing"] = "ProcessingTabButton",
	};

	private static readonly Setting[] Settings =
	[
		new(L("match display"), L("Match day"), "Match Day", "Dynamic Mode", SettingKind.Radio, HighlightTurkish,
			Hint: L("fewer highlights make the match shorter")),
		new(L("dynamic mode"), L("Match day"), "Match Day", "Dynamic Mode", SettingKind.Checkbox,
			Hint: L("the game chooses the highlights")),
		// The slider's steps as the page labels them, read off it one step at a time.
		new(L("highlight speed"), L("Match day"), "Match Day", "Match Speed During Highlights", SettingKind.Slider,
			Steps: ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "1.75x", "2x", "2.5x", "3x", "4x", "10x"],
			Hint: L("while a move is played")),
		new(L("speed between highlights"), L("Match day"), "Match Day", "Match Speed Between Highlights", SettingKind.Slider,
			Steps: ["4x", "8x", "10x", "20x", "30x", "40x"],
			Hint: L("between moves")),
		new(L("auto save"), L("Saving"), "Your World", "Use Auto Saves", SettingKind.Checkbox),
		// The name of the block a list sits in, which the template gives it and no translation
		// touches; the game's own words for a list can differ from the English one.
		new(L("save frequency"), L("Saving"), "Your World", "Every", SettingKind.Dropdown, IntervalTurkish, Offers: "Fortnight",
			Block: "AutoSaveIntervalSetting"),
		new(L("save type"), L("Saving"), "Your World", "Auto save type", SettingKind.Dropdown, SaveTypeTurkish, Offers: "Single File",
			Block: "AutoSaveTypeSetting"),
		new(L("language"), L("Region and units"), "Your World", "Game Language", SettingKind.Dropdown,
			Hint: L("the game's and fm's texts in this language"), Block: "GameLanguagePreferencesBlock"),
		new(L("currency"), L("Region and units"), "Your World", "Currency", SettingKind.Dropdown,
			Block: "CurrencyPreferencesBlock"),
		new(L("wages"), L("Region and units"), "Your World", "Wages", SettingKind.Dropdown, UnitTurkish,
			Block: "WagesPreferencesBlock"),
		new(L("date format"), L("Region and units"), "Your World", "Format", SettingKind.Dropdown,
			Block: "FormatPreferencesBlock"),
		new(L("temperature"), L("Region and units"), "Your World", "Temperature", SettingKind.Dropdown, UnitTurkish,
			Block: "TemperaturePreferencesBlock"),
		new(L("height"), L("Region and units"), "Your World", "Height", SettingKind.Dropdown, UnitTurkish,
			Block: "HeightPreferencesBlock"),
		new(L("short distance"), L("Region and units"), "Your World", "Short Distance", SettingKind.Dropdown, UnitTurkish,
			Block: "ShortDistancePreferencesBlock"),
		new(L("long distance"), L("Region and units"), "Your World", "Long Distance", SettingKind.Dropdown, UnitTurkish,
			Block: "LongDistancePreferencesBlock"),
		new(L("odds format"), L("Region and units"), "Your World", "Match Odds", SettingKind.Dropdown,
			Block: "MatchOddsPreferencesBlock"),
		// The Processing tab puts every one of its lists in the same block, so those keep to their
		// headings; so does a slider, a checkbox and the radio group, which the game does not report
		// a block for.
		new(L("other matches' speed"), L("Processing"), "Processing", "Speed to Process Other Teams' Matches", SettingKind.Dropdown,
			Hint: L("Fast: days move on quicker")),
		new(L("match scheduling"), L("Processing"), "Processing", "Match Scheduling Options", SettingKind.Dropdown,
			Hint: L("whether matches are moved for broadcasting")),
		new(L("no continuing unread"), L("Processing"), "Processing", "Continue Button Legacy Mode", SettingKind.Checkbox,
			Hint: L("Continue does not work until every message is read")),
	];

	/// <summary>A setting as found on its page: the control, its choices and which one is in force.</summary>
	private sealed record SettingNow(Setting Setting, string Pointer, List<string> Choices, int Current, bool Enabled);
}
