using System.Text.Json;

namespace FmCli;

/// <summary>
/// Every preference on the game's All Preferences list, not only the ones <see cref="Settings"/>
/// names: each held by the game's own key for it (<see cref="ControlKey"/>), shown under the
/// game's own title for it, grouped by the block of the page it sits in.
/// </summary>
internal static partial class Program
{
	/// <summary>The page's blocks (by their element names) and the group each is listed under.</summary>
	private static readonly Dictionary<string, string> PreferenceBlocks = new()
	{
		["GameLanguagePreferencesBlock"] = "Region and units", ["DataLanguagePreferencesBlock"] = "Region and units",
		["CurrencyPreferencesBlock"] = "Region and units", ["WagesPreferencesBlock"] = "Region and units",
		["TemperaturePreferencesBlock"] = "Region and units", ["HeightPreferencesBlock"] = "Region and units",
		["ShortDistancePreferencesBlock"] = "Region and units", ["LongDistancePreferencesBlock"] = "Region and units",
		["FormatPreferencesBlock"] = "Region and units", ["SeparatorPreferencesBlock"] = "Region and units",
		["PositivePreferencesBlock"] = "Region and units", ["NegativePreferencesBlock"] = "Region and units",
		["DecimalSymbolPreferencesBlock"] = "Region and units", ["DigitGroupingSymbolPreferencesBlock"] = "Region and units",
		["MatchStorageSpacePreferencesBlock"] = "Saving", ["SaveLocationPreferencesBlock"] = "Saving",
		["AutoSavePreferencesBlock"] = "Saving",
		["SortAttributesPreferencesBlock"] = "Interface", ["GameEditorMenuBarPreferencesBlock"] = "Interface",
		["DisplayPositionAndRolePreferencesBlock"] = "Interface", ["ShortcutsPreferencesBlock"] = "Interface",
		["WindowModeResolutionPreferencesBlock"] = "Interface", ["HyperlinksPreferencesBlock"] = "Interface",
		["DisplayPlayerInfoPreferencesBlock"] = "Interface", ["ShowScreenIDsPreferencesBlock"] = "Interface",
		["ColorBlindModePreferencesBlock"] = "Interface",
		["SoundsPreferencesBlock"] = "Sound",
		["In-machPreferencesBlock"] = "Match day", ["HighlightsPreferencesBlock"] = "Match day",
		["3DPreferencesBlock"] = "Graphics",
		["MatchSchedulingOptionsPreferencesBlock"] = "Processing", ["ProcessingPreferencesBlock"] = "Processing",
		["LandingPagePreferencesBlock"] = "Landing pages",
	};

	/// <summary>Preferences the page gives no title of their own to find: named here, by their keys.</summary>
	private static readonly Setting[] UntitledSettings =
	[
		new(L("past match speed"), L("Match day"), "settings.PastMatchSpeedDuringHighlightsIndex", SettingKind.Slider,
			Steps: ["0.5x", "0.75x", "1x", "1.25x", "1.5x", "1.75x", "2x", "2.5x", "3x", "4x", "10x"],
			StepShown: "settings.PastMatchSpeedDuringHighlights"),
		new(L("manual saves use the auto save type"), L("Saving"), "use_auto_save_mode_for_manual_save", SettingKind.Checkbox),
	];

	/// <summary>
	/// A list whose rows are bare numbers (the 3D lists, colour blind mode) with the words the game
	/// shows for them, which exist only on the rows it lays out when the list is open: opened,
	/// read once every row is there, closed. Any other list comes back as it is.
	/// </summary>
	private static async Task<SettingNow> WithRowWords(SettingNow now)
	{
		if (now.Setting.Kind != SettingKind.Dropdown || !now.Enabled || !BareNumbers(now.Choices)) return now;
		await Fm.Get("submenu", ("ptr", now.Pointer));
		var words = new Dictionary<int, string>();
		await Until(async () =>
		{
			words = Rows(await Fm.Get("dropdownrows"));
			return words.Count == now.Choices.Count;
		}, seconds: 5, every: 0.1);
		// The same rows in the game's language: what is shown; the English above is matched too.
		var inGame = Rows(await Fm.Get("dropdownrows", ("shown", "1")));
		await Fm.Get("submenu", ("ptr", now.Pointer), ("close", "1"));
		await Until(async () => !(await Fm.Get("dropdownrows")).Contains('#'), seconds: 3, every: 0.1);
		if (words.Count != now.Choices.Count) return now;
		var source = Enumerable.Range(0, now.Choices.Count).Select(i => words.GetValueOrDefault(i, now.Choices[i])).ToList();
		var shown = Enumerable.Range(0, now.Choices.Count).Select(i => inGame.GetValueOrDefault(i, source[i])).ToList();
		return now with { Choices = shown, Source = source };

		static Dictionary<int, string> Rows(string listed) =>
			System.Text.RegularExpressions.Regex.Matches(listed, "#(\\d+)\\s+\"(.*)\"").ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
	}

	private static bool BareNumbers(List<string> choices) => choices.Count > 0 && choices.All(c => int.TryParse(c, out _));

	/// <summary>Element names every control sits in, which say nothing of which one it is.</summary>
	private static readonly HashSet<string> PlainNames =
	[
		"DropdownChevron", "TemplateContainer", "RightFrame", "Remapper", "Dropdown", "Prefs", "View", "setting_block",
		"MainContainer", "binding-expect", "inputs-switch-toggle-default-template", "inputs-switch-toggle-label",
		"inputs-switch-toggle-label-0", "unity-content-container", "unity-content-viewport", "Block",
		"unity-content-and-vertical-scroll-container",
	];

	private static List<string> NamesOf(JsonElement control) =>
		control.TryGetProperty("Names", out var n) && n.ValueKind == JsonValueKind.Array ? n.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
		: control.TryGetProperty("Within", out var w) && w.ValueKind == JsonValueKind.Array ? w.EnumerateArray().Select(x => x.GetString() ?? "").ToList()
		: [];

	/// <summary>The names a control sits in inside its block, nearest first; the block's own last.</summary>
	private static List<string> InBlock(JsonElement control)
	{
		var names = NamesOf(control);
		var block = names.FindIndex(n => n.EndsWith("PreferencesBlock", StringComparison.Ordinal));
		return block < 0 ? names : names.Take(block + 1).ToList();
	}

	/// <summary>
	/// The game's own key for a control on the preferences, the same in every language: its
	/// preference key when no other control has that key; else the element that sets it
	/// (BindingSetter-IntroMusic: three sound switches write one field); else the page a landing
	/// list is for (its hub's Guid); else the nearest named element of its own (GraphicsQuality).
	/// Empty when it has none: such a control is not listed.
	/// </summary>
	private static string ControlKey(JsonElement control, List<JsonElement> form)
	{
		var pref = Field(control, "Pref");
		var own = InBlock(control);
		if (own.FirstOrDefault(n => n.StartsWith("BindingSetter-", StringComparison.Ordinal)) is { } setter) return setter["BindingSetter-".Length..];
		if (pref.Length > 0 && form.Count(f => Field(f, "Pref") == pref) == 1) return pref;
		if (Field(control, "Section") is { Length: > 0 } section) return "landing." + section;
		return own.FirstOrDefault(n => !PlainNames.Contains(n) && !n.EndsWith("PreferencesBlock", StringComparison.Ordinal)
			&& !n.StartsWith("setting_line", StringComparison.Ordinal) && !n.StartsWith("dropdown-", StringComparison.Ordinal)) ?? pref;
	}

	/// <summary>Every control on the read page as a setting: its key, its title as the game shows it, its group.</summary>
	private static List<Setting> AllSettings(List<JsonElement> form)
	{
		var all = new List<Setting>();
		foreach (var control in form)
		{
			if (!Enum.TryParse<SettingKind>(Field(control, "Kind"), ignoreCase: true, out var kind)) continue;
			if (Field(control, "Visible") == "False") continue;
			// The named settings keep their names and their slider steps.
			if (Settings.Concat(UntitledSettings).FirstOrDefault(s => s.Pref == Field(control, "Pref")) is { } named)
			{
				if (!all.Contains(named)) all.Add(named);
				continue;
			}
			var key = ControlKey(control, form);
			if (key.Length == 0 || all.Any(s => s.Pref == key)) continue;
			// The nearest block with a group: some blocks sit inside a larger one.
			var block = NamesOf(control).FirstOrDefault(PreferenceBlocks.ContainsKey) ?? "";
			var group = L(PreferenceBlocks.GetValueOrDefault(block, "Other"));
			var title = Field(control, "Title");
			all.Add(new(title.Length > 0 ? title : key, group, key, kind));
		}
		return all;
	}

	/// <summary>A setting off the whole list by what was typed: its key whole, else its title whole, else by its start.</summary>
	private static (Setting? Setting, string? Value, List<Setting> Close) FindInAll(List<Setting> all, List<string> words)
	{
		List<Setting> close = [];
		for (var take = words.Count; take >= 1; take--)
		{
			var name = Fold(string.Join(" ", words.Take(take)));
			var value = take < words.Count ? string.Join(" ", words.Skip(take)) : null;
			var hits = all.Where(s => Fold(s.Pref) == name).ToList();
			if (hits.Count == 0) hits = all.Where(s => Fold(s.Name) == name).ToList();
			if (hits.Count == 0) hits = all.Where(s => Fold(s.Name).StartsWith(name, StringComparison.Ordinal)).ToList();
			if (hits.Count == 1) return (hits[0], value, []);
			if (hits.Count > 1 && close.Count == 0) close = hits;
		}
		return (null, null, close);
	}
}
