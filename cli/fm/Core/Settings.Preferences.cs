using System.Text.Json;

namespace FmCli;

internal static partial class Program
{
	/// <summary>
	/// The FM menu's settings tile, by the entry the game files it under (`PreferencesFMMenu` in
	/// the event's source) rather than by the words on it, which are this language's. Empty while
	/// the menu is away.
	/// </summary>
	private static async Task<string?> PreferencesTile()
	{
		var listed = await Fm.Get("buttons", ("type", "OpenPanelEventContext"), ("limit", "1000"));
		string? pointer = null;
		foreach (var line in listed.Split('\n'))
		{
			if (line.StartsWith("0x", StringComparison.Ordinal)) pointer = line.Split(' ')[0];
			else if (pointer != null && line.Contains("PreferencesFMMenu", StringComparison.Ordinal)) return pointer;
		}
		return null;
	}

	/// <summary>
	/// Opens one tab of the full preferences and returns what it shows, in reading order: from
	/// wherever the game is, through the FM menu, its Preferences tile and "See all".
	/// </summary>
	private static async Task<List<JsonElement>> PreferencesTab(string tab)
	{
		async Task<bool> Showing(string text) => (await Fm.GetJson("texts", ("anchor", text), ("up", "0"))).GetArrayLength() > 0;

		// Through the FM menu, its Preferences tile and "See all". Now and then a step's click lands
		// before its target listens (the first time in a session most often), so the way is walked
		// again once before giving up.
		for (var attempt = 0; attempt < 2 && !await Showing("All Preferences"); attempt++)
		{
			if (await PreferencesTile() is not { } tile)
			{
				var menu = (await Fm.GetJson("buttons", ("json", "1"))).EnumerateArray().FirstOrDefault(b => Field(b, "Name") == "dashboard-hud-fm-button");
				if (menu!.ValueKind != JsonValueKind.Object)
				{
					Console.Error.WriteLine(await FmMenuMissing());
					return null;
				}
				await Fm.Get("click", ("ptr", Field(menu, "Pointer")));
				await Until(async () => await PreferencesTile() != null, seconds: 8, every: 0.25);
				tile = await PreferencesTile();
			}
			if (tile == null)
			{
				Console.Error.WriteLine(L("Settings tile not found in the FM menu."));
				return null;
			}
			await Fm.Get("click", ("ptr", tile));
			// The link reads "See all"; through a Turkish game's table it comes back "See All", whose
			// translation is the same.
			var seeAll = "See all";
			await Until(async () => await Showing(seeAll = "See all") || await Showing(seeAll = "See All"), seconds: 8, every: 0.25);
			await Fm.Get("click", ("text", seeAll));
			await Until(() => Showing("All Preferences"), seconds: 8, every: 0.25);
		}
		if (!await Showing("All Preferences"))
		{
			Console.Error.WriteLine(L("The settings page did not open."));
			return null;
		}

		// The tab's own label is the game's words ("Matchday" through a Turkish game); its button
		// is taken by the container it sits in, whose template name no language changes.
		if (!TabButtons.TryGetValue(tab, out var tabButton))
		{
			Console.Error.WriteLine(Lf("The {0} tab in the settings could not be read.", tab));
			return null;
		}
		await Fm.Get("click", ("within", tabButton), ("name", "DetailedPreferencesTabButton"));
		// The tab fills in over a few frames: its headings can be there before the lists under them.
		List<JsonElement> form = [];
		var read = "";
		await Until(async () =>
		{
			form = (await Fm.GetJson("form", ("anchor", "All Preferences"), ("up", "6"))).EnumerateArray().ToList();
			// A list can be on the page before its choices are (seen on Processing: no choices,
			// nothing in force), and what is there is only trusted once it reads the same twice.
			var whole = Settings.Where(s => s.Tab == tab).All(s => Locate(s, form) is { } now
				&& (s.Kind != SettingKind.Dropdown || (now.Choices.Count > 0 && now.Current >= 0)));
			var key = string.Join("|", form.Select(f => f.GetRawText()));
			var settled = whole && key == read;
			read = key;
			return settled;
		}, seconds: 8, every: 0.25);
		if (form.Count == 0) Console.Error.WriteLine(L($"The {tab} tab in the settings could not be read."));
		return form.Count == 0 ? null : form;
	}

	/// <summary>Closes the preferences with nothing changed.</summary>
	private static async Task LeavePreferences()
	{
		async Task<bool> Open() => (await Fm.GetJson("texts", ("anchor", "All Preferences"), ("up", "0"))).GetArrayLength() > 0;
		if (!await Open()) return;
		// Its close button, which drops whatever was left unconfirmed (CancelPreferenceChanges).
		await Fm.Get("press", ("event", "CancelPreferenceChanges"));

		// The page fades out over a moment; a command that starts before it is gone finds a
		// page that stops answering under it.
		await Until(async () => !await Open(), seconds: 5, every: 0.25);
	}

	/// <summary>The control under a setting's heading, or the list that offers its telltale choice.</summary>

	/// <summary>Why the game's FM menu is not there: most often a screen with no menus at all (the results after a match).</summary>
	private static async Task<string?> FmMenuMissing() =>
		(await Fm.GetJson("pages")).GetArrayLength() == 0
			? Lf("The game is on a screen without a menu ({0}); the FM menu cannot be opened. To go past it: next", Field(await Fm.GetJson("state"), "State"))
			: L("The game's FM menu was not found.");
}
